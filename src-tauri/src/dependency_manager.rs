use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256};
use std::collections::{HashMap, HashSet};
use std::fs::{self, File};
use std::io::{self, Read, Write};
use std::path::{Component, Path, PathBuf};
use std::sync::{Arc, Mutex};
use std::thread;
use std::time::{SystemTime, UNIX_EPOCH};
use uuid::Uuid;
use zip::ZipArchive;

const MANAGED_MARKER: &str = ".roadwatcher-managed-component.json";
const ALLOWED_DOWNLOAD_HOSTS: &[&str] = &[
    "github.com",
    "objects.githubusercontent.com",
    "release-assets.githubusercontent.com",
    "releases.astral.sh",
    "files.pythonhosted.org",
    "download.osgeo.org",
    "www.gyan.dev",
];

#[derive(Clone, Debug, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct DependencyCatalog {
    pub schema_version: u32,
    pub platform: String,
    pub catalog_version: String,
    pub components: Vec<DependencyComponent>,
}

#[derive(Clone, Debug, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct DependencyComponent {
    pub id: String,
    pub label: String,
    pub version: String,
    pub purpose: String,
    pub required: bool,
    pub recommended: bool,
    pub license: DependencyLicense,
    pub source_url: String,
    pub availability: String,
    pub artifact: Option<DependencyArtifact>,
    pub dependencies: Vec<String>,
}

#[derive(Clone, Debug, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct DependencyLicense {
    pub id: String,
    pub label: String,
    pub url: String,
    pub digest: String,
    pub consent_required: bool,
}

#[derive(Clone, Debug, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct DependencyArtifact {
    pub url: String,
    pub sha256: String,
    pub max_bytes: u64,
    pub archive: String,
    pub file_name: Option<String>,
}

#[derive(Clone, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct DependencyCatalogResponse {
    pub schema_version: u32,
    pub platform: String,
    pub catalog_version: String,
    pub components: Vec<DependencyComponentStatus>,
}

#[derive(Clone, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct DependencyComponentStatus {
    #[serde(flatten)]
    pub component: DependencyComponent,
    pub state: String,
    pub install_path: String,
    pub update_available: bool,
    pub detail: String,
}

#[derive(Clone, Debug, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct DependencyInstallJob {
    pub job_id: String,
    pub status: String,
    pub progress: u8,
    pub detail: String,
    pub component_ids: Vec<String>,
    pub current_component_id: String,
}

#[derive(Clone, Debug)]
pub struct ManagedComponentIdentity {
    pub version: String,
    pub artifact_sha256: String,
}

#[derive(Clone, Debug, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
struct ManagedComponentMarker {
    id: String,
    version: String,
    artifact_sha256: String,
    source_url: String,
    installed_at_unix: u64,
}

#[derive(Clone)]
pub struct DependencyManager {
    catalog: Arc<DependencyCatalog>,
    root: PathBuf,
    jobs: Arc<Mutex<HashMap<String, DependencyInstallJob>>>,
    cancellations: Arc<Mutex<HashSet<String>>>,
}

impl DependencyManager {
    pub fn load(catalog_path: &Path, app_local_data: &Path) -> Result<Self, String> {
        let text = fs::read_to_string(catalog_path)
            .map_err(|error| format!("could not read dependency catalog: {error}"))?;
        let catalog: DependencyCatalog = serde_json::from_str(&text)
            .map_err(|error| format!("dependency catalog is invalid JSON: {error}"))?;
        validate_catalog(&catalog)?;
        let manager = Self {
            catalog: Arc::new(catalog),
            root: app_local_data.join("managed-components"),
            jobs: Arc::new(Mutex::new(HashMap::new())),
            cancellations: Arc::new(Mutex::new(HashSet::new())),
        };
        manager.recover_interrupted_state()?;
        Ok(manager)
    }

    #[cfg(test)]
    fn from_catalog(catalog: DependencyCatalog, root: PathBuf) -> Result<Self, String> {
        validate_catalog(&catalog)?;
        Ok(Self {
            catalog: Arc::new(catalog),
            root,
            jobs: Arc::new(Mutex::new(HashMap::new())),
            cancellations: Arc::new(Mutex::new(HashSet::new())),
        })
    }

    pub fn catalog(&self) -> DependencyCatalogResponse {
        DependencyCatalogResponse {
            schema_version: self.catalog.schema_version,
            platform: self.catalog.platform.clone(),
            catalog_version: self.catalog.catalog_version.clone(),
            components: self
                .catalog
                .components
                .iter()
                .map(|component| self.component_status(component))
                .collect(),
        }
    }

    pub fn start(
        &self,
        component_ids: Vec<String>,
        accepted_license_digests: Vec<String>,
    ) -> Result<DependencyInstallJob, String> {
        if component_ids.is_empty() {
            return Err("Select at least one catalog component to install.".to_string());
        }
        let mut seen = HashSet::new();
        let accepted = accepted_license_digests.into_iter().collect::<HashSet<_>>();
        for id in &component_ids {
            if !seen.insert(id.clone()) {
                return Err(format!(
                    "Dependency install request repeats component {id}."
                ));
            }
            let component = self.component(id)?;
            if component.availability != "available" || component.artifact.is_none() {
                return Err(format!(
                    "{} is not installable yet: its exact artifact and integrity evidence are still {}.",
                    component.label, component.availability
                ));
            }
            if component.license.consent_required && !accepted.contains(&component.license.digest) {
                return Err(format!(
                    "License consent for {} does not match the catalog digest.",
                    component.label
                ));
            }
            for dependency in &component.dependencies {
                if !component_ids.contains(dependency) && !self.is_ready(dependency) {
                    return Err(format!(
                        "{} requires {} to be installed or included in the same request.",
                        component.label, dependency
                    ));
                }
            }
        }

        let job = DependencyInstallJob {
            job_id: Uuid::new_v4().to_string(),
            status: "queued".to_string(),
            progress: 0,
            detail: "Dependency installation queued.".to_string(),
            component_ids,
            current_component_id: String::new(),
        };
        self.jobs
            .lock()
            .map_err(lock_error)?
            .insert(job.job_id.clone(), job.clone());
        self.persist_job(&job)?;
        let manager = self.clone();
        let job_id = job.job_id.clone();
        thread::spawn(move || manager.run_job(&job_id));
        Ok(job)
    }

    pub fn status(&self, job_id: &str) -> Result<DependencyInstallJob, String> {
        self.jobs
            .lock()
            .map_err(lock_error)?
            .get(job_id)
            .cloned()
            .ok_or_else(|| "Dependency installation job was not found.".to_string())
    }

    pub fn cancel(&self, job_id: &str) -> Result<DependencyInstallJob, String> {
        let current = self.status(job_id)?;
        if matches!(current.status.as_str(), "ready" | "failed" | "cancelled") {
            return Ok(current);
        }
        self.cancellations
            .lock()
            .map_err(lock_error)?
            .insert(job_id.to_string());
        self.update_job(job_id, |job| {
            job.detail = "Cancellation requested; the current bounded operation will stop safely."
                .to_string();
        });
        self.status(job_id)
    }

    pub fn remove(&self, component_id: &str) -> Result<DependencyComponentStatus, String> {
        let component = self.component(component_id)?.clone();
        let target = self.component_path(&component);
        if !target.exists() {
            return Ok(self.component_status(&component));
        }
        let marker = read_marker(&target)?;
        if marker.id != component.id || marker.version != component.version {
            return Err(
                "Refusing to remove a directory without the exact RoadWatcher ownership marker."
                    .to_string(),
            );
        }
        fs::remove_dir_all(&target)
            .map_err(|error| format!("could not remove managed component: {error}"))?;
        Ok(self.component_status(&component))
    }

    pub fn managed_component_path(&self, component_id: &str) -> Option<PathBuf> {
        let component = self.component(component_id).ok()?;
        self.is_ready(component_id)
            .then(|| self.component_path(component))
    }

    pub fn managed_executable(&self, component_id: &str, file_name: &str) -> Option<PathBuf> {
        let root = self.managed_component_path(component_id)?;
        find_named_file(&root, file_name, 4)
    }

    pub fn managed_named_file(&self, component_id: &str, file_name: &str) -> Option<PathBuf> {
        let root = self.managed_component_path(component_id)?;
        find_named_file(&root, file_name, 6)
    }

    pub fn managed_identity(&self, component_id: &str) -> Option<ManagedComponentIdentity> {
        let component = self.component(component_id).ok()?;
        let marker = read_marker(&self.component_path(component)).ok()?;
        Some(ManagedComponentIdentity {
            version: marker.version,
            artifact_sha256: marker.artifact_sha256,
        })
    }

    fn run_job(&self, job_id: &str) {
        let component_ids = match self.status(job_id) {
            Ok(job) => job.component_ids,
            Err(_) => return,
        };
        let total = component_ids.len().max(1);
        self.update_job(job_id, |job| {
            job.status = "downloading".to_string();
            job.detail = "Starting verified dependency download.".to_string();
        });

        for (index, id) in component_ids.iter().enumerate() {
            if self.is_cancelled(job_id) {
                self.finish_cancelled(job_id);
                return;
            }
            self.update_job(job_id, |job| {
                job.current_component_id = id.clone();
                job.progress = ((index * 100) / total) as u8;
                job.status = "downloading".to_string();
                job.detail = format!("Downloading {id} from its audited catalog source.");
            });
            let component = match self.component(id) {
                Ok(value) => value.clone(),
                Err(error) => {
                    self.finish_failed(job_id, error);
                    return;
                }
            };
            if let Err(error) = self.install_one(job_id, &component) {
                if self.is_cancelled(job_id) {
                    self.finish_cancelled(job_id);
                } else {
                    self.finish_failed(job_id, error);
                }
                return;
            }
        }
        self.update_job(job_id, |job| {
            job.status = "ready".to_string();
            job.progress = 100;
            job.current_component_id.clear();
            job.detail =
                "All selected managed dependencies are installed and verified.".to_string();
        });
    }

    fn install_one(&self, job_id: &str, component: &DependencyComponent) -> Result<(), String> {
        let artifact = component
            .artifact
            .as_ref()
            .ok_or_else(|| "catalog artifact is unavailable".to_string())?;
        fs::create_dir_all(&self.root).map_err(|error| error.to_string())?;
        let staging = self.root.join(format!(".staging-{}", Uuid::new_v4()));
        let payload = staging.join("payload");
        fs::create_dir_all(&payload).map_err(|error| error.to_string())?;
        let download = staging.join("artifact.download");
        let result = (|| {
            download_verified(
                &artifact.url,
                &download,
                artifact.max_bytes,
                &artifact.sha256,
                || self.is_cancelled(job_id),
            )?;
            self.update_job(job_id, |job| {
                job.status = "installing".to_string();
                job.detail = format!("Installing and validating {}.", component.label);
            });
            match artifact.archive.as_str() {
                "file" => {
                    let name = artifact.file_name.as_deref().unwrap_or("artifact.bin");
                    validate_relative_path(Path::new(name))?;
                    fs::copy(&download, payload.join(name)).map_err(|error| error.to_string())?;
                }
                "zip" => extract_zip(&download, &payload, || self.is_cancelled(job_id))?,
                other => return Err(format!("unsupported dependency archive type {other}")),
            }
            if self.is_cancelled(job_id) {
                return Err("dependency installation cancelled".to_string());
            }
            let marker = ManagedComponentMarker {
                id: component.id.clone(),
                version: component.version.clone(),
                artifact_sha256: artifact.sha256.clone(),
                source_url: artifact.url.clone(),
                installed_at_unix: now_unix(),
            };
            let marker_bytes =
                serde_json::to_vec_pretty(&marker).map_err(|error| error.to_string())?;
            let mut marker_file =
                File::create(payload.join(MANAGED_MARKER)).map_err(|error| error.to_string())?;
            marker_file
                .write_all(&marker_bytes)
                .map_err(|error| error.to_string())?;
            marker_file.sync_all().map_err(|error| error.to_string())?;
            promote_owned_directory(&payload, &self.component_path(component), &self.root)?;
            Ok(())
        })();
        let _ = fs::remove_dir_all(&staging);
        result
    }

    fn component(&self, id: &str) -> Result<&DependencyComponent, String> {
        self.catalog
            .components
            .iter()
            .find(|item| item.id == id)
            .ok_or_else(|| format!("Unknown dependency catalog component {id}."))
    }

    fn component_path(&self, component: &DependencyComponent) -> PathBuf {
        self.root.join(&component.id).join(&component.version)
    }

    fn is_ready(&self, id: &str) -> bool {
        let Ok(component) = self.component(id) else {
            return false;
        };
        matches!(read_marker(&self.component_path(component)), Ok(marker) if marker.id == component.id && marker.version == component.version && component.artifact.as_ref().is_some_and(|artifact| marker.artifact_sha256 == artifact.sha256))
    }

    fn component_status(&self, component: &DependencyComponent) -> DependencyComponentStatus {
        let path = self.component_path(component);
        let update_available =
            !self.is_ready(&component.id) && self.has_owned_previous_version(component);
        let (state, detail) = if self.is_ready(&component.id) {
            (
                "ready",
                "Managed component identity and integrity marker are valid.".to_string(),
            )
        } else if path.exists() {
            (
                "invalid",
                "Managed directory exists without the exact current catalog identity.".to_string(),
            )
        } else if update_available {
            (
                "notInstalled",
                "A different owned version is installed; the audited catalog update is available."
                    .to_string(),
            )
        } else if component.availability == "available" {
            (
                "notInstalled",
                "Available for explicit managed installation.".to_string(),
            )
        } else {
            (
                "notInstalled",
                format!(
                    "Artifact is {} and cannot be installed yet.",
                    component.availability
                ),
            )
        };
        DependencyComponentStatus {
            component: component.clone(),
            state: state.to_string(),
            install_path: path.display().to_string(),
            update_available,
            detail,
        }
    }

    fn update_job(&self, job_id: &str, update: impl FnOnce(&mut DependencyInstallJob)) {
        let updated = if let Ok(mut jobs) = self.jobs.lock() {
            jobs.get_mut(job_id).map(|job| {
                update(job);
                job.clone()
            })
        } else {
            None
        };
        if let Some(job) = updated {
            let _ = self.persist_job(&job);
        }
    }

    fn has_owned_previous_version(&self, component: &DependencyComponent) -> bool {
        let root = self.root.join(&component.id);
        let Ok(entries) = fs::read_dir(root) else {
            return false;
        };
        entries.flatten().any(|entry| {
            let path = entry.path();
            path.is_dir()
                && path != self.component_path(component)
                && matches!(read_marker(&path), Ok(marker) if marker.id == component.id)
        })
    }

    fn persist_job(&self, job: &DependencyInstallJob) -> Result<(), String> {
        let directory = self.root.join(".jobs");
        fs::create_dir_all(&directory).map_err(|error| error.to_string())?;
        let final_path = directory.join(format!("{}.json", job.job_id));
        let temporary = directory.join(format!(".{}.tmp", job.job_id));
        let previous = directory.join(format!(".{}.previous", job.job_id));
        let bytes = serde_json::to_vec_pretty(job).map_err(|error| error.to_string())?;
        let mut file = File::create(&temporary).map_err(|error| error.to_string())?;
        file.write_all(&bytes).map_err(|error| error.to_string())?;
        file.sync_all().map_err(|error| error.to_string())?;
        if final_path.exists() {
            let _ = fs::remove_file(&previous);
            fs::rename(&final_path, &previous).map_err(|error| error.to_string())?;
        }
        if let Err(error) = fs::rename(&temporary, &final_path) {
            if previous.exists() {
                let _ = fs::rename(&previous, &final_path);
            }
            return Err(error.to_string());
        }
        let _ = fs::remove_file(previous);
        Ok(())
    }

    fn recover_interrupted_state(&self) -> Result<(), String> {
        fs::create_dir_all(&self.root).map_err(|error| error.to_string())?;
        for entry in fs::read_dir(&self.root)
            .map_err(|error| error.to_string())?
            .flatten()
        {
            let name = entry.file_name().to_string_lossy().to_string();
            if entry.path().is_dir() && name.starts_with(".staging-") {
                fs::remove_dir_all(entry.path()).map_err(|error| {
                    format!("could not clean interrupted dependency staging: {error}")
                })?;
            }
        }
        let jobs_directory = self.root.join(".jobs");
        if !jobs_directory.is_dir() {
            return Ok(());
        }
        let mut recovered = 0_usize;
        for entry in fs::read_dir(&jobs_directory)
            .map_err(|error| error.to_string())?
            .flatten()
        {
            if recovered >= 1_000
                || entry.path().extension().and_then(|value| value.to_str()) != Some("json")
            {
                continue;
            }
            let Ok(text) = fs::read_to_string(entry.path()) else {
                continue;
            };
            let Ok(mut job) = serde_json::from_str::<DependencyInstallJob>(&text) else {
                continue;
            };
            if matches!(job.status.as_str(), "queued" | "downloading" | "installing") {
                job.status = "failed".to_string();
                job.detail = "RoadWatcher restarted before this managed installation completed; unpublished staging was removed and retry is safe.".to_string();
                self.persist_job(&job)?;
            }
            self.jobs
                .lock()
                .map_err(lock_error)?
                .insert(job.job_id.clone(), job);
            recovered += 1;
        }
        Ok(())
    }

    fn is_cancelled(&self, job_id: &str) -> bool {
        self.cancellations
            .lock()
            .map(|items| items.contains(job_id))
            .unwrap_or(true)
    }

    fn finish_cancelled(&self, job_id: &str) {
        self.update_job(job_id, |job| {
            job.status = "cancelled".to_string();
            job.detail = "Dependency installation cancelled; unpublished staging data was removed."
                .to_string();
        });
    }

    fn finish_failed(&self, job_id: &str, error: String) {
        self.update_job(job_id, |job| {
            job.status = "failed".to_string();
            job.detail = error;
        });
    }
}

pub fn validate_catalog(catalog: &DependencyCatalog) -> Result<(), String> {
    if catalog.schema_version != 1 {
        return Err("unsupported dependency catalog schema".to_string());
    }
    if catalog.platform != "windows-x86_64" {
        return Err("dependency catalog must target windows-x86_64".to_string());
    }
    let mut ids = HashSet::new();
    for component in &catalog.components {
        if component.id.is_empty()
            || !component
                .id
                .chars()
                .all(|value| value.is_ascii_lowercase() || value.is_ascii_digit() || value == '-')
        {
            return Err(format!(
                "dependency component id {} is invalid",
                component.id
            ));
        }
        if !ids.insert(component.id.clone()) {
            return Err(format!("duplicate dependency component {}", component.id));
        }
        validate_https_url(&component.source_url)?;
        validate_https_url(&component.license.url)?;
        if component.license.digest.trim().is_empty() {
            return Err(format!("{} license digest is blank", component.id));
        }
        if component.availability == "available" && component.artifact.is_none() {
            return Err(format!(
                "{} claims availability without an artifact",
                component.id
            ));
        }
        if let Some(artifact) = &component.artifact {
            validate_download_url(&artifact.url)?;
            if artifact.max_bytes == 0 {
                return Err(format!("{} has no download size limit", component.id));
            }
            if artifact.sha256.len() != 64
                || !artifact
                    .sha256
                    .chars()
                    .all(|value| value.is_ascii_hexdigit())
            {
                return Err(format!("{} artifact SHA-256 is invalid", component.id));
            }
            if !matches!(artifact.archive.as_str(), "file" | "zip") {
                return Err(format!("{} archive type is unsupported", component.id));
            }
        }
    }
    for component in &catalog.components {
        for dependency in &component.dependencies {
            if !ids.contains(dependency) {
                return Err(format!(
                    "{} depends on unknown component {}",
                    component.id, dependency
                ));
            }
        }
    }
    for component in &catalog.components {
        visit_dependencies(
            &component.id,
            catalog,
            &mut HashSet::new(),
            &mut HashSet::new(),
        )?;
    }
    Ok(())
}

fn visit_dependencies(
    id: &str,
    catalog: &DependencyCatalog,
    visiting: &mut HashSet<String>,
    visited: &mut HashSet<String>,
) -> Result<(), String> {
    if visited.contains(id) {
        return Ok(());
    }
    if !visiting.insert(id.to_string()) {
        return Err(format!("dependency cycle includes {id}"));
    }
    let component = catalog
        .components
        .iter()
        .find(|item| item.id == id)
        .ok_or_else(|| format!("unknown dependency {id}"))?;
    for dependency in &component.dependencies {
        visit_dependencies(dependency, catalog, visiting, visited)?;
    }
    visiting.remove(id);
    visited.insert(id.to_string());
    Ok(())
}

fn validate_https_url(url: &str) -> Result<(), String> {
    if !url.starts_with("https://") || url.contains(['\r', '\n']) {
        return Err(format!("unsafe dependency URL {url}"));
    }
    Ok(())
}

fn validate_download_url(url: &str) -> Result<(), String> {
    validate_https_url(url)?;
    let host = url
        .trim_start_matches("https://")
        .split('/')
        .next()
        .unwrap_or_default()
        .split(':')
        .next()
        .unwrap_or_default();
    if !ALLOWED_DOWNLOAD_HOSTS.contains(&host) {
        return Err(format!(
            "dependency download host {host} is not allowlisted"
        ));
    }
    Ok(())
}

struct DownloadResponse {
    status: u16,
    etag: Option<String>,
    accepts_ranges: bool,
    content_range: Option<String>,
    reader: Box<dyn Read>,
}

#[derive(Clone, Debug, Eq, PartialEq)]
struct ResumeRequest {
    offset: u64,
    etag: String,
}

trait DownloadClient {
    fn get(&self, url: &str, resume: Option<&ResumeRequest>) -> Result<DownloadResponse, String>;
}

struct UreqDownloadClient;

impl DownloadClient for UreqDownloadClient {
    fn get(&self, url: &str, resume: Option<&ResumeRequest>) -> Result<DownloadResponse, String> {
        let mut request = ureq::get(url);
        if let Some(resume) = resume {
            request = request
                .header("Range", &format!("bytes={}-", resume.offset))
                .header("If-Range", &resume.etag);
        }
        let response = request
            .call()
            .map_err(|error| format!("dependency download failed: {error}"))?;
        let status = response.status().as_u16();
        let etag = response
            .headers()
            .get("etag")
            .and_then(|value| value.to_str().ok())
            .map(str::to_string);
        let accepts_ranges = response
            .headers()
            .get("accept-ranges")
            .and_then(|value| value.to_str().ok())
            .is_some_and(|value| value.eq_ignore_ascii_case("bytes"));
        let content_range = response
            .headers()
            .get("content-range")
            .and_then(|value| value.to_str().ok())
            .map(str::to_string);
        Ok(DownloadResponse {
            status,
            etag,
            accepts_ranges,
            content_range,
            reader: Box::new(response.into_body().into_reader()),
        })
    }
}

fn download_verified(
    url: &str,
    destination: &Path,
    max_bytes: u64,
    expected_sha256: &str,
    cancelled: impl Fn() -> bool,
) -> Result<(), String> {
    validate_download_url(url)?;
    download_verified_with_client(
        &UreqDownloadClient,
        url,
        destination,
        max_bytes,
        expected_sha256,
        cancelled,
    )
}

fn download_verified_with_client(
    client: &impl DownloadClient,
    url: &str,
    destination: &Path,
    max_bytes: u64,
    expected_sha256: &str,
    cancelled: impl Fn() -> bool,
) -> Result<(), String> {
    let mut response = client.get(url, None)?;
    validate_initial_response(&response)?;
    let mut stable_etag = strong_etag(response.etag.as_deref())
        .filter(|_| response.accepts_ranges)
        .map(str::to_string);
    let mut output = File::create(destination).map_err(|error| error.to_string())?;
    let mut hash = Sha256::new();
    let mut total = 0_u64;
    let mut buffer = [0_u8; 64 * 1024];
    let mut resume_attempts = 0_u8;
    let mut restart_attempts = 0_u8;
    loop {
        if cancelled() {
            return Err("dependency download cancelled".to_string());
        }
        let read = match response.reader.read(&mut buffer) {
            Ok(value) => value,
            Err(error) => {
                if let Some(etag) = stable_etag
                    .clone()
                    .filter(|_| total > 0 && resume_attempts < 2)
                {
                    resume_attempts += 1;
                    let request = ResumeRequest {
                        offset: total,
                        etag,
                    };
                    if let Ok(candidate) = client.get(url, Some(&request)) {
                        if validate_resume_response(&candidate, &request).is_ok() {
                            response = candidate;
                            continue;
                        }
                    }
                }
                if restart_attempts == 0 {
                    restart_attempts += 1;
                    response = client.get(url, None)?;
                    validate_initial_response(&response)?;
                    stable_etag = strong_etag(response.etag.as_deref())
                        .filter(|_| response.accepts_ranges)
                        .map(str::to_string);
                    output =
                        File::create(destination).map_err(|write_error| write_error.to_string())?;
                    hash = Sha256::new();
                    total = 0;
                    resume_attempts = 0;
                    continue;
                }
                return Err(format!(
                    "dependency download stream failed after safe retry: {error}"
                ));
            }
        };
        if read == 0 {
            break;
        }
        total = total
            .checked_add(read as u64)
            .ok_or_else(|| "dependency size overflow".to_string())?;
        if total > max_bytes {
            return Err("dependency download exceeded its catalog size limit".to_string());
        }
        hash.update(&buffer[..read]);
        output
            .write_all(&buffer[..read])
            .map_err(|error| error.to_string())?;
    }
    output.sync_all().map_err(|error| error.to_string())?;
    let actual = hex::encode(hash.finalize());
    if !actual.eq_ignore_ascii_case(expected_sha256) {
        return Err(format!(
            "dependency SHA-256 mismatch: expected {expected_sha256}, got {actual}"
        ));
    }
    Ok(())
}

fn validate_initial_response(response: &DownloadResponse) -> Result<(), String> {
    if response.status != 200 {
        return Err(format!(
            "dependency download returned HTTP {} instead of 200",
            response.status
        ));
    }
    Ok(())
}

fn validate_resume_response(
    response: &DownloadResponse,
    request: &ResumeRequest,
) -> Result<(), String> {
    if response.status != 206 {
        return Err(format!(
            "dependency resume returned HTTP {} instead of 206",
            response.status
        ));
    }
    if strong_etag(response.etag.as_deref()) != Some(request.etag.as_str()) {
        return Err("dependency resume ETag changed".to_string());
    }
    let content_range = response
        .content_range
        .as_deref()
        .ok_or_else(|| "dependency resume omitted Content-Range".to_string())?;
    let (range, total_length) = content_range
        .strip_prefix("bytes ")
        .and_then(|value| value.split_once('/'))
        .ok_or_else(|| "dependency resume Content-Range is invalid".to_string())?;
    let (start, end) = range
        .split_once('-')
        .and_then(|(start, end)| Some((start.parse::<u64>().ok()?, end.parse::<u64>().ok()?)))
        .ok_or_else(|| "dependency resume Content-Range is invalid".to_string())?;
    let total_length = total_length
        .parse::<u64>()
        .map_err(|_| "dependency resume Content-Range total is invalid".to_string())?;
    if start != request.offset {
        return Err("dependency resume Content-Range starts at the wrong offset".to_string());
    }
    if end < start || total_length <= end {
        return Err("dependency resume Content-Range bounds are invalid".to_string());
    }
    Ok(())
}

fn strong_etag(value: Option<&str>) -> Option<&str> {
    value.filter(|etag| {
        etag.len() >= 2
            && etag.starts_with('"')
            && etag.ends_with('"')
            && !etag.starts_with("W/")
            && !etag.contains(['\r', '\n'])
    })
}

fn extract_zip(
    archive_path: &Path,
    destination: &Path,
    cancelled: impl Fn() -> bool,
) -> Result<(), String> {
    let file = File::open(archive_path).map_err(|error| error.to_string())?;
    let mut archive =
        ZipArchive::new(file).map_err(|error| format!("dependency ZIP is invalid: {error}"))?;
    for index in 0..archive.len() {
        if cancelled() {
            return Err("dependency extraction cancelled".to_string());
        }
        let mut entry = archive.by_index(index).map_err(|error| error.to_string())?;
        let relative = entry
            .enclosed_name()
            .ok_or_else(|| "dependency ZIP contains an unsafe path".to_string())?;
        validate_relative_path(&relative)?;
        let output_path = destination.join(relative);
        if entry.is_dir() {
            fs::create_dir_all(&output_path).map_err(|error| error.to_string())?;
        } else {
            if let Some(parent) = output_path.parent() {
                fs::create_dir_all(parent).map_err(|error| error.to_string())?;
            }
            let mut output = File::create(&output_path).map_err(|error| error.to_string())?;
            io::copy(&mut entry, &mut output).map_err(|error| error.to_string())?;
        }
    }
    Ok(())
}

fn validate_relative_path(path: &Path) -> Result<(), String> {
    if path.is_absolute()
        || path
            .components()
            .any(|part| !matches!(part, Component::Normal(_)))
    {
        return Err("dependency artifact contains an unsafe path".to_string());
    }
    Ok(())
}

fn promote_owned_directory(
    staging_payload: &Path,
    target: &Path,
    root: &Path,
) -> Result<(), String> {
    if !target.starts_with(root) {
        return Err("managed dependency target escaped its root".to_string());
    }
    if let Some(parent) = target.parent() {
        fs::create_dir_all(parent).map_err(|error| error.to_string())?;
    }
    let backup = target.with_extension(format!("backup-{}", Uuid::new_v4()));
    let had_target = target.exists();
    if had_target {
        read_marker(target)?;
        fs::rename(target, &backup)
            .map_err(|error| format!("could not preserve previous managed component: {error}"))?;
    }
    if let Err(error) = fs::rename(staging_payload, target) {
        if had_target {
            let _ = fs::rename(&backup, target);
        }
        return Err(format!(
            "could not publish managed component atomically: {error}"
        ));
    }
    if had_target {
        fs::remove_dir_all(&backup)
            .map_err(|error| format!("could not remove previous managed component: {error}"))?;
    }
    Ok(())
}

fn read_marker(target: &Path) -> Result<ManagedComponentMarker, String> {
    let text = fs::read_to_string(target.join(MANAGED_MARKER))
        .map_err(|_| "managed component ownership marker is missing".to_string())?;
    serde_json::from_str(&text)
        .map_err(|_| "managed component ownership marker is invalid".to_string())
}

fn find_named_file(root: &Path, file_name: &str, remaining_depth: usize) -> Option<PathBuf> {
    if remaining_depth == 0 {
        return None;
    }
    let entries = fs::read_dir(root).ok()?;
    for entry in entries.flatten() {
        let path = entry.path();
        if path.is_file()
            && path
                .file_name()
                .is_some_and(|name| name.to_string_lossy().eq_ignore_ascii_case(file_name))
        {
            return Some(path);
        }
        if path.is_dir() {
            if let Some(found) = find_named_file(&path, file_name, remaining_depth - 1) {
                return Some(found);
            }
        }
    }
    None
}

fn now_unix() -> u64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap_or_default()
        .as_secs()
}

fn lock_error<T>(_: std::sync::PoisonError<T>) -> String {
    "dependency manager state lock failed".to_string()
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::collections::VecDeque;
    use std::io::{Cursor, ErrorKind};

    fn catalog() -> DependencyCatalog {
        DependencyCatalog {
            schema_version: 1,
            platform: "windows-x86_64".to_string(),
            catalog_version: "test".to_string(),
            components: vec![DependencyComponent {
                id: "tool".to_string(),
                label: "Tool".to_string(),
                version: "1".to_string(),
                purpose: "test".to_string(),
                required: true,
                recommended: true,
                license: DependencyLicense {
                    id: "mit".to_string(),
                    label: "MIT".to_string(),
                    url: "https://example.com/license".to_string(),
                    digest: "digest".to_string(),
                    consent_required: true,
                },
                source_url: "https://example.com/tool".to_string(),
                availability: "available".to_string(),
                artifact: Some(DependencyArtifact {
                    url: "https://github.com/example/tool.zip".to_string(),
                    sha256: "a".repeat(64),
                    max_bytes: 100,
                    archive: "zip".to_string(),
                    file_name: None,
                }),
                dependencies: vec![],
            }],
        }
    }

    #[test]
    fn rejects_duplicate_cycles_and_unsafe_artifacts() {
        let mut duplicate = catalog();
        duplicate.components.push(duplicate.components[0].clone());
        assert!(validate_catalog(&duplicate)
            .unwrap_err()
            .contains("duplicate"));
        let mut unsafe_catalog = catalog();
        unsafe_catalog.components[0].artifact.as_mut().unwrap().url =
            "http://example.com/tool.zip".to_string();
        assert!(validate_catalog(&unsafe_catalog)
            .unwrap_err()
            .contains("unsafe"));
        let mut cycle = catalog();
        cycle.components[0].dependencies.push("tool".to_string());
        assert!(validate_catalog(&cycle).unwrap_err().contains("cycle"));
    }

    #[test]
    fn rejects_missing_license_consent_and_unknown_ids() {
        let root =
            std::env::temp_dir().join(format!("roadwatcher-dependency-test-{}", Uuid::new_v4()));
        let manager = DependencyManager::from_catalog(catalog(), root).unwrap();
        assert!(manager
            .start(vec!["missing".to_string()], vec![])
            .unwrap_err()
            .contains("Unknown"));
        assert!(manager
            .start(vec!["tool".to_string()], vec![])
            .unwrap_err()
            .contains("License consent"));
    }

    #[test]
    fn removal_refuses_unowned_directories() {
        let root =
            std::env::temp_dir().join(format!("roadwatcher-dependency-test-{}", Uuid::new_v4()));
        let manager = DependencyManager::from_catalog(catalog(), root.clone()).unwrap();
        let target = root.join("tool").join("1");
        fs::create_dir_all(&target).unwrap();
        assert!(manager
            .remove("tool")
            .unwrap_err()
            .contains("ownership marker"));
        fs::remove_dir_all(root).unwrap();
    }

    #[test]
    fn relative_paths_reject_traversal_and_roots() {
        assert!(validate_relative_path(Path::new("bin/tool.exe")).is_ok());
        assert!(validate_relative_path(Path::new("../tool.exe")).is_err());
        assert!(validate_relative_path(Path::new("C:/tool.exe")).is_err());
    }

    #[test]
    fn restart_recovers_jobs_and_removes_only_managed_staging() {
        let app_data = std::env::temp_dir().join(format!(
            "roadwatcher-dependency-recovery-{}",
            Uuid::new_v4()
        ));
        fs::create_dir_all(&app_data).unwrap();
        let catalog_path = app_data.join("catalog.json");
        fs::write(&catalog_path, serde_json::to_vec(&catalog()).unwrap()).unwrap();
        let manager = DependencyManager::load(&catalog_path, &app_data).unwrap();
        let job = DependencyInstallJob {
            job_id: Uuid::new_v4().to_string(),
            status: "installing".to_string(),
            progress: 50,
            detail: "interrupted".to_string(),
            component_ids: vec!["tool".to_string()],
            current_component_id: "tool".to_string(),
        };
        manager.persist_job(&job).unwrap();
        fs::create_dir_all(manager.root.join(".staging-interrupted")).unwrap();
        fs::create_dir_all(manager.root.join("unowned-user-directory")).unwrap();
        drop(manager);

        let recovered = DependencyManager::load(&catalog_path, &app_data).unwrap();
        let status = recovered.status(&job.job_id).unwrap();
        assert_eq!(status.status, "failed");
        assert!(status.detail.contains("retry is safe"));
        assert!(!recovered.root.join(".staging-interrupted").exists());
        assert!(recovered.root.join("unowned-user-directory").is_dir());
        fs::remove_dir_all(app_data).unwrap();
    }

    #[test]
    fn resumes_only_with_matching_strong_etag_and_content_range() {
        let payload = b"abcdefghij";
        let client = FakeDownloadClient::new(vec![
            Ok(download_response(
                200,
                Some("\"artifact-v1\""),
                true,
                None,
                Box::new(InterruptAfterData::new(&payload[..4])),
            )),
            Ok(download_response(
                206,
                Some("\"artifact-v1\""),
                true,
                Some("bytes 4-9/10"),
                Box::new(Cursor::new(payload[4..].to_vec())),
            )),
        ]);
        let destination = temporary_download_path("resume");
        download_verified_with_client(
            &client,
            "https://github.com/example/tool.zip",
            &destination,
            100,
            &hex::encode(Sha256::digest(payload)),
            || false,
        )
        .unwrap();
        assert_eq!(fs::read(&destination).unwrap(), payload);
        assert_eq!(
            client.requests(),
            vec![
                None,
                Some(ResumeRequest {
                    offset: 4,
                    etag: "\"artifact-v1\"".to_string(),
                })
            ]
        );
        fs::remove_file(destination).unwrap();
    }

    #[test]
    fn weak_or_changed_etag_restarts_the_staged_download_from_zero() {
        let payload = b"abcdefghij";
        let client = FakeDownloadClient::new(vec![
            Ok(download_response(
                200,
                Some("W/\"artifact-v1\""),
                true,
                None,
                Box::new(InterruptAfterData::new(&payload[..4])),
            )),
            Ok(download_response(
                200,
                Some("\"artifact-v2\""),
                true,
                None,
                Box::new(Cursor::new(payload.to_vec())),
            )),
        ]);
        let destination = temporary_download_path("restart");
        download_verified_with_client(
            &client,
            "https://github.com/example/tool.zip",
            &destination,
            100,
            &hex::encode(Sha256::digest(payload)),
            || false,
        )
        .unwrap();
        assert_eq!(fs::read(&destination).unwrap(), payload);
        assert_eq!(client.requests(), vec![None, None]);
        fs::remove_file(destination).unwrap();
    }

    #[test]
    fn resume_identity_drift_is_rejected_before_a_safe_restart() {
        let payload = b"abcdefghij";
        let client = FakeDownloadClient::new(vec![
            Ok(download_response(
                200,
                Some("\"artifact-v1\""),
                true,
                None,
                Box::new(InterruptAfterData::new(&payload[..4])),
            )),
            Ok(download_response(
                206,
                Some("\"artifact-v2\""),
                true,
                Some("bytes 4-9/10"),
                Box::new(Cursor::new(payload[4..].to_vec())),
            )),
            Ok(download_response(
                200,
                Some("\"artifact-v2\""),
                true,
                None,
                Box::new(Cursor::new(payload.to_vec())),
            )),
        ]);
        let destination = temporary_download_path("identity-drift");
        download_verified_with_client(
            &client,
            "https://github.com/example/tool.zip",
            &destination,
            100,
            &hex::encode(Sha256::digest(payload)),
            || false,
        )
        .unwrap();
        assert_eq!(fs::read(&destination).unwrap(), payload);
        assert_eq!(client.requests().len(), 3);
        assert!(client.requests()[1].is_some());
        assert!(client.requests()[2].is_none());
        fs::remove_file(destination).unwrap();
    }

    fn temporary_download_path(label: &str) -> PathBuf {
        std::env::temp_dir().join(format!("roadwatcher-download-{label}-{}", Uuid::new_v4()))
    }

    fn download_response(
        status: u16,
        etag: Option<&str>,
        accepts_ranges: bool,
        content_range: Option<&str>,
        reader: Box<dyn Read>,
    ) -> DownloadResponse {
        DownloadResponse {
            status,
            etag: etag.map(str::to_string),
            accepts_ranges,
            content_range: content_range.map(str::to_string),
            reader,
        }
    }

    struct InterruptAfterData {
        data: Cursor<Vec<u8>>,
        interrupted: bool,
    }

    impl InterruptAfterData {
        fn new(data: &[u8]) -> Self {
            Self {
                data: Cursor::new(data.to_vec()),
                interrupted: false,
            }
        }
    }

    impl Read for InterruptAfterData {
        fn read(&mut self, buffer: &mut [u8]) -> io::Result<usize> {
            let read = self.data.read(buffer)?;
            if read > 0 {
                return Ok(read);
            }
            if !self.interrupted {
                self.interrupted = true;
                return Err(io::Error::new(
                    ErrorKind::ConnectionReset,
                    "simulated interruption",
                ));
            }
            Ok(0)
        }
    }

    struct FakeDownloadClient {
        responses: Mutex<VecDeque<Result<DownloadResponse, String>>>,
        requests: Mutex<Vec<Option<ResumeRequest>>>,
    }

    impl FakeDownloadClient {
        fn new(responses: Vec<Result<DownloadResponse, String>>) -> Self {
            Self {
                responses: Mutex::new(responses.into()),
                requests: Mutex::new(Vec::new()),
            }
        }

        fn requests(&self) -> Vec<Option<ResumeRequest>> {
            self.requests.lock().unwrap().clone()
        }
    }

    impl DownloadClient for FakeDownloadClient {
        fn get(
            &self,
            _url: &str,
            resume: Option<&ResumeRequest>,
        ) -> Result<DownloadResponse, String> {
            self.requests.lock().unwrap().push(resume.cloned());
            self.responses
                .lock()
                .unwrap()
                .pop_front()
                .expect("fake download response")
        }
    }
}
