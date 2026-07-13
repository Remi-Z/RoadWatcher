use crate::bounded_process::run_bounded_process_cancellable;
use crate::managed_runtime;
use crate::managed_valhalla_config::validate_portable_config;
use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256};
use std::collections::{BTreeMap, HashMap, HashSet};
use std::fs::{self, File};
use std::io::{self, Read, Write};
use std::path::{Component, Path, PathBuf};
use std::process::Command;
use std::sync::{Arc, Mutex};
use std::thread;
use std::time::{Duration, SystemTime, UNIX_EPOCH};
use url::Url;
use uuid::Uuid;
use zip::ZipArchive;

const MANAGED_MARKER: &str = ".roadwatcher-managed-component.json";
const UV_PYTHON_BOOTSTRAP_TIMEOUT: Duration = Duration::from_secs(20 * 60);
const UV_PYTHON_BOOTSTRAP_OUTPUT_LIMIT: u64 = 16 * 1024 * 1024;
const UV_WHEEL_ENVIRONMENT_TIMEOUT: Duration = Duration::from_secs(10 * 60);
const UV_WHEEL_ENVIRONMENT_OUTPUT_LIMIT: u64 = 4 * 1024 * 1024;
const MANAGED_ENVIRONMENT_MARKER: &str = ".roadwatcher-managed-environment";
const FFMPEG_ARCHIVE_ROOT: &str = "ffmpeg-8.1.1-full_build";
const FFMPEG_VERSION_PREFIX: &str = "ffmpeg version 8.1.1-full_build-www.gyan.dev";
const FFPROBE_VERSION_PREFIX: &str = "ffprobe version 8.1.1-full_build-www.gyan.dev";
const ROADWATCHER_RELEASE_OWNER: &str = "Remi-Z";
const ROADWATCHER_RELEASE_REPOSITORY: &str = "RoadWatcher";
const ROADWATCHER_RELEASE_MANIFEST_MAX_BYTES: u64 = 4 * 1024 * 1024;
const ROADWATCHER_RELEASE_ARCHIVE_MAX_BYTES: u64 = 16 * 1024 * 1024 * 1024;
const ROADWATCHER_RELEASE_MAX_ZIP_ENTRIES: usize = 250_000;
const ROADWATCHER_RELEASE_MAX_UNPACKED_BYTES: u64 = 32 * 1024 * 1024 * 1024;
const ROADWATCHER_VALHALLA_CONFIG_MAX_BYTES: u64 = 4 * 1024 * 1024;
const ROADWATCHER_VALHALLA_TILE_MAX_FILES: usize = 200_000;
const ROADWATCHER_VALHALLA_TILE_MAX_BYTES: u64 = 24 * 1024 * 1024 * 1024;
const ROADWATCHER_CV_LABELS_MAX_BYTES: u64 = 2 * 1024 * 1024;
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
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub artifact_manifest: Option<DependencyArtifactManifestDescriptor>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub bootstrap: Option<DependencyBootstrap>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub install_strategy: Option<DependencyInstallStrategy>,
    pub dependencies: Vec<String>,
    pub references: Vec<DependencyReference>,
    pub project_imports: Vec<DependencyProjectImport>,
}

#[derive(Clone, Debug, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct DependencyInstallStrategy {
    pub kind: String,
    #[serde(default)]
    pub python_version: String,
    #[serde(default)]
    pub package: String,
    #[serde(default)]
    pub package_version: String,
    #[serde(default)]
    pub wheel_file_name: String,
}

#[derive(Clone, Debug, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct DependencyReference {
    pub id: String,
    pub path: String,
    pub kind: String,
}

#[derive(Clone, Debug, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct DependencyProjectImport {
    pub id: String,
    pub label: String,
    pub path: String,
    pub source_crs: String,
    pub layer_name: String,
    pub layer_kind: String,
}

#[derive(Clone, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ManagedProjectImport {
    pub id: String,
    pub label: String,
    pub source_path: String,
    pub source_crs: String,
    pub layer_name: String,
    pub layer_kind: String,
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
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub size_bytes: Option<u64>,
    pub archive: String,
    pub file_name: Option<String>,
}

#[derive(Clone, Debug, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct DependencyArtifactManifestDescriptor {
    pub url: String,
    pub sha256: String,
    pub max_bytes: u64,
    pub kind: String,
}

#[derive(Clone, Debug, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct DependencyBootstrap {
    pub kind: String,
    pub version: String,
    pub source_url: String,
    pub license: DependencyLicense,
    pub artifact: Option<DependencyArtifact>,
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
    pub managed_references: HashMap<String, String>,
    pub managed_project_imports: Vec<ManagedProjectImport>,
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
    #[serde(default, skip_serializing_if = "Option::is_none")]
    bootstrap_artifact_sha256: Option<String>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    artifact_manifest_sha256: Option<String>,
    source_url: String,
    installed_at_unix: u64,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
struct RoadWatcherReleaseManifest {
    schema_version: u32,
    id: String,
    kind: String,
    version: String,
    platform: String,
    generated_at: String,
    artifact: RoadWatcherReleaseArtifactIdentity,
    sources: Vec<RoadWatcherReleaseSource>,
    tools: Vec<RoadWatcherReleaseTool>,
    build: RoadWatcherReleaseBuild,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
struct RoadWatcherReleaseArtifactIdentity {
    file_name: String,
    size_bytes: u64,
    sha256: String,
    license: RoadWatcherReleaseLicense,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
struct RoadWatcherReleaseLicense {
    id: String,
    name: String,
    url: String,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
struct RoadWatcherReleaseSource {
    id: String,
    url: String,
    version: String,
    license: RoadWatcherReleaseLicense,
    downloaded_sha256: String,
    #[serde(default)]
    publisher_sha256: Option<String>,
    retrieved_at: String,
    size_bytes: u64,
    #[serde(default)]
    etag: Option<String>,
    #[serde(default)]
    last_modified: Option<String>,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
struct RoadWatcherReleaseTool {
    name: String,
    version: String,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
struct RoadWatcherReleaseBuild {
    recipe: String,
    recipe_version: String,
    parameters: BTreeMap<String, serde_json::Value>,
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
            if let Some(bootstrap) = &component.bootstrap {
                if bootstrap.license.consent_required
                    && !accepted.contains(&bootstrap.license.digest)
                {
                    return Err(format!(
                        "License consent for {} {} does not match the catalog digest.",
                        component.label, bootstrap.version
                    ));
                }
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

    pub fn managed_reference(&self, component_id: &str, reference_id: &str) -> Option<PathBuf> {
        let component = self.component(component_id).ok()?;
        if !self.is_ready(component_id) {
            return None;
        }
        self.resolve_references(component)
            .ok()?
            .remove(reference_id)
            .map(PathBuf::from)
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
            let artifact_manifest_sha256 = if let Some(strategy) = &component.install_strategy {
                self.install_strategy(
                    job_id, component, artifact, strategy, &download, &payload, &staging,
                )?
            } else {
                match artifact.archive.as_str() {
                    "file" => {
                        let name = artifact.file_name.as_deref().unwrap_or("artifact.bin");
                        validate_relative_path(Path::new(name))?;
                        fs::copy(&download, payload.join(name))
                            .map_err(|error| error.to_string())?;
                    }
                    "zip" => extract_zip(&download, &payload, || self.is_cancelled(job_id))?,
                    other => return Err(format!("unsupported dependency archive type {other}")),
                }
                None
            };
            if let Some(bootstrap) = &component.bootstrap {
                self.install_bootstrap(job_id, bootstrap, &payload, &staging)?;
            }
            if self.is_cancelled(job_id) {
                return Err("dependency installation cancelled".to_string());
            }
            let marker = ManagedComponentMarker {
                id: component.id.clone(),
                version: component.version.clone(),
                artifact_sha256: artifact.sha256.clone(),
                bootstrap_artifact_sha256: component
                    .bootstrap
                    .as_ref()
                    .and_then(|bootstrap| bootstrap.artifact.as_ref())
                    .map(|artifact| artifact.sha256.clone()),
                artifact_manifest_sha256,
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
            drop(marker_file);
            promote_owned_directory(&payload, &self.component_path(component), &self.root)?;
            Ok(())
        })();
        let _ = fs::remove_dir_all(&staging);
        result
    }

    fn install_strategy(
        &self,
        job_id: &str,
        component: &DependencyComponent,
        artifact: &DependencyArtifact,
        strategy: &DependencyInstallStrategy,
        download: &Path,
        payload: &Path,
        staging: &Path,
    ) -> Result<Option<String>, String> {
        match strategy.kind.as_str() {
            "uv-wheel-environment" => {
                let uv = self
                    .managed_reference("uv-python", "executable")
                    .ok_or_else(|| "managed uv is not installed and ready".to_string())?;
                let python_installations = self
                    .managed_reference("uv-python", "python-installations")
                    .ok_or_else(|| "managed Python installations are not ready".to_string())?;
                let wheel = staging.join(&strategy.wheel_file_name);
                fs::copy(download, &wheel).map_err(|error| error.to_string())?;
                install_uv_wheel_environment(
                    &uv,
                    &python_installations,
                    &wheel,
                    payload,
                    strategy,
                    || self.is_cancelled(job_id),
                )
                .map(|_| None)
            }
            "verified-ffmpeg-archive" => {
                extract_zip(download, payload, || self.is_cancelled(job_id))?;
                validate_ffmpeg_payload(payload, || self.is_cancelled(job_id)).map(|_| None)
            }
            "roadwatcher-release-archive" => {
                let descriptor = component.artifact_manifest.as_ref().ok_or_else(|| {
                    "RoadWatcher release archive is missing its verified manifest descriptor"
                        .to_string()
                })?;
                let manifest_path = staging.join("artifact.manifest.json");
                self.update_job(job_id, |job| {
                    job.status = "downloading".to_string();
                    job.detail = format!(
                        "Downloading the verified build manifest for {}.",
                        component.label
                    );
                });
                download_verified(
                    &descriptor.url,
                    &manifest_path,
                    descriptor.max_bytes,
                    &descriptor.sha256,
                    || self.is_cancelled(job_id),
                )?;
                self.update_job(job_id, |job| {
                    job.status = "installing".to_string();
                    job.detail = format!(
                        "Verifying the release manifest and installing {}.",
                        component.label
                    );
                });
                verify_roadwatcher_release_manifest(
                    component,
                    artifact,
                    descriptor,
                    download,
                    &manifest_path,
                )?;
                extract_zip_bounded(
                    download,
                    payload,
                    ROADWATCHER_RELEASE_MAX_ZIP_ENTRIES,
                    ROADWATCHER_RELEASE_MAX_UNPACKED_BYTES,
                    || self.is_cancelled(job_id),
                )?;
                validate_roadwatcher_release_payload(component, payload, || {
                    self.is_cancelled(job_id)
                })?;
                Ok(Some(descriptor.sha256.clone()))
            }
            other => Err(format!("unsupported dependency install strategy {other}")),
        }
    }

    fn install_bootstrap(
        &self,
        job_id: &str,
        bootstrap: &DependencyBootstrap,
        payload: &Path,
        staging: &Path,
    ) -> Result<(), String> {
        match bootstrap.kind.as_str() {
            "uv-managed-python" => {
                let artifact = bootstrap.artifact.as_ref().ok_or_else(|| {
                    "catalog uv-managed Python bootstrap artifact is unavailable".to_string()
                })?;
                let mirror_relative = artifact.file_name.as_deref().ok_or_else(|| {
                    "catalog uv-managed Python bootstrap mirror path is unavailable".to_string()
                })?;
                let mirror_relative = Path::new(mirror_relative);
                validate_relative_path(mirror_relative).map_err(|_| {
                    "catalog uv-managed Python bootstrap mirror path is unsafe".to_string()
                })?;
                let mirror = staging.join("python-mirror");
                let download = mirror.join(mirror_relative);
                let parent = download.parent().ok_or_else(|| {
                    "catalog uv-managed Python bootstrap mirror path is invalid".to_string()
                })?;
                fs::create_dir_all(parent).map_err(|error| error.to_string())?;
                self.update_job(job_id, |job| {
                    job.detail = format!(
                        "Downloading the catalog-pinned Python {} runtime.",
                        bootstrap.version
                    );
                });
                download_verified(
                    &artifact.url,
                    &download,
                    artifact.max_bytes,
                    &artifact.sha256,
                    || self.is_cancelled(job_id),
                )?;
                self.update_job(job_id, |job| {
                    job.status = "installing".to_string();
                    job.detail = format!(
                        "Installing and validating app-local Python {} with uv.",
                        bootstrap.version
                    );
                });
                install_uv_managed_python(payload, &mirror, &bootstrap.version, || {
                    self.is_cancelled(job_id)
                })
            }
            other => Err(format!("unsupported dependency bootstrap kind {other}")),
        }
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
        self.has_valid_marker(component)
            && self.resolve_references(component).is_ok()
            && self.resolve_project_imports(component).is_ok()
    }

    fn has_valid_marker(&self, component: &DependencyComponent) -> bool {
        matches!(read_marker(&self.component_path(component)), Ok(marker) if marker.id == component.id
            && marker.version == component.version
            && component.artifact.as_ref().is_some_and(|artifact| marker.artifact_sha256 == artifact.sha256)
            && marker.bootstrap_artifact_sha256 == component.bootstrap.as_ref().and_then(|bootstrap| bootstrap.artifact.as_ref()).map(|artifact| artifact.sha256.clone())
            && marker.artifact_manifest_sha256 == component.artifact_manifest.as_ref().map(|manifest| manifest.sha256.clone()))
    }

    fn resolve_references(
        &self,
        component: &DependencyComponent,
    ) -> Result<HashMap<String, String>, String> {
        let root = self.component_path(component);
        let mut result = HashMap::new();
        for reference in &component.references {
            let candidate = root.join(&reference.path);
            let valid_kind = match reference.kind.as_str() {
                "file" => candidate.is_file(),
                "directory" => candidate.is_dir(),
                _ => false,
            };
            if !valid_kind {
                return Err(format!(
                    "Managed reference {} is missing or has the wrong type.",
                    reference.id
                ));
            }
            let canonical = candidate.canonicalize().map_err(|error| {
                format!(
                    "Could not validate managed reference {}: {error}",
                    reference.id
                )
            })?;
            let canonical_root = root
                .canonicalize()
                .map_err(|error| format!("Could not validate managed component root: {error}"))?;
            if !canonical.starts_with(&canonical_root) {
                return Err(format!(
                    "Managed reference {} escaped its component root.",
                    reference.id
                ));
            }
            result.insert(reference.id.clone(), canonical.display().to_string());
        }
        Ok(result)
    }

    fn resolve_project_imports(
        &self,
        component: &DependencyComponent,
    ) -> Result<Vec<ManagedProjectImport>, String> {
        let root = self.component_path(component);
        let canonical_root = root
            .canonicalize()
            .map_err(|error| format!("Could not validate managed component root: {error}"))?;
        component
            .project_imports
            .iter()
            .map(|project_import| {
                let source = root.join(&project_import.path);
                if !source.exists() {
                    return Err(format!(
                        "Managed project import {} is missing.",
                        project_import.id
                    ));
                }
                let canonical = source.canonicalize().map_err(|error| {
                    format!(
                        "Could not validate managed project import {}: {error}",
                        project_import.id
                    )
                })?;
                if !canonical.starts_with(&canonical_root) {
                    return Err(format!(
                        "Managed project import {} escaped its component root.",
                        project_import.id
                    ));
                }
                Ok(ManagedProjectImport {
                    id: project_import.id.clone(),
                    label: project_import.label.clone(),
                    source_path: canonical.display().to_string(),
                    source_crs: project_import.source_crs.clone(),
                    layer_name: project_import.layer_name.clone(),
                    layer_kind: project_import.layer_kind.clone(),
                })
            })
            .collect()
    }

    fn component_status(&self, component: &DependencyComponent) -> DependencyComponentStatus {
        let path = self.component_path(component);
        let marker_valid = self.has_valid_marker(component);
        let resolved_references = marker_valid.then(|| self.resolve_references(component));
        let resolved_project_imports =
            marker_valid.then(|| self.resolve_project_imports(component));
        let managed_references = resolved_references
            .as_ref()
            .and_then(|result| result.as_ref().ok())
            .cloned()
            .unwrap_or_default();
        let managed_project_imports = resolved_project_imports
            .as_ref()
            .and_then(|result| result.as_ref().ok())
            .cloned()
            .unwrap_or_default();
        let update_available =
            !self.is_ready(&component.id) && self.has_owned_previous_version(component);
        let (state, detail) = if marker_valid
            && resolved_references.as_ref().is_some_and(Result::is_ok)
            && resolved_project_imports.as_ref().is_some_and(Result::is_ok)
        {
            (
                "ready",
                "Managed component identity and integrity marker are valid.".to_string(),
            )
        } else if marker_valid {
            (
                "invalid",
                resolved_references
                    .and_then(Result::err)
                    .or_else(|| resolved_project_imports.and_then(Result::err))
                    .unwrap_or_else(|| "Managed component references are invalid.".to_string()),
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
            managed_references,
            managed_project_imports,
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

fn install_uv_managed_python(
    payload: &Path,
    mirror: &Path,
    version: &str,
    should_cancel: impl Fn() -> bool + Copy,
) -> Result<(), String> {
    let uv = payload.join("uv.exe");
    if !uv.is_file() {
        return Err("uv-managed Python bootstrap requires payload/uv.exe".to_string());
    }
    let python_root = payload.join("python-installations");
    fs::create_dir_all(&python_root).map_err(|error| error.to_string())?;
    let canonical_payload = payload
        .canonicalize()
        .map_err(|error| format!("could not validate uv bootstrap payload: {error}"))?;
    let canonical_uv = uv
        .canonicalize()
        .map_err(|error| format!("could not validate uv bootstrap executable: {error}"))?;
    let canonical_mirror = mirror
        .canonicalize()
        .map_err(|error| format!("could not validate uv bootstrap mirror: {error}"))?;
    let canonical_python_root = python_root
        .canonicalize()
        .map_err(|error| format!("could not validate uv Python directory: {error}"))?;
    if !canonical_uv.starts_with(&canonical_payload)
        || !canonical_python_root.starts_with(&canonical_payload)
    {
        return Err("uv-managed Python bootstrap path escaped its staged payload".to_string());
    }
    let mirror_url = Url::from_directory_path(&canonical_mirror)
        .map_err(|_| "could not convert the confined Python mirror to a file URL".to_string())?;
    let cache = mirror
        .parent()
        .ok_or_else(|| "uv bootstrap staging root is unavailable".to_string())?
        .join("uv-cache");
    fs::create_dir_all(&cache).map_err(|error| error.to_string())?;
    let mut command = uv_managed_python_install_command(
        &canonical_uv,
        mirror_url.as_str(),
        &canonical_python_root,
        &cache,
        version,
    );
    let output = run_bounded_process_cancellable(
        &mut command,
        UV_PYTHON_BOOTSTRAP_TIMEOUT,
        UV_PYTHON_BOOTSTRAP_OUTPUT_LIMIT,
        should_cancel,
    )
    .map_err(|error| format!("uv-managed Python installation failed: {error}"))?;
    if !output.status.success() {
        return Err(format!(
            "uv-managed Python installation returned a non-zero exit status: {}",
            bounded_process_detail(&output.stderr, &output.stdout)
        ));
    }
    let python = find_named_file(&canonical_python_root, "python.exe", 5)
        .or_else(|| find_named_file(&canonical_python_root, "python3.exe", 5))
        .ok_or_else(|| "uv did not install a discoverable Python executable".to_string())?;
    let canonical_python = python
        .canonicalize()
        .map_err(|error| format!("could not validate installed Python executable: {error}"))?;
    if !canonical_python.starts_with(&canonical_python_root) {
        return Err("installed Python executable escaped its managed directory".to_string());
    }
    let mut probe = Command::new(&canonical_python);
    probe.arg("--version");
    let probe_output = run_bounded_process_cancellable(
        &mut probe,
        Duration::from_secs(30),
        64 * 1024,
        should_cancel,
    )
    .map_err(|error| format!("managed Python version probe failed: {error}"))?;
    let detail = bounded_process_detail(&probe_output.stdout, &probe_output.stderr);
    if !probe_output.status.success() || detail.trim() != format!("Python {version}") {
        return Err(format!(
            "managed Python version probe did not return exact Python {version}: {detail}"
        ));
    }
    Ok(())
}

fn install_uv_wheel_environment(
    uv: &Path,
    python_installations: &Path,
    wheel: &Path,
    payload: &Path,
    strategy: &DependencyInstallStrategy,
    should_cancel: impl Fn() -> bool + Copy,
) -> Result<(), String> {
    let canonical_uv = uv
        .canonicalize()
        .map_err(|error| format!("could not validate managed uv: {error}"))?;
    let canonical_python_installations = python_installations
        .canonicalize()
        .map_err(|error| format!("could not validate managed Python root: {error}"))?;
    let canonical_wheel = wheel
        .canonicalize()
        .map_err(|error| format!("could not validate verified wheel: {error}"))?;
    let canonical_payload = payload
        .canonicalize()
        .map_err(|error| format!("could not validate staged environment: {error}"))?;
    let staging = canonical_payload
        .parent()
        .ok_or_else(|| "staged environment root is unavailable".to_string())?;
    if canonical_wheel.parent() != Some(staging)
        || canonical_wheel.file_name().and_then(|value| value.to_str())
            != Some(strategy.wheel_file_name.as_str())
    {
        return Err("verified wheel escaped its fixed staging identity".to_string());
    }
    let cache = staging.join("uv-wheel-cache");
    fs::create_dir_all(&cache).map_err(|error| error.to_string())?;

    let mut venv = uv_wheel_venv_command(
        &canonical_uv,
        &canonical_python_installations,
        &canonical_payload,
        &cache,
        &strategy.python_version,
    );
    let output = run_bounded_process_cancellable(
        &mut venv,
        UV_WHEEL_ENVIRONMENT_TIMEOUT,
        UV_WHEEL_ENVIRONMENT_OUTPUT_LIMIT,
        should_cancel,
    )
    .map_err(|error| format!("managed wheel environment creation failed: {error}"))?;
    if !output.status.success() {
        return Err(format!(
            "managed wheel environment creation returned a non-zero exit status: {}",
            bounded_process_detail(&output.stderr, &output.stdout)
        ));
    }

    let python = canonical_payload.join(if cfg!(windows) {
        "Scripts/python.exe"
    } else {
        "bin/python"
    });
    let mut install = uv_wheel_install_command(&canonical_uv, &python, &canonical_wheel, &cache);
    let output = run_bounded_process_cancellable(
        &mut install,
        UV_WHEEL_ENVIRONMENT_TIMEOUT,
        UV_WHEEL_ENVIRONMENT_OUTPUT_LIMIT,
        should_cancel,
    )
    .map_err(|error| format!("verified wheel installation failed: {error}"))?;
    if !output.status.success() {
        return Err(format!(
            "verified wheel installation returned a non-zero exit status: {}",
            bounded_process_detail(&output.stderr, &output.stdout)
        ));
    }
    fs::write(
        canonical_payload.join(MANAGED_ENVIRONMENT_MARKER),
        format!("{}-{}", strategy.package, strategy.package_version),
    )
    .map_err(|error| error.to_string())?;
    managed_runtime::probe_managed_environment(
        &canonical_payload,
        &format!("{}-{}", strategy.package, strategy.package_version),
    )?;
    Ok(())
}

fn validate_ffmpeg_payload(
    payload: &Path,
    should_cancel: impl Fn() -> bool + Copy,
) -> Result<(), String> {
    let root = payload.join(FFMPEG_ARCHIVE_ROOT);
    let expected_files = [
        (
            "LICENSE",
            35_147,
            "8ceb4b9ee5adedde47b31e975c1d90c73ad27b6b165a1dcd80c7c545eb65b903",
        ),
        (
            "README.txt",
            45_240,
            "35ef02f329d062a1b49397a2869718264b5f12776517791c02126f0efd323528",
        ),
        (
            "bin/ffmpeg.exe",
            227_398_656,
            "09948d4cdd0650da6ff5a87577469f2a218dc2615ae379f8f734d24c49de0f73",
        ),
        (
            "bin/ffprobe.exe",
            227_193_344,
            "a6618e99bb58869ded3c6f37b53aa1a8d701c3591dbb7b5b317d47369c112be2",
        ),
    ];
    for (relative, expected_size, expected_hash) in expected_files {
        let path = root.join(relative);
        let metadata = fs::metadata(&path)
            .map_err(|error| format!("managed FFmpeg is missing {relative}: {error}"))?;
        if !metadata.is_file() || metadata.len() != expected_size {
            return Err(format!(
                "managed FFmpeg {relative} size drifted from {expected_size} bytes"
            ));
        }
        let actual = hash_file_cancellable(&path, should_cancel)?;
        if !actual.eq_ignore_ascii_case(expected_hash) {
            return Err(format!("managed FFmpeg {relative} SHA-256 drifted"));
        }
    }
    probe_ffmpeg_executable(
        &root.join("bin/ffmpeg.exe"),
        FFMPEG_VERSION_PREFIX,
        should_cancel,
    )?;
    probe_ffmpeg_executable(
        &root.join("bin/ffprobe.exe"),
        FFPROBE_VERSION_PREFIX,
        should_cancel,
    )?;
    Ok(())
}

fn hash_file_cancellable(path: &Path, should_cancel: impl Fn() -> bool) -> Result<String, String> {
    let mut file = File::open(path).map_err(|error| error.to_string())?;
    let mut hash = Sha256::new();
    let mut buffer = [0_u8; 1024 * 1024];
    loop {
        if should_cancel() {
            return Err("managed component validation cancelled".to_string());
        }
        let read = file.read(&mut buffer).map_err(|error| error.to_string())?;
        if read == 0 {
            return Ok(hex::encode(hash.finalize()));
        }
        hash.update(&buffer[..read]);
    }
}

fn probe_ffmpeg_executable(
    executable: &Path,
    expected_prefix: &str,
    should_cancel: impl Fn() -> bool,
) -> Result<(), String> {
    let mut command = Command::new(executable);
    command.arg("-version");
    let output = run_bounded_process_cancellable(
        &mut command,
        Duration::from_secs(30),
        256 * 1024,
        should_cancel,
    )
    .map_err(|error| format!("managed FFmpeg version probe failed: {error}"))?;
    let detail = bounded_process_detail(&output.stdout, &output.stderr);
    if !output.status.success()
        || !detail
            .lines()
            .next()
            .is_some_and(|line| line.starts_with(expected_prefix))
    {
        return Err(format!(
            "managed FFmpeg version probe did not match {expected_prefix}: {detail}"
        ));
    }
    Ok(())
}

fn uv_wheel_venv_command(
    uv: &Path,
    python_installations: &Path,
    payload: &Path,
    cache: &Path,
    python_version: &str,
) -> Command {
    let mut command = Command::new(uv);
    command
        .arg("venv")
        .arg("--allow-existing")
        .arg("--no-project")
        .arg("--python")
        .arg(python_version)
        .arg("--managed-python")
        .arg("--no-python-downloads")
        .arg("--link-mode")
        .arg("copy")
        .arg("--offline")
        .arg("--no-config")
        .arg("--no-progress")
        .arg(payload)
        .env_remove("VIRTUAL_ENV")
        .env_remove("UV_PROJECT")
        .env_remove("UV_PROJECT_ENVIRONMENT")
        .env_remove("UV_PYTHON_INSTALL_DIR")
        .env_remove("UV_CACHE_DIR")
        .env("UV_PYTHON_INSTALL_DIR", python_installations)
        .env("UV_CACHE_DIR", cache);
    command
}

fn uv_wheel_install_command(uv: &Path, python: &Path, wheel: &Path, cache: &Path) -> Command {
    let mut command = Command::new(uv);
    command
        .arg("pip")
        .arg("install")
        .arg("--python")
        .arg(python)
        .arg("--no-deps")
        .arg("--only-binary")
        .arg(":all:")
        .arg("--no-index")
        .arg("--link-mode")
        .arg("copy")
        .arg("--offline")
        .arg("--no-config")
        .arg("--no-progress")
        .arg(wheel)
        .env_remove("VIRTUAL_ENV")
        .env_remove("UV_PROJECT")
        .env_remove("UV_PROJECT_ENVIRONMENT")
        .env_remove("UV_CACHE_DIR")
        .env("UV_CACHE_DIR", cache);
    command
}

fn uv_managed_python_install_command(
    uv: &Path,
    mirror_url: &str,
    python_root: &Path,
    cache: &Path,
    version: &str,
) -> Command {
    let mut command = Command::new(uv);
    command
        .arg("python")
        .arg("install")
        .arg("--install-dir")
        .arg(python_root)
        .arg("--no-bin")
        .arg("--no-registry")
        .arg("--managed-python")
        .arg("--no-progress")
        .arg("--offline")
        .arg("--no-config")
        .arg("--mirror")
        .arg(mirror_url)
        .arg(version)
        .env_remove("UV_PYTHON_DOWNLOADS_JSON_URL")
        .env_remove("UV_PYTHON_DOWNLOADS")
        .env_remove("UV_PYTHON_INSTALL_DIR")
        .env_remove("UV_PYTHON_INSTALL_BIN")
        .env_remove("UV_PYTHON_INSTALL_REGISTRY")
        .env_remove("UV_CACHE_DIR")
        .env("UV_PYTHON_INSTALL_DIR", python_root)
        .env("UV_PYTHON_INSTALL_BIN", "0")
        .env("UV_PYTHON_INSTALL_REGISTRY", "0")
        .env("UV_CACHE_DIR", cache);
    command
}

fn bounded_process_detail(primary: &[u8], secondary: &[u8]) -> String {
    let primary = String::from_utf8_lossy(primary).trim().to_string();
    if !primary.is_empty() {
        return primary.chars().take(2048).collect();
    }
    String::from_utf8_lossy(secondary)
        .trim()
        .chars()
        .take(2048)
        .collect()
}

fn is_exact_python_version(value: &str) -> bool {
    let parts = value.split('.').collect::<Vec<_>>();
    parts.len() == 3
        && parts.iter().all(|part| {
            !part.is_empty() && part.chars().all(|character| character.is_ascii_digit())
        })
}

pub fn validate_catalog(catalog: &DependencyCatalog) -> Result<(), String> {
    if catalog.schema_version != 1 {
        return Err("unsupported dependency catalog schema".to_string());
    }
    if catalog.platform != "windows-x86_64" {
        return Err("dependency catalog must target windows-x86_64".to_string());
    }
    let mut ids = HashSet::new();
    let mut licenses = HashMap::new();
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
        if let Some(previous_digest) = licenses.insert(
            component.license.id.clone(),
            component.license.digest.clone(),
        ) {
            if previous_digest != component.license.digest {
                return Err(format!(
                    "license {} has inconsistent digests",
                    component.license.id
                ));
            }
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
            if let Some(size_bytes) = artifact.size_bytes {
                if size_bytes == 0 || size_bytes > artifact.max_bytes {
                    return Err(format!("{} artifact exact size is invalid", component.id));
                }
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
            if let Some(file_name) = &artifact.file_name {
                validate_relative_path(Path::new(file_name))
                    .map_err(|_| format!("{} artifact file name is unsafe", component.id))?;
            }
        }
        if let Some(bootstrap) = &component.bootstrap {
            if component.id != "uv-python" || bootstrap.kind != "uv-managed-python" {
                return Err(format!("{} bootstrap kind is unsupported", component.id));
            }
            if !is_exact_python_version(&bootstrap.version) {
                return Err(format!(
                    "{} bootstrap Python version is invalid",
                    component.id
                ));
            }
            validate_https_url(&bootstrap.source_url)?;
            validate_https_url(&bootstrap.license.url)?;
            if bootstrap.license.id.trim().is_empty()
                || bootstrap.license.label.trim().is_empty()
                || bootstrap.license.digest.trim().is_empty()
            {
                return Err(format!(
                    "{} bootstrap license evidence is incomplete",
                    component.id
                ));
            }
            if let Some(previous_digest) = licenses.insert(
                bootstrap.license.id.clone(),
                bootstrap.license.digest.clone(),
            ) {
                if previous_digest != bootstrap.license.digest {
                    return Err(format!(
                        "license {} has inconsistent digests",
                        bootstrap.license.id
                    ));
                }
            }
            if component.availability == "available" && bootstrap.artifact.is_none() {
                return Err(format!(
                    "{} claims availability without a bootstrap artifact",
                    component.id
                ));
            }
            if let Some(artifact) = &bootstrap.artifact {
                validate_download_url(&artifact.url)?;
                if artifact.max_bytes == 0 {
                    return Err(format!(
                        "{} bootstrap has no download size limit",
                        component.id
                    ));
                }
                if artifact.sha256.len() != 64
                    || !artifact
                        .sha256
                        .chars()
                        .all(|value| value.is_ascii_hexdigit())
                {
                    return Err(format!(
                        "{} bootstrap artifact SHA-256 is invalid",
                        component.id
                    ));
                }
                if artifact.archive != "file" {
                    return Err(format!(
                        "{} bootstrap archive type is unsupported",
                        component.id
                    ));
                }
                let file_name = artifact
                    .file_name
                    .as_deref()
                    .ok_or_else(|| format!("{} bootstrap mirror path is missing", component.id))?;
                validate_relative_path(Path::new(file_name))
                    .map_err(|_| format!("{} bootstrap mirror path is unsafe", component.id))?;
                if Path::new(file_name).components().count() < 2
                    || !file_name.to_ascii_lowercase().ends_with(".tar.gz")
                {
                    return Err(format!("{} bootstrap mirror path is invalid", component.id));
                }
            }
        }
        if let Some(strategy) = &component.install_strategy {
            let artifact = component
                .artifact
                .as_ref()
                .ok_or_else(|| format!("{} install strategy has no artifact", component.id))?;
            let valid = match strategy.kind.as_str() {
                "uv-wheel-environment" => {
                    component.id == "managed-valhalla"
                        && strategy.python_version == "3.12.13"
                        && strategy.package == "pyvalhalla"
                        && strategy.package_version == "3.7.0"
                        && component.version == strategy.package_version
                        && artifact.archive == "file"
                        && artifact.file_name.as_deref()
                            == Some(strategy.wheel_file_name.as_str())
                        && strategy.wheel_file_name
                            == "pyvalhalla-3.7.0-cp312-abi3-win_amd64.whl"
                        && component.dependencies == ["uv-python"]
                }
                "verified-ffmpeg-archive" => {
                    component.id == "ffmpeg"
                        && component.version == "8.1.1-audited-windows-x64"
                        && strategy.python_version.is_empty()
                        && strategy.package.is_empty()
                        && strategy.package_version.is_empty()
                        && strategy.wheel_file_name.is_empty()
                        && component.license.digest
                            == "c31bd2401e4b09ced92dc957006d20997edbd7211301d9ca06e94802a2e11b50"
                        && artifact.url == "https://github.com/GyanD/codexffmpeg/releases/download/8.1.1/ffmpeg-8.1.1-full_build.zip"
                        && artifact.sha256
                            == "49b28c5f16addd40239a66949973458769b7056fb7752c30ac0d53389d09a552"
                        && artifact.max_bytes == 252_194_496
                        && artifact.archive == "zip"
                        && artifact.file_name.as_deref()
                            == Some("ffmpeg-8.1.1-full_build.zip")
                        && component.dependencies.is_empty()
                }
                "roadwatcher-release-archive" => {
                    let descriptor = component.artifact_manifest.as_ref().ok_or_else(|| {
                        format!(
                            "{} RoadWatcher release archive has no manifest descriptor",
                            component.id
                        )
                    })?;
                    validate_roadwatcher_release_catalog_contract(component, artifact, descriptor)?;
                    true
                }
                _ => false,
            };
            if !valid {
                return Err(format!(
                    "{} install strategy drifted from its fixed backend contract",
                    component.id
                ));
            }
        } else if component.artifact_manifest.is_some() {
            return Err(format!(
                "{} manifest descriptor is only valid for a fixed RoadWatcher release archive",
                component.id
            ));
        }
        if component
            .artifact
            .as_ref()
            .is_some_and(|artifact| artifact.size_bytes.is_some())
            && component
                .install_strategy
                .as_ref()
                .is_none_or(|strategy| strategy.kind != "roadwatcher-release-archive")
        {
            return Err(format!(
                "{} exact artifact size is only valid for a fixed RoadWatcher release archive",
                component.id
            ));
        }
        let mut reference_ids = HashSet::new();
        for reference in &component.references {
            if reference.id.is_empty()
                || !reference.id.chars().all(|value| {
                    value.is_ascii_lowercase() || value.is_ascii_digit() || value == '-'
                })
                || !reference_ids.insert(reference.id.clone())
            {
                return Err(format!(
                    "{} has an invalid or duplicate managed reference id",
                    component.id
                ));
            }
            if !matches!(reference.kind.as_str(), "file" | "directory") {
                return Err(format!(
                    "{} managed reference kind is unsupported",
                    component.id
                ));
            }
            validate_relative_path(Path::new(&reference.path))
                .map_err(|_| format!("{} managed reference path is unsafe", component.id))?;
        }
        if component.bootstrap.is_some()
            && (!component.references.iter().any(|reference| {
                reference.id == "executable"
                    && reference.path == "uv.exe"
                    && reference.kind == "file"
            }) || !component.references.iter().any(|reference| {
                reference.id == "python-installations"
                    && reference.path == "python-installations"
                    && reference.kind == "directory"
            }))
        {
            return Err(format!(
                "{} bootstrap references do not match the fixed backend contract",
                component.id
            ));
        }
        if let Some(strategy) = &component.install_strategy {
            let valid_reference = match strategy.kind.as_str() {
                "uv-wheel-environment" => {
                    component.references.len() == 1
                        && component.references.iter().any(|reference| {
                            reference.id == "service-executable"
                                && reference.path == "Scripts/valhalla_service.exe"
                                && reference.kind == "file"
                        })
                }
                "verified-ffmpeg-archive" => {
                    component.references.len() == 1
                        && component.references.iter().any(|reference| {
                            reference.id == "binary-directory"
                                && reference.path == "ffmpeg-8.1.1-full_build/bin"
                                && reference.kind == "directory"
                        })
                }
                "roadwatcher-release-archive" => match component.id.as_str() {
                    "york-valhalla-tiles" => {
                        component.references.len() == 2
                            && component.references.iter().any(|reference| {
                                reference.id == "config"
                                    && reference.path == "valhalla.json"
                                    && reference.kind == "file"
                            })
                            && component.references.iter().any(|reference| {
                                reference.id == "tiles"
                                    && reference.path == "tiles"
                                    && reference.kind == "directory"
                            })
                    }
                    "cv-yolo11n" => {
                        component.references.len() == 2
                            && component.references.iter().any(|reference| {
                                reference.id == "model"
                                    && reference.path == "yolo11n.onnx"
                                    && reference.kind == "file"
                            })
                            && component.references.iter().any(|reference| {
                                reference.id == "labels"
                                    && reference.path == "labels.txt"
                                    && reference.kind == "file"
                            })
                    }
                    _ => false,
                },
                _ => false,
            };
            if !valid_reference {
                return Err(format!(
                    "{} managed reference drifted from its fixed backend contract",
                    component.id
                ));
            }
        }
        let mut import_ids = HashSet::new();
        for project_import in &component.project_imports {
            if project_import.id.is_empty()
                || !project_import.id.chars().all(|value| {
                    value.is_ascii_lowercase() || value.is_ascii_digit() || value == '-'
                })
                || !import_ids.insert(project_import.id.clone())
                || project_import.label.trim().is_empty()
                || project_import.source_crs.trim().is_empty()
                || !matches!(
                    project_import.layer_kind.as_str(),
                    "mixed" | "traffic_light" | "stop_sign" | "bike_lane" | "crosswalk" | "other"
                )
            {
                return Err(format!(
                    "{} has an invalid managed project import",
                    component.id
                ));
            }
            validate_relative_path(Path::new(&project_import.path))
                .map_err(|_| format!("{} managed project import path is unsafe", component.id))?;
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

fn validate_roadwatcher_release_catalog_contract(
    component: &DependencyComponent,
    artifact: &DependencyArtifact,
    descriptor: &DependencyArtifactManifestDescriptor,
) -> Result<(), String> {
    let expected_kind = match component.id.as_str() {
        "york-valhalla-tiles" => "tiles",
        "cv-yolo11n" => "model",
        _ => {
            return Err(format!(
                "{} is not an approved RoadWatcher-generated release component",
                component.id
            ))
        }
    };
    if component.source_url != "https://github.com/Remi-Z/RoadWatcher/releases" {
        return Err(format!(
            "{} release source must be the canonical RoadWatcher releases page",
            component.id
        ));
    }
    if component.bootstrap.is_some()
        || !component.project_imports.is_empty()
        || !component.license.consent_required
    {
        return Err(format!(
            "{} release component drifted from the fixed owner-approved contract",
            component.id
        ));
    }
    let expected_dependencies: &[&str] = match component.id.as_str() {
        "york-valhalla-tiles" => &["managed-valhalla"],
        "cv-yolo11n" => &[],
        _ => unreachable!("release component ID was checked above"),
    };
    if component
        .dependencies
        .iter()
        .map(String::as_str)
        .collect::<Vec<_>>()
        != expected_dependencies
    {
        return Err(format!(
            "{} release component has unexpected dependencies",
            component.id
        ));
    }
    if artifact.archive != "zip" || artifact.max_bytes > ROADWATCHER_RELEASE_ARCHIVE_MAX_BYTES {
        return Err(format!(
            "{} release archive has an unsupported size or format",
            component.id
        ));
    }
    let exact_size = artifact.size_bytes.ok_or_else(|| {
        format!(
            "{} release archive must record its exact verified byte size",
            component.id
        )
    })?;
    if exact_size > artifact.max_bytes {
        return Err(format!(
            "{} release archive exact size exceeds its download limit",
            component.id
        ));
    }
    let archive_name = artifact
        .file_name
        .as_deref()
        .ok_or_else(|| format!("{} release archive file name is missing", component.id))?;
    validate_release_file_name(archive_name, ".zip")?;
    let (archive_tag, archive_url_name) = parse_roadwatcher_release_download(&artifact.url)?;
    if archive_url_name != archive_name {
        return Err(format!(
            "{} release archive URL file name does not match the catalog",
            component.id
        ));
    }
    if descriptor.kind != expected_kind
        || descriptor.max_bytes == 0
        || descriptor.max_bytes > ROADWATCHER_RELEASE_MANIFEST_MAX_BYTES
        || !is_sha256(&descriptor.sha256)
    {
        return Err(format!(
            "{} release manifest descriptor is invalid",
            component.id
        ));
    }
    let expected_manifest_name = archive_name
        .strip_suffix(".zip")
        .map(|stem| format!("{stem}.manifest.json"))
        .ok_or_else(|| format!("{} release archive file name is invalid", component.id))?;
    let (manifest_tag, manifest_url_name) = parse_roadwatcher_release_download(&descriptor.url)?;
    if archive_tag != manifest_tag || manifest_url_name != expected_manifest_name {
        return Err(format!(
            "{} release manifest must be a companion asset from the same release tag",
            component.id
        ));
    }
    Ok(())
}

fn validate_release_file_name(file_name: &str, extension: &str) -> Result<(), String> {
    validate_relative_path(Path::new(file_name))?;
    if Path::new(file_name).components().count() != 1
        || file_name.len() > 200
        || !file_name.is_ascii()
        || !file_name.ends_with(extension)
    {
        return Err("RoadWatcher release asset file name is unsafe".to_string());
    }
    Ok(())
}

fn parse_roadwatcher_release_download(url: &str) -> Result<(String, String), String> {
    validate_download_url(url)?;
    let parsed = Url::parse(url)
        .map_err(|error| format!("RoadWatcher release asset URL is invalid: {error}"))?;
    if parsed.scheme() != "https"
        || parsed.host_str() != Some("github.com")
        || parsed.port().is_some()
        || !parsed.username().is_empty()
        || parsed.password().is_some()
        || parsed.query().is_some()
        || parsed.fragment().is_some()
    {
        return Err("RoadWatcher release asset URL is not canonical".to_string());
    }
    let segments = parsed
        .path_segments()
        .ok_or_else(|| "RoadWatcher release asset URL has no path".to_string())?
        .collect::<Vec<_>>();
    let [owner, repository, releases, download, tag, file_name] = segments.as_slice() else {
        return Err("RoadWatcher release asset URL has an unexpected path".to_string());
    };
    if *owner != ROADWATCHER_RELEASE_OWNER
        || *repository != ROADWATCHER_RELEASE_REPOSITORY
        || *releases != "releases"
        || *download != "download"
        || !valid_release_tag(tag)
    {
        return Err(
            "RoadWatcher release asset URL is not an approved release download".to_string(),
        );
    }
    validate_release_file_name(file_name, "")?;
    Ok(((*tag).to_string(), (*file_name).to_string()))
}

fn valid_release_tag(value: &str) -> bool {
    !value.is_empty()
        && value.len() <= 100
        && value.chars().all(|character| {
            character.is_ascii_alphanumeric() || matches!(character, '.' | '_' | '-')
        })
}

fn is_sha256(value: &str) -> bool {
    value.len() == 64 && value.chars().all(|character| character.is_ascii_hexdigit())
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
        let mut current = Url::parse(url)
            .map_err(|error| format!("dependency download URL is invalid: {error}"))?;
        for redirect_count in 0..=5 {
            validate_download_url(current.as_str())?;
            let mut request = ureq::get(current.as_str());
            if let Some(resume) = resume {
                request = request
                    .header("Range", &format!("bytes={}-", resume.offset))
                    .header("If-Range", &resume.etag);
            }
            let response = request
                .config()
                .max_redirects(0)
                .max_redirects_will_error(false)
                .build()
                .call()
                .map_err(|error| format!("dependency download failed: {error}"))?;
            if response.status().is_redirection() {
                if redirect_count == 5 {
                    return Err("dependency download exceeded its redirect limit".to_string());
                }
                let location = response
                    .headers()
                    .get("location")
                    .and_then(|value| value.to_str().ok())
                    .ok_or_else(|| "dependency redirect omitted a valid Location".to_string())?;
                current = resolve_allowed_redirect(&current, location)?;
                continue;
            }
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
            return Ok(DownloadResponse {
                status,
                etag,
                accepts_ranges,
                content_range,
                reader: Box::new(response.into_body().into_reader()),
            });
        }
        unreachable!("bounded dependency redirect loop always returns")
    }
}

fn resolve_allowed_redirect(current: &Url, location: &str) -> Result<Url, String> {
    if location.contains(['\r', '\n']) {
        return Err("dependency redirect Location is unsafe".to_string());
    }
    let next = current
        .join(location)
        .map_err(|error| format!("dependency redirect Location is invalid: {error}"))?;
    validate_download_url(next.as_str())?;
    Ok(next)
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
    extract_zip_bounded(archive_path, destination, usize::MAX, u64::MAX, cancelled)
}

fn extract_zip_bounded(
    archive_path: &Path,
    destination: &Path,
    max_entries: usize,
    max_unpacked_bytes: u64,
    cancelled: impl Fn() -> bool,
) -> Result<(), String> {
    let file = File::open(archive_path).map_err(|error| error.to_string())?;
    let mut archive =
        ZipArchive::new(file).map_err(|error| format!("dependency ZIP is invalid: {error}"))?;
    if archive.len() > max_entries {
        return Err("dependency ZIP exceeds its entry-count limit".to_string());
    }
    let mut unpacked_bytes = 0_u64;
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
            let remaining = max_unpacked_bytes.saturating_sub(unpacked_bytes);
            if entry.size() > remaining {
                return Err("dependency ZIP exceeds its unpacked-size limit".to_string());
            }
            if let Some(parent) = output_path.parent() {
                fs::create_dir_all(parent).map_err(|error| error.to_string())?;
            }
            let mut output = File::create(&output_path).map_err(|error| error.to_string())?;
            let copied = {
                let mut bounded_entry = (&mut entry).take(remaining.saturating_add(1));
                io::copy(&mut bounded_entry, &mut output).map_err(|error| error.to_string())?
            };
            if copied > remaining {
                return Err("dependency ZIP exceeds its unpacked-size limit".to_string());
            }
            unpacked_bytes = unpacked_bytes
                .checked_add(copied)
                .ok_or_else(|| "dependency ZIP unpacked-size overflow".to_string())?;
        }
    }
    Ok(())
}

fn verify_roadwatcher_release_manifest(
    component: &DependencyComponent,
    artifact: &DependencyArtifact,
    descriptor: &DependencyArtifactManifestDescriptor,
    archive_path: &Path,
    manifest_path: &Path,
) -> Result<(), String> {
    let exact_size = artifact.size_bytes.ok_or_else(|| {
        "RoadWatcher release archive is missing its exact catalog byte size".to_string()
    })?;
    let metadata = fs::metadata(archive_path)
        .map_err(|error| format!("could not inspect verified release archive: {error}"))?;
    if !metadata.is_file() || metadata.len() != exact_size {
        return Err(
            "verified release archive does not match its exact catalog byte size".to_string(),
        );
    }
    let bytes = read_bounded_file(manifest_path, descriptor.max_bytes, "release manifest")?;
    let manifest: RoadWatcherReleaseManifest = serde_json::from_slice(&bytes)
        .map_err(|error| format!("RoadWatcher release manifest is invalid JSON: {error}"))?;
    validate_roadwatcher_release_manifest(&manifest, component, artifact, descriptor)
}

fn validate_roadwatcher_release_manifest(
    manifest: &RoadWatcherReleaseManifest,
    component: &DependencyComponent,
    artifact: &DependencyArtifact,
    descriptor: &DependencyArtifactManifestDescriptor,
) -> Result<(), String> {
    if manifest.schema_version != 1
        || manifest.id != component.id
        || manifest.kind != descriptor.kind
        || manifest.version != component.version
        || manifest.platform != "windows-x86_64"
    {
        return Err("RoadWatcher release manifest identity does not match the catalog".to_string());
    }
    validate_iso_utc(&manifest.generated_at, "release manifest generatedAt")?;
    let file_name = artifact.file_name.as_deref().ok_or_else(|| {
        "RoadWatcher release archive file name is missing from the catalog".to_string()
    })?;
    let exact_size = artifact.size_bytes.ok_or_else(|| {
        "RoadWatcher release archive exact byte size is missing from the catalog".to_string()
    })?;
    validate_release_file_name(&manifest.artifact.file_name, ".zip")?;
    if manifest.artifact.file_name != file_name
        || manifest.artifact.size_bytes != exact_size
        || !manifest
            .artifact
            .sha256
            .eq_ignore_ascii_case(&artifact.sha256)
        || manifest.artifact.license.id != component.license.id
        || manifest.artifact.license.url != component.license.url
    {
        return Err(
            "RoadWatcher release manifest artifact identity does not match the catalog".to_string(),
        );
    }
    validate_release_license(&manifest.artifact.license, "release artifact license")?;
    if manifest.sources.is_empty() || manifest.tools.is_empty() {
        return Err("RoadWatcher release manifest lacks required build provenance".to_string());
    }
    let mut source_ids = HashSet::new();
    for source in &manifest.sources {
        if !valid_component_id(&source.id) || !source_ids.insert(source.id.clone()) {
            return Err(
                "RoadWatcher release manifest has an invalid or duplicate source ID".to_string(),
            );
        }
        validate_https_url(&source.url)?;
        validate_bounded_text(&source.version, "release source version", 200)?;
        validate_release_license(&source.license, "release source license")?;
        if !is_sha256(&source.downloaded_sha256) || source.size_bytes == 0 {
            return Err("RoadWatcher release manifest source identity is invalid".to_string());
        }
        if let Some(publisher_sha256) = &source.publisher_sha256 {
            if !is_sha256(publisher_sha256) {
                return Err("RoadWatcher release manifest publisher hash is invalid".to_string());
            }
        }
        if let Some(etag) = &source.etag {
            validate_bounded_text(etag, "release source ETag", 512)?;
        }
        if let Some(last_modified) = &source.last_modified {
            validate_bounded_text(last_modified, "release source Last-Modified", 512)?;
        }
        let has_retrieval_identity = source.etag.is_some() || source.last_modified.is_some();
        if source.publisher_sha256.is_none() && !has_retrieval_identity {
            return Err(
                "RoadWatcher release manifest source lacks publisher or retrieval identity"
                    .to_string(),
            );
        }
        validate_iso_utc(&source.retrieved_at, "release source retrievedAt")?;
    }
    let mut tool_names = HashSet::new();
    for tool in &manifest.tools {
        validate_bounded_text(&tool.name, "release build tool name", 100)?;
        validate_bounded_text(&tool.version, "release build tool version", 100)?;
        if !tool_names.insert(tool.name.clone()) {
            return Err("RoadWatcher release manifest has duplicate build tools".to_string());
        }
    }
    validate_bounded_text(&manifest.build.recipe, "release build recipe", 260)?;
    validate_bounded_text(
        &manifest.build.recipe_version,
        "release build recipe version",
        100,
    )?;
    for (key, value) in &manifest.build.parameters {
        if !valid_build_parameter_name(key)
            || !matches!(
                value,
                serde_json::Value::String(_)
                    | serde_json::Value::Number(_)
                    | serde_json::Value::Bool(_)
            )
        {
            return Err("RoadWatcher release manifest build parameters are invalid".to_string());
        }
        if let serde_json::Value::String(value) = value {
            validate_bounded_text(value, "release build parameter", 512)?;
        }
    }
    Ok(())
}

fn read_bounded_file(path: &Path, max_bytes: u64, label: &str) -> Result<Vec<u8>, String> {
    let metadata =
        fs::metadata(path).map_err(|error| format!("could not inspect {label}: {error}"))?;
    if !metadata.is_file() || metadata.len() > max_bytes {
        return Err(format!("{label} exceeds its bounded size limit"));
    }
    let capacity = usize::try_from(metadata.len()).unwrap_or(0);
    let mut bytes = Vec::with_capacity(capacity);
    let mut input = File::open(path).map_err(|error| format!("could not read {label}: {error}"))?;
    Read::take(&mut input, max_bytes.saturating_add(1))
        .read_to_end(&mut bytes)
        .map_err(|error| format!("could not read {label}: {error}"))?;
    if bytes.len() as u64 > max_bytes {
        return Err(format!("{label} exceeds its bounded size limit"));
    }
    Ok(bytes)
}

fn validate_roadwatcher_release_payload(
    component: &DependencyComponent,
    payload: &Path,
    cancelled: impl Fn() -> bool + Copy,
) -> Result<(), String> {
    match component.id.as_str() {
        "york-valhalla-tiles" => validate_york_valhalla_payload(payload, cancelled),
        "cv-yolo11n" => validate_cv_yolo_payload(payload),
        _ => Err("RoadWatcher release payload has an unsupported component identity".to_string()),
    }
}

fn validate_cv_yolo_payload(payload: &Path) -> Result<(), String> {
    validate_exact_payload_root(payload, &[("yolo11n.onnx", "file"), ("labels.txt", "file")])?;
    validate_regular_nonempty_file(&payload.join("yolo11n.onnx"), "RoadWatcher CV model")?;
    let labels = read_bounded_file(
        &payload.join("labels.txt"),
        ROADWATCHER_CV_LABELS_MAX_BYTES,
        "RoadWatcher CV labels",
    )?;
    let labels = std::str::from_utf8(&labels)
        .map_err(|_| "RoadWatcher CV labels are not UTF-8".to_string())?;
    if !labels.ends_with('\n') {
        return Err("RoadWatcher CV labels must end with a newline".to_string());
    }
    let mut known = HashSet::new();
    let mut count = 0_usize;
    for label in labels.lines() {
        validate_bounded_text(label, "RoadWatcher CV label", 200)?;
        if !known.insert(label.to_string()) {
            return Err("RoadWatcher CV labels contain duplicates".to_string());
        }
        count += 1;
        if count > 10_000 {
            return Err("RoadWatcher CV labels exceed their count limit".to_string());
        }
    }
    if count == 0 {
        return Err("RoadWatcher CV labels are empty".to_string());
    }
    Ok(())
}

fn validate_york_valhalla_payload(
    payload: &Path,
    cancelled: impl Fn() -> bool + Copy,
) -> Result<(), String> {
    validate_exact_payload_root(
        payload,
        &[("valhalla.json", "file"), ("tiles", "directory")],
    )?;
    let config = read_bounded_file(
        &payload.join("valhalla.json"),
        ROADWATCHER_VALHALLA_CONFIG_MAX_BYTES,
        "RoadWatcher Valhalla configuration",
    )?;
    let config: serde_json::Value = serde_json::from_slice(&config)
        .map_err(|error| format!("RoadWatcher Valhalla configuration is invalid JSON: {error}"))?;
    validate_portable_config(&config)?;
    let mut file_count = 0_usize;
    let mut total_bytes = 0_u64;
    validate_valhalla_tile_tree(
        &payload.join("tiles"),
        0,
        &mut file_count,
        &mut total_bytes,
        cancelled,
    )?;
    if file_count == 0 {
        return Err("RoadWatcher Valhalla tiles contain no tile files".to_string());
    }
    Ok(())
}

fn validate_exact_payload_root(payload: &Path, expected: &[(&str, &str)]) -> Result<(), String> {
    let mut entries = HashMap::new();
    for entry in
        fs::read_dir(payload).map_err(|error| format!("could not inspect payload: {error}"))?
    {
        let entry = entry.map_err(|error| format!("could not inspect payload entry: {error}"))?;
        let name = entry.file_name().to_string_lossy().to_string();
        let fold = name.to_ascii_lowercase();
        if entries
            .insert(
                fold,
                (
                    name,
                    entry.path(),
                    entry.file_type().map_err(|error| error.to_string())?,
                ),
            )
            .is_some()
        {
            return Err("RoadWatcher release payload contains a case-colliding entry".to_string());
        }
    }
    if entries.len() != expected.len() {
        return Err("RoadWatcher release payload has unexpected root entries".to_string());
    }
    for (name, kind) in expected {
        let (actual_name, path, file_type) = entries
            .get(&name.to_ascii_lowercase())
            .ok_or_else(|| format!("RoadWatcher release payload is missing {name}"))?;
        if actual_name != name
            || file_type.is_symlink()
            || !matches!(
                (*kind, file_type.is_file(), file_type.is_dir()),
                ("file", true, _) | ("directory", _, true)
            )
        {
            return Err(format!(
                "RoadWatcher release payload entry {name} has the wrong type"
            ));
        }
        if *kind == "file" {
            validate_regular_nonempty_file(path, "RoadWatcher release payload file")?;
        }
    }
    Ok(())
}

fn validate_regular_nonempty_file(path: &Path, label: &str) -> Result<(), String> {
    let metadata = fs::symlink_metadata(path)
        .map_err(|error| format!("could not inspect {label}: {error}"))?;
    if metadata.file_type().is_symlink() || !metadata.file_type().is_file() || metadata.len() == 0 {
        return Err(format!("{label} is not a non-empty regular file"));
    }
    Ok(())
}

fn validate_valhalla_tile_tree(
    root: &Path,
    depth: usize,
    file_count: &mut usize,
    total_bytes: &mut u64,
    cancelled: impl Fn() -> bool + Copy,
) -> Result<(), String> {
    if depth > 16 {
        return Err("RoadWatcher Valhalla tiles exceed their directory-depth limit".to_string());
    }
    for entry in
        fs::read_dir(root).map_err(|error| format!("could not inspect Valhalla tiles: {error}"))?
    {
        if cancelled() {
            return Err("dependency installation cancelled".to_string());
        }
        let entry =
            entry.map_err(|error| format!("could not inspect Valhalla tile entry: {error}"))?;
        let path = entry.path();
        let metadata = fs::symlink_metadata(&path)
            .map_err(|error| format!("could not inspect Valhalla tile entry: {error}"))?;
        if metadata.file_type().is_symlink() {
            return Err("RoadWatcher Valhalla tiles contain a link".to_string());
        }
        if metadata.file_type().is_dir() {
            validate_valhalla_tile_tree(&path, depth + 1, file_count, total_bytes, cancelled)?;
            continue;
        }
        if !metadata.file_type().is_file()
            || metadata.len() == 0
            || path.extension().and_then(|value| value.to_str()) != Some("gph")
        {
            return Err("RoadWatcher Valhalla tiles contain an invalid tile file".to_string());
        }
        *file_count = file_count
            .checked_add(1)
            .ok_or_else(|| "RoadWatcher Valhalla tile count overflow".to_string())?;
        *total_bytes = total_bytes
            .checked_add(metadata.len())
            .ok_or_else(|| "RoadWatcher Valhalla tile size overflow".to_string())?;
        if *file_count > ROADWATCHER_VALHALLA_TILE_MAX_FILES
            || *total_bytes > ROADWATCHER_VALHALLA_TILE_MAX_BYTES
        {
            return Err(
                "RoadWatcher Valhalla tiles exceed their bounded payload limit".to_string(),
            );
        }
    }
    Ok(())
}

fn validate_release_license(
    license: &RoadWatcherReleaseLicense,
    label: &str,
) -> Result<(), String> {
    if !valid_component_id(&license.id) {
        return Err(format!("{label} ID is invalid"));
    }
    validate_bounded_text(&license.name, label, 200)?;
    validate_https_url(&license.url)
}

fn validate_iso_utc(value: &str, label: &str) -> Result<(), String> {
    if value.len() > 64 || !value.ends_with('Z') {
        return Err(format!("{label} is not an ISO UTC timestamp"));
    }
    chrono::DateTime::parse_from_rfc3339(value)
        .map_err(|_| format!("{label} is not an ISO UTC timestamp"))?;
    Ok(())
}

fn validate_bounded_text(value: &str, label: &str, max_length: usize) -> Result<(), String> {
    if value.trim().is_empty() || value.len() > max_length || value.contains(['\r', '\n', '\0']) {
        return Err(format!("{label} is invalid"));
    }
    Ok(())
}

fn valid_component_id(value: &str) -> bool {
    !value.is_empty()
        && value.chars().all(|character| {
            character.is_ascii_lowercase() || character.is_ascii_digit() || character == '-'
        })
}

fn valid_build_parameter_name(value: &str) -> bool {
    let mut characters = value.chars();
    matches!(characters.next(), Some(character) if character.is_ascii_alphabetic())
        && characters.all(|character| character.is_ascii_alphanumeric())
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
        rename_directory_with_transient_retry(target, &backup)
            .map_err(|error| format!("could not preserve previous managed component: {error}"))?;
    }
    if let Err(error) = rename_directory_with_transient_retry(staging_payload, target) {
        if had_target {
            let _ = rename_directory_with_transient_retry(&backup, target);
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

fn rename_directory_with_transient_retry(source: &Path, target: &Path) -> io::Result<()> {
    const RETRIES: usize = 100;
    for attempt in 0..=RETRIES {
        match fs::rename(source, target) {
            Ok(()) => return Ok(()),
            Err(error) if attempt < RETRIES && transient_windows_rename_error(&error) => {
                thread::sleep(Duration::from_millis(50));
            }
            Err(error) => return Err(error),
        }
    }
    unreachable!("bounded rename retry loop always returns")
}

fn transient_windows_rename_error(error: &io::Error) -> bool {
    cfg!(windows) && matches!(error.raw_os_error(), Some(5 | 32 | 33))
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
    use std::cell::Cell;
    use std::collections::VecDeque;
    use std::io::{Cursor, ErrorKind};
    use zip::write::SimpleFileOptions;
    use zip::ZipWriter;

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
                    size_bytes: None,
                    archive: "zip".to_string(),
                    file_name: None,
                }),
                artifact_manifest: None,
                bootstrap: None,
                install_strategy: None,
                dependencies: vec![],
                references: vec![],
                project_imports: vec![],
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
        let mut unsafe_reference = catalog();
        unsafe_reference.components[0].references = vec![DependencyReference {
            id: "tool".to_string(),
            path: "../tool.exe".to_string(),
            kind: "file".to_string(),
        }];
        assert!(validate_catalog(&unsafe_reference)
            .unwrap_err()
            .contains("reference path"));

        let mut disallowed_host = catalog();
        disallowed_host.components[0].artifact.as_mut().unwrap().url =
            "https://example.com/tool.zip".to_string();
        assert!(validate_catalog(&disallowed_host)
            .unwrap_err()
            .contains("not allowlisted"));

        let mut invalid_hash = catalog();
        invalid_hash.components[0].artifact.as_mut().unwrap().sha256 = "not-a-hash".to_string();
        assert!(validate_catalog(&invalid_hash)
            .unwrap_err()
            .contains("SHA-256"));

        let mut unsupported_archive = catalog();
        unsupported_archive.components[0]
            .artifact
            .as_mut()
            .unwrap()
            .archive = "tar".to_string();
        assert!(validate_catalog(&unsupported_archive)
            .unwrap_err()
            .contains("unsupported"));

        let mut inconsistent_license = catalog();
        let mut second = inconsistent_license.components[0].clone();
        second.id = "tool-two".to_string();
        second.license.digest = "different-digest".to_string();
        inconsistent_license.components.push(second);
        assert!(validate_catalog(&inconsistent_license)
            .unwrap_err()
            .contains("inconsistent digests"));
    }

    fn uv_bootstrap_catalog() -> DependencyCatalog {
        let mut value = catalog();
        let component = &mut value.components[0];
        component.id = "uv-python".to_string();
        component.references = vec![
            DependencyReference {
                id: "executable".to_string(),
                path: "uv.exe".to_string(),
                kind: "file".to_string(),
            },
            DependencyReference {
                id: "python-installations".to_string(),
                path: "python-installations".to_string(),
                kind: "directory".to_string(),
            },
        ];
        component.bootstrap = Some(DependencyBootstrap {
            kind: "uv-managed-python".to_string(),
            version: "3.12.13".to_string(),
            source_url: "https://releases.astral.sh/python-build-standalone".to_string(),
            license: DependencyLicense {
                id: "psf-2.0".to_string(),
                label: "PSF-2.0".to_string(),
                url: "https://docs.python.org/3.12/license.html".to_string(),
                digest: "python-license-digest".to_string(),
                consent_required: true,
            },
            artifact: Some(DependencyArtifact {
                url: "https://releases.astral.sh/python.tar.gz".to_string(),
                sha256: "b".repeat(64),
                max_bytes: 22_000_000,
                size_bytes: None,
                archive: "file".to_string(),
                file_name: Some("20260610/python.tar.gz".to_string()),
            }),
        });
        value
    }

    #[test]
    fn validates_only_the_fixed_catalog_pinned_uv_python_bootstrap() {
        assert!(validate_catalog(&uv_bootstrap_catalog()).is_ok());

        let mut arbitrary_kind = uv_bootstrap_catalog();
        arbitrary_kind.components[0]
            .bootstrap
            .as_mut()
            .unwrap()
            .kind = "command".to_string();
        assert!(validate_catalog(&arbitrary_kind)
            .unwrap_err()
            .contains("unsupported"));

        let mut missing_artifact = uv_bootstrap_catalog();
        missing_artifact.components[0]
            .bootstrap
            .as_mut()
            .unwrap()
            .artifact = None;
        assert!(validate_catalog(&missing_artifact)
            .unwrap_err()
            .contains("without a bootstrap artifact"));

        let mut unsafe_mirror = uv_bootstrap_catalog();
        unsafe_mirror.components[0]
            .bootstrap
            .as_mut()
            .unwrap()
            .artifact
            .as_mut()
            .unwrap()
            .file_name = Some("../python.tar.gz".to_string());
        assert!(validate_catalog(&unsafe_mirror)
            .unwrap_err()
            .contains("mirror path is unsafe"));

        let mut missing_reference = uv_bootstrap_catalog();
        missing_reference.components[0].references.pop();
        assert!(validate_catalog(&missing_reference)
            .unwrap_err()
            .contains("fixed backend contract"));
    }

    #[test]
    fn uv_python_command_is_app_local_registry_free_and_argument_locked() {
        let command = uv_managed_python_install_command(
            Path::new("C:/managed/uv.exe"),
            "file:///C:/staging/python-mirror/",
            Path::new("C:/managed/python-installations"),
            Path::new("C:/staging/uv-cache"),
            "3.12.13",
        );
        let args = command
            .get_args()
            .map(|value| value.to_string_lossy().to_string())
            .collect::<Vec<_>>();
        assert_eq!(
            args,
            vec![
                "python",
                "install",
                "--install-dir",
                "C:/managed/python-installations",
                "--no-bin",
                "--no-registry",
                "--managed-python",
                "--no-progress",
                "--offline",
                "--no-config",
                "--mirror",
                "file:///C:/staging/python-mirror/",
                "3.12.13",
            ]
        );
        let environment = command
            .get_envs()
            .map(|(key, value)| {
                (
                    key.to_string_lossy().to_string(),
                    value.map(|item| item.to_string_lossy().to_string()),
                )
            })
            .collect::<HashMap<_, _>>();
        assert_eq!(
            environment.get("UV_PYTHON_INSTALL_BIN"),
            Some(&Some("0".to_string()))
        );
        assert_eq!(
            environment.get("UV_PYTHON_INSTALL_REGISTRY"),
            Some(&Some("0".to_string()))
        );
    }

    fn valhalla_wheel_catalog() -> DependencyCatalog {
        let mut value = uv_bootstrap_catalog();
        let mut base = catalog();
        let mut component = base.components.remove(0);
        component.id = "managed-valhalla".to_string();
        component.label = "Managed Valhalla 3.7.0".to_string();
        component.version = "3.7.0".to_string();
        component.artifact = Some(DependencyArtifact {
            url: "https://files.pythonhosted.org/packages/49/bd/pyvalhalla.whl".to_string(),
            sha256: "e".repeat(64),
            max_bytes: 24_298_623,
            size_bytes: None,
            archive: "file".to_string(),
            file_name: Some("pyvalhalla-3.7.0-cp312-abi3-win_amd64.whl".to_string()),
        });
        component.install_strategy = Some(DependencyInstallStrategy {
            kind: "uv-wheel-environment".to_string(),
            python_version: "3.12.13".to_string(),
            package: "pyvalhalla".to_string(),
            package_version: "3.7.0".to_string(),
            wheel_file_name: "pyvalhalla-3.7.0-cp312-abi3-win_amd64.whl".to_string(),
        });
        component.dependencies = vec!["uv-python".to_string()];
        component.references = vec![DependencyReference {
            id: "service-executable".to_string(),
            path: "Scripts/valhalla_service.exe".to_string(),
            kind: "file".to_string(),
        }];
        value.components.push(component);
        value
    }

    #[test]
    fn validates_only_the_fixed_pyvalhalla_wheel_environment() {
        assert!(validate_catalog(&valhalla_wheel_catalog()).is_ok());

        let mut arbitrary_package = valhalla_wheel_catalog();
        arbitrary_package.components[1]
            .install_strategy
            .as_mut()
            .unwrap()
            .package = "arbitrary".to_string();
        assert!(validate_catalog(&arbitrary_package)
            .unwrap_err()
            .contains("fixed backend contract"));

        let mut arbitrary_reference = valhalla_wheel_catalog();
        arbitrary_reference.components[1].references[0].path = "service.exe".to_string();
        assert!(validate_catalog(&arbitrary_reference)
            .unwrap_err()
            .contains("reference drifted"));
    }

    #[test]
    fn pyvalhalla_commands_are_offline_managed_and_argument_locked() {
        let venv = uv_wheel_venv_command(
            Path::new("C:/managed/uv.exe"),
            Path::new("C:/managed/python-installations"),
            Path::new("C:/staging/payload"),
            Path::new("C:/staging/cache"),
            "3.12.13",
        );
        let venv_args = venv
            .get_args()
            .map(|value| value.to_string_lossy().to_string())
            .collect::<Vec<_>>();
        assert_eq!(
            venv_args,
            vec![
                "venv",
                "--allow-existing",
                "--no-project",
                "--python",
                "3.12.13",
                "--managed-python",
                "--no-python-downloads",
                "--link-mode",
                "copy",
                "--offline",
                "--no-config",
                "--no-progress",
                "C:/staging/payload",
            ]
        );
        let install = uv_wheel_install_command(
            Path::new("C:/managed/uv.exe"),
            Path::new("C:/staging/payload/Scripts/python.exe"),
            Path::new("C:/staging/pyvalhalla-3.7.0-cp312-abi3-win_amd64.whl"),
            Path::new("C:/staging/cache"),
        );
        let install_args = install
            .get_args()
            .map(|value| value.to_string_lossy().to_string())
            .collect::<Vec<_>>();
        assert!(install_args.contains(&"--offline".to_string()));
        assert!(install_args.contains(&"--no-index".to_string()));
        assert!(install_args.contains(&"--no-deps".to_string()));
        assert!(install_args.contains(&"--no-config".to_string()));
        assert!(!install_args.iter().any(|value| value.starts_with("http")));
    }

    fn ffmpeg_catalog() -> DependencyCatalog {
        let mut value = catalog();
        let component = &mut value.components[0];
        component.id = "ffmpeg".to_string();
        component.label = "FFmpeg and ffprobe".to_string();
        component.version = "8.1.1-audited-windows-x64".to_string();
        component.license.digest =
            "c31bd2401e4b09ced92dc957006d20997edbd7211301d9ca06e94802a2e11b50".to_string();
        component.artifact = Some(DependencyArtifact {
            url: "https://github.com/GyanD/codexffmpeg/releases/download/8.1.1/ffmpeg-8.1.1-full_build.zip".to_string(),
            sha256: "49b28c5f16addd40239a66949973458769b7056fb7752c30ac0d53389d09a552".to_string(),
            max_bytes: 252_194_496,
            size_bytes: None,
            archive: "zip".to_string(),
            file_name: Some("ffmpeg-8.1.1-full_build.zip".to_string()),
        });
        component.install_strategy = Some(DependencyInstallStrategy {
            kind: "verified-ffmpeg-archive".to_string(),
            python_version: String::new(),
            package: String::new(),
            package_version: String::new(),
            wheel_file_name: String::new(),
        });
        component.references = vec![DependencyReference {
            id: "binary-directory".to_string(),
            path: "ffmpeg-8.1.1-full_build/bin".to_string(),
            kind: "directory".to_string(),
        }];
        value
    }

    #[test]
    fn validates_only_the_fixed_ffmpeg_archive() {
        assert!(validate_catalog(&ffmpeg_catalog()).is_ok());

        let mut arbitrary_hash = ffmpeg_catalog();
        arbitrary_hash.components[0]
            .artifact
            .as_mut()
            .unwrap()
            .sha256 = "f".repeat(64);
        assert!(validate_catalog(&arbitrary_hash)
            .unwrap_err()
            .contains("fixed backend contract"));

        let mut arbitrary_reference = ffmpeg_catalog();
        arbitrary_reference.components[0].references[0].path = "bin".to_string();
        assert!(validate_catalog(&arbitrary_reference)
            .unwrap_err()
            .contains("managed reference drifted"));
    }

    fn roadwatcher_release_catalog(component_id: &str) -> DependencyCatalog {
        let mut value = catalog();
        let mut managed_valhalla_support = value.components[0].clone();
        let component = &mut value.components[0];
        let (kind, file_name, references, dependencies) = match component_id {
            "york-valhalla-tiles" => (
                "tiles",
                "york-valhalla-tiles-2026.07.13-test.1-windows-x86_64.zip",
                vec![
                    DependencyReference {
                        id: "config".to_string(),
                        path: "valhalla.json".to_string(),
                        kind: "file".to_string(),
                    },
                    DependencyReference {
                        id: "tiles".to_string(),
                        path: "tiles".to_string(),
                        kind: "directory".to_string(),
                    },
                ],
                vec!["managed-valhalla".to_string()],
            ),
            "cv-yolo11n" => (
                "model",
                "cv-yolo11n-2026.07.13-test.1-windows-x86_64.zip",
                vec![
                    DependencyReference {
                        id: "model".to_string(),
                        path: "yolo11n.onnx".to_string(),
                        kind: "file".to_string(),
                    },
                    DependencyReference {
                        id: "labels".to_string(),
                        path: "labels.txt".to_string(),
                        kind: "file".to_string(),
                    },
                ],
                vec![],
            ),
            other => panic!("unsupported fixture component {other}"),
        };
        let tag = "internal-test";
        let manifest_name = file_name
            .strip_suffix(".zip")
            .map(|stem| format!("{stem}.manifest.json"))
            .unwrap();
        component.id = component_id.to_string();
        component.label = format!("Test {component_id}");
        component.version = "2026.07.13-test.1".to_string();
        component.source_url = "https://github.com/Remi-Z/RoadWatcher/releases".to_string();
        component.license = DependencyLicense {
            id: "agpl-3-0".to_string(),
            label: "AGPL-3.0".to_string(),
            url: "https://example.com/approved-license".to_string(),
            digest: "approved-license-digest".to_string(),
            consent_required: true,
        };
        component.artifact = Some(DependencyArtifact {
            url: format!(
                "https://github.com/Remi-Z/RoadWatcher/releases/download/{tag}/{file_name}"
            ),
            sha256: "d".repeat(64),
            max_bytes: 100_000,
            size_bytes: Some(64),
            archive: "zip".to_string(),
            file_name: Some(file_name.to_string()),
        });
        component.artifact_manifest = Some(DependencyArtifactManifestDescriptor {
            url: format!(
                "https://github.com/Remi-Z/RoadWatcher/releases/download/{tag}/{manifest_name}"
            ),
            sha256: "e".repeat(64),
            max_bytes: 100_000,
            kind: kind.to_string(),
        });
        component.install_strategy = Some(DependencyInstallStrategy {
            kind: "roadwatcher-release-archive".to_string(),
            python_version: String::new(),
            package: String::new(),
            package_version: String::new(),
            wheel_file_name: String::new(),
        });
        component.bootstrap = None;
        component.dependencies = dependencies;
        component.references = references;
        component.project_imports = vec![];
        if component_id == "york-valhalla-tiles" {
            managed_valhalla_support.id = "managed-valhalla".to_string();
            managed_valhalla_support.label = "Managed Valhalla support fixture".to_string();
            managed_valhalla_support.version = "fixture".to_string();
            managed_valhalla_support.dependencies = vec![];
            managed_valhalla_support.references = vec![];
            managed_valhalla_support.install_strategy = None;
            managed_valhalla_support.artifact_manifest = None;
            value.components.push(managed_valhalla_support);
        }
        value
    }

    #[test]
    fn validates_only_manifest_bound_roadwatcher_release_archives() {
        validate_catalog(&roadwatcher_release_catalog("york-valhalla-tiles")).unwrap();
        assert!(validate_catalog(&roadwatcher_release_catalog("cv-yolo11n")).is_ok());

        let mut missing_descriptor = roadwatcher_release_catalog("cv-yolo11n");
        missing_descriptor.components[0].artifact_manifest = None;
        assert!(validate_catalog(&missing_descriptor)
            .unwrap_err()
            .contains("manifest descriptor"));

        let mut missing_exact_size = roadwatcher_release_catalog("cv-yolo11n");
        missing_exact_size.components[0]
            .artifact
            .as_mut()
            .unwrap()
            .size_bytes = None;
        assert!(validate_catalog(&missing_exact_size)
            .unwrap_err()
            .contains("exact verified byte size"));

        let mut wrong_release = roadwatcher_release_catalog("cv-yolo11n");
        wrong_release.components[0].artifact.as_mut().unwrap().url =
            "https://github.com/Remi-Z/RoadWatcher/releases/download/internal-test/other.zip"
                .to_string();
        assert!(validate_catalog(&wrong_release)
            .unwrap_err()
            .contains("file name"));

        let mut wrong_manifest = roadwatcher_release_catalog("cv-yolo11n");
        wrong_manifest.components[0]
            .artifact_manifest
            .as_mut()
            .unwrap()
            .kind = "tiles".to_string();
        assert!(validate_catalog(&wrong_manifest)
            .unwrap_err()
            .contains("manifest descriptor"));

        let mut wrong_reference = roadwatcher_release_catalog("york-valhalla-tiles");
        wrong_reference.components[0].references[1].path = "other".to_string();
        assert!(validate_catalog(&wrong_reference)
            .unwrap_err()
            .contains("reference drifted"));

        let mut arbitrary_component = roadwatcher_release_catalog("cv-yolo11n");
        arbitrary_component.components[0].id = "arbitrary".to_string();
        assert!(validate_catalog(&arbitrary_component)
            .unwrap_err()
            .contains("not an approved"));
    }

    #[test]
    fn verifies_exact_roadwatcher_release_manifest_identity() {
        let root = std::env::temp_dir().join(format!(
            "roadwatcher-release-manifest-test-{}",
            Uuid::new_v4()
        ));
        fs::create_dir_all(&root).unwrap();
        let mut catalog = roadwatcher_release_catalog("cv-yolo11n");
        let archive_name = catalog.components[0]
            .artifact
            .as_ref()
            .unwrap()
            .file_name
            .clone()
            .unwrap();
        let archive_path = root.join(archive_name);
        fs::write(
            &archive_path,
            b"RoadWatcher generated release archive fixture",
        )
        .unwrap();
        {
            let artifact = catalog.components[0].artifact.as_mut().unwrap();
            artifact.size_bytes = Some(fs::metadata(&archive_path).unwrap().len());
            artifact.sha256 = hex::encode(Sha256::digest(fs::read(&archive_path).unwrap()));
        }
        let manifest_path = root.join("artifact.manifest.json");
        let manifest = release_manifest_fixture(
            &catalog.components[0],
            catalog.components[0].artifact.as_ref().unwrap(),
        );
        fs::write(
            &manifest_path,
            serde_json::to_vec_pretty(&manifest).unwrap(),
        )
        .unwrap();
        let component = &catalog.components[0];
        let artifact = component.artifact.as_ref().unwrap();
        let descriptor = component.artifact_manifest.as_ref().unwrap();
        verify_roadwatcher_release_manifest(
            component,
            artifact,
            descriptor,
            &archive_path,
            &manifest_path,
        )
        .unwrap();

        let mut wrong_id = manifest.clone();
        wrong_id["id"] = serde_json::Value::String("other".to_string());
        fs::write(
            &manifest_path,
            serde_json::to_vec_pretty(&wrong_id).unwrap(),
        )
        .unwrap();
        assert!(verify_roadwatcher_release_manifest(
            component,
            artifact,
            descriptor,
            &archive_path,
            &manifest_path,
        )
        .unwrap_err()
        .contains("identity"));

        let mut wrong_license = manifest.clone();
        wrong_license["artifact"]["license"]["id"] =
            serde_json::Value::String("different-license".to_string());
        fs::write(
            &manifest_path,
            serde_json::to_vec_pretty(&wrong_license).unwrap(),
        )
        .unwrap();
        assert!(verify_roadwatcher_release_manifest(
            component,
            artifact,
            descriptor,
            &archive_path,
            &manifest_path,
        )
        .unwrap_err()
        .contains("artifact identity"));

        let mut malformed_source = manifest.clone();
        malformed_source["sources"][0]["etag"] =
            serde_json::Value::String("unsafe\nETag".to_string());
        fs::write(
            &manifest_path,
            serde_json::to_vec_pretty(&malformed_source).unwrap(),
        )
        .unwrap();
        assert!(verify_roadwatcher_release_manifest(
            component,
            artifact,
            descriptor,
            &archive_path,
            &manifest_path,
        )
        .unwrap_err()
        .contains("ETag"));

        let mut unsafe_shape = manifest;
        unsafe_shape["unexpected"] = serde_json::Value::Bool(true);
        fs::write(
            &manifest_path,
            serde_json::to_vec_pretty(&unsafe_shape).unwrap(),
        )
        .unwrap();
        assert!(verify_roadwatcher_release_manifest(
            component,
            artifact,
            descriptor,
            &archive_path,
            &manifest_path,
        )
        .unwrap_err()
        .contains("invalid JSON"));
        fs::remove_dir_all(root).unwrap();
    }

    #[test]
    fn validates_release_payload_layouts_and_bounded_extraction() {
        let root = std::env::temp_dir().join(format!(
            "roadwatcher-release-payload-test-{}",
            Uuid::new_v4()
        ));
        fs::create_dir_all(&root).unwrap();
        let cv_payload = root.join("cv");
        fs::create_dir_all(&cv_payload).unwrap();
        fs::write(cv_payload.join("yolo11n.onnx"), b"fixture-model").unwrap();
        fs::write(cv_payload.join("labels.txt"), b"car\nbicycle\n").unwrap();
        validate_roadwatcher_release_payload(
            &roadwatcher_release_catalog("cv-yolo11n").components[0],
            &cv_payload,
            || false,
        )
        .unwrap();
        fs::write(cv_payload.join("unexpected.txt"), b"nope").unwrap();
        assert!(validate_roadwatcher_release_payload(
            &roadwatcher_release_catalog("cv-yolo11n").components[0],
            &cv_payload,
            || false,
        )
        .unwrap_err()
        .contains("unexpected root"));
        fs::remove_file(cv_payload.join("unexpected.txt")).unwrap();

        let york_payload = root.join("york");
        fs::create_dir_all(york_payload.join("tiles/0/000")).unwrap();
        fs::write(
            york_payload.join("valhalla.json"),
            br#"{"mjolnir":{"tile_dir":"${ROADWATCHER_TILE_DIR}"}}"#,
        )
        .unwrap();
        fs::write(york_payload.join("tiles/0/000/000.gph"), b"tile").unwrap();
        validate_roadwatcher_release_payload(
            &roadwatcher_release_catalog("york-valhalla-tiles").components[0],
            &york_payload,
            || false,
        )
        .unwrap();
        fs::write(
            york_payload.join("valhalla.json"),
            br#"{"mjolnir":{"tile_dir":"${ROADWATCHER_TILE_DIR}","tile_extract":"elsewhere"}}"#,
        )
        .unwrap();
        assert!(validate_roadwatcher_release_payload(
            &roadwatcher_release_catalog("york-valhalla-tiles").components[0],
            &york_payload,
            || false,
        )
        .unwrap_err()
        .contains("tile_extract"));

        let archive_path = root.join("bounded.zip");
        let archive = File::create(&archive_path).unwrap();
        let mut writer = ZipWriter::new(archive);
        writer
            .start_file("one.txt", SimpleFileOptions::default())
            .unwrap();
        writer.write_all(b"12345").unwrap();
        writer
            .start_file("two.txt", SimpleFileOptions::default())
            .unwrap();
        writer.write_all(b"67890").unwrap();
        writer.finish().unwrap();
        let extraction = root.join("extracted");
        fs::create_dir_all(&extraction).unwrap();
        assert!(
            extract_zip_bounded(&archive_path, &extraction, 1, 100, || false)
                .unwrap_err()
                .contains("entry-count")
        );
        assert!(
            extract_zip_bounded(&archive_path, &extraction, 10, 4, || false)
                .unwrap_err()
                .contains("unpacked-size")
        );
        fs::remove_dir_all(root).unwrap();
    }

    fn release_manifest_fixture(
        component: &DependencyComponent,
        artifact: &DependencyArtifact,
    ) -> serde_json::Value {
        serde_json::json!({
            "schemaVersion": 1,
            "id": component.id,
            "kind": component.artifact_manifest.as_ref().unwrap().kind,
            "version": component.version,
            "platform": "windows-x86_64",
            "generatedAt": "2026-07-13T12:00:00Z",
            "artifact": {
                "fileName": artifact.file_name,
                "sizeBytes": artifact.size_bytes,
                "sha256": artifact.sha256,
                "license": {
                    "id": component.license.id,
                    "name": "Approved test license",
                    "url": component.license.url
                }
            },
            "sources": [{
                "id": "approved-source",
                "url": "https://example.com/source",
                "version": "v1",
                "license": {
                    "id": "approved-source-license",
                    "name": "Approved source license",
                    "url": "https://example.com/source-license"
                },
                "downloadedSha256": "a".repeat(64),
                "publisherSha256": "b".repeat(64),
                "retrievedAt": "2026-07-13T12:00:00Z",
                "sizeBytes": 1
            }],
            "tools": [{ "name": "fixture-builder", "version": "1" }],
            "build": {
                "recipe": "scripts/fixture.py",
                "recipeVersion": "1",
                "parameters": { "bufferKm": 10 }
            }
        })
    }

    #[test]
    #[ignore = "requires explicitly supplied approved uv and CPython archives"]
    fn real_approved_uv_python_bootstrap_smoke() {
        let uv_archive = PathBuf::from(
            std::env::var("ROADWATCHER_TEST_UV_ARCHIVE")
                .expect("ROADWATCHER_TEST_UV_ARCHIVE is required"),
        );
        let python_archive = PathBuf::from(
            std::env::var("ROADWATCHER_TEST_PYTHON_ARCHIVE")
                .expect("ROADWATCHER_TEST_PYTHON_ARCHIVE is required"),
        );
        assert_eq!(
            hex::encode(Sha256::digest(fs::read(&uv_archive).unwrap())),
            "02ad29f07e674d68726ba3bb1ff25b335d83515756e2b1a194bb56c3cc30e07c"
        );
        assert_eq!(
            hex::encode(Sha256::digest(fs::read(&python_archive).unwrap())),
            "99dce0b23bf3c3b28d350cdd7bfe3cd3be51cc4f285faae7c0df110d106d1a8d"
        );
        let root = std::env::temp_dir().join(format!(
            "roadwatcher-real-uv-python-bootstrap-{}",
            Uuid::new_v4()
        ));
        let payload = root.join("payload");
        let mirror = root.join("python-mirror");
        let mirror_release = mirror.join("20260610");
        fs::create_dir_all(&payload).unwrap();
        fs::create_dir_all(&mirror_release).unwrap();
        extract_zip(&uv_archive, &payload, || false).unwrap();
        fs::copy(
            &python_archive,
            mirror_release.join(
                "cpython-3.12.13+20260610-x86_64-pc-windows-msvc-install_only_stripped.tar.gz",
            ),
        )
        .unwrap();
        install_uv_managed_python(&payload, &mirror, "3.12.13", || false).unwrap();
        let python_root = payload.join("python-installations");
        assert!(find_named_file(&python_root, "python.exe", 5).is_some());
        fs::remove_dir_all(root).unwrap();
    }

    #[test]
    #[ignore = "downloads the explicitly approved uv, CPython, and pyvalhalla artifacts"]
    fn real_managed_uv_python_install_and_remove_smoke() {
        assert_eq!(
            std::env::var("ROADWATCHER_RUN_REAL_MANAGED_UV").as_deref(),
            Ok("1"),
            "set ROADWATCHER_RUN_REAL_MANAGED_UV=1 to acknowledge the networked smoke"
        );
        let app_data = std::env::temp_dir().join(format!(
            "roadwatcher-real-managed-uv-python-{}",
            Uuid::new_v4()
        ));
        let catalog_path = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
            .join("resources")
            .join("dependency-catalog.json");
        let manager = DependencyManager::load(&catalog_path, &app_data).unwrap();
        let uv_component = manager
            .catalog
            .components
            .iter()
            .find(|component| component.id == "uv-python")
            .unwrap();
        let valhalla_component = manager
            .catalog
            .components
            .iter()
            .find(|component| component.id == "managed-valhalla")
            .unwrap();
        let digests = vec![
            uv_component.license.digest.clone(),
            uv_component
                .bootstrap
                .as_ref()
                .unwrap()
                .license
                .digest
                .clone(),
            valhalla_component.license.digest.clone(),
        ];
        let job = manager
            .start(
                vec!["uv-python".to_string(), "managed-valhalla".to_string()],
                digests,
            )
            .unwrap();
        let deadline = std::time::Instant::now() + Duration::from_secs(7 * 60);
        let terminal = loop {
            let status = manager.status(&job.job_id).unwrap();
            if matches!(status.status.as_str(), "ready" | "failed" | "cancelled") {
                break status;
            }
            assert!(
                std::time::Instant::now() < deadline,
                "managed uv/Python/Valhalla install timed out"
            );
            thread::sleep(Duration::from_millis(100));
        };
        assert_eq!(terminal.status, "ready", "{}", terminal.detail);
        let ready = manager
            .catalog()
            .components
            .into_iter()
            .find(|component| component.component.id == "uv-python")
            .unwrap();
        assert_eq!(ready.state, "ready");
        assert!(ready.managed_references["executable"].ends_with("uv.exe"));
        assert!(Path::new(&ready.managed_references["python-installations"]).is_dir());
        assert_eq!(
            manager
                .managed_identity("uv-python")
                .unwrap()
                .artifact_sha256,
            "02ad29f07e674d68726ba3bb1ff25b335d83515756e2b1a194bb56c3cc30e07c"
        );
        let valhalla = manager
            .catalog()
            .components
            .into_iter()
            .find(|component| component.component.id == "managed-valhalla")
            .unwrap();
        assert_eq!(valhalla.state, "ready");
        assert!(valhalla.managed_references["service-executable"].ends_with("valhalla_service.exe"));
        assert!(managed_runtime::probe_managed_environment(
            Path::new(&valhalla.install_path),
            "pyvalhalla-3.7.0"
        )
        .unwrap()
        .contains("3.7.0"));
        assert_eq!(
            manager
                .managed_identity("managed-valhalla")
                .unwrap()
                .artifact_sha256,
            "edfc7ae3dbff0ba2de7f555a8c6e2e1e736d2cd08ff1c5781026622f2ad7b4ef"
        );
        let removed = manager.remove("managed-valhalla").unwrap();
        assert_eq!(removed.state, "notInstalled");
        let removed = manager.remove("uv-python").unwrap();
        assert_eq!(removed.state, "notInstalled");
        fs::remove_dir_all(app_data).unwrap();
    }

    #[test]
    #[ignore = "downloads and validates the explicitly approved 252 MB FFmpeg archive"]
    fn real_managed_ffmpeg_install_proxy_reference_and_remove_smoke() {
        assert_eq!(
            std::env::var("ROADWATCHER_RUN_REAL_MANAGED_FFMPEG").as_deref(),
            Ok("1"),
            "set ROADWATCHER_RUN_REAL_MANAGED_FFMPEG=1 to run the approved FFmpeg smoke"
        );
        let app_data = std::env::temp_dir().join(format!(
            "roadwatcher-real-managed-ffmpeg-{}",
            Uuid::new_v4()
        ));
        let catalog_path = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
            .join("resources")
            .join("dependency-catalog.json");
        let manager = DependencyManager::load(&catalog_path, &app_data).unwrap();
        let component = manager
            .catalog
            .components
            .iter()
            .find(|component| component.id == "ffmpeg")
            .unwrap();
        let job = manager
            .start(
                vec!["ffmpeg".to_string()],
                vec![component.license.digest.clone()],
            )
            .unwrap();
        let deadline = std::time::Instant::now() + Duration::from_secs(12 * 60);
        let terminal = loop {
            let status = manager.status(&job.job_id).unwrap();
            if matches!(status.status.as_str(), "ready" | "failed" | "cancelled") {
                break status;
            }
            assert!(
                std::time::Instant::now() < deadline,
                "managed FFmpeg install timed out"
            );
            thread::sleep(Duration::from_millis(100));
        };
        assert_eq!(terminal.status, "ready", "{terminal:?}");
        let ready = manager
            .catalog()
            .components
            .into_iter()
            .find(|component| component.component.id == "ffmpeg")
            .unwrap();
        assert_eq!(ready.state, "ready");
        let binary_directory = PathBuf::from(&ready.managed_references["binary-directory"]);
        assert!(binary_directory.join("ffmpeg.exe").is_file());
        assert!(binary_directory.join("ffprobe.exe").is_file());
        assert_eq!(
            manager.managed_identity("ffmpeg").unwrap().artifact_sha256,
            "49b28c5f16addd40239a66949973458769b7056fb7752c30ac0d53389d09a552"
        );
        let removed = manager.remove("ffmpeg").unwrap();
        assert_eq!(removed.state, "notInstalled");
        fs::remove_dir_all(app_data).unwrap();
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

        let bootstrap_root = std::env::temp_dir().join(format!(
            "roadwatcher-bootstrap-consent-test-{}",
            Uuid::new_v4()
        ));
        let bootstrap_manager =
            DependencyManager::from_catalog(uv_bootstrap_catalog(), bootstrap_root).unwrap();
        assert!(bootstrap_manager
            .start(vec!["uv-python".to_string()], vec!["digest".to_string()])
            .unwrap_err()
            .contains("3.12.13"));
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
    fn publishes_only_valid_backend_resolved_component_references() {
        let root =
            std::env::temp_dir().join(format!("roadwatcher-reference-test-{}", Uuid::new_v4()));
        let mut configured_catalog = catalog();
        configured_catalog.components[0].references = vec![DependencyReference {
            id: "executable".to_string(),
            path: "bin/tool.exe".to_string(),
            kind: "file".to_string(),
        }];
        configured_catalog.components[0].project_imports = vec![DependencyProjectImport {
            id: "signals".to_string(),
            label: "Traffic signals".to_string(),
            path: "data/signals.gpkg".to_string(),
            source_crs: "EPSG:4326".to_string(),
            layer_name: "signals".to_string(),
            layer_kind: "traffic_light".to_string(),
        }];
        let manager = DependencyManager::from_catalog(configured_catalog, root.clone()).unwrap();
        let target = root.join("tool").join("1");
        fs::create_dir_all(target.join("bin")).unwrap();
        fs::write(target.join("bin/tool.exe"), b"fixture").unwrap();
        fs::create_dir_all(target.join("data")).unwrap();
        fs::write(target.join("data/signals.gpkg"), b"fixture").unwrap();
        fs::write(
            target.join(MANAGED_MARKER),
            serde_json::to_vec(&ManagedComponentMarker {
                id: "tool".to_string(),
                version: "1".to_string(),
                artifact_sha256: "a".repeat(64),
                bootstrap_artifact_sha256: None,
                artifact_manifest_sha256: None,
                source_url: "https://github.com/example/tool.zip".to_string(),
                installed_at_unix: 1,
            })
            .unwrap(),
        )
        .unwrap();

        let ready = manager.catalog().components.remove(0);
        assert_eq!(ready.state, "ready");
        assert!(ready
            .managed_references
            .get("executable")
            .is_some_and(|path| path.ends_with("tool.exe")));
        assert!(manager
            .managed_reference("tool", "executable")
            .is_some_and(|path| path.ends_with("tool.exe")));
        assert!(manager.managed_reference("tool", "undeclared").is_none());
        assert_eq!(ready.managed_project_imports.len(), 1);
        assert!(ready.managed_project_imports[0]
            .source_path
            .ends_with("signals.gpkg"));

        fs::remove_file(target.join("bin/tool.exe")).unwrap();
        let invalid = manager.catalog().components.remove(0);
        assert_eq!(invalid.state, "invalid");
        assert!(invalid.managed_references.is_empty());
        assert!(manager.managed_reference("tool", "executable").is_none());
        fs::remove_dir_all(root).unwrap();
    }

    #[test]
    fn relative_paths_reject_traversal_and_roots() {
        assert!(validate_relative_path(Path::new("bin/tool.exe")).is_ok());
        assert!(validate_relative_path(Path::new("../tool.exe")).is_err());
        assert!(validate_relative_path(Path::new("C:/tool.exe")).is_err());
    }

    #[test]
    fn redirects_remain_https_and_host_allowlisted() {
        let source = Url::parse("https://github.com/example/tool.zip").unwrap();
        assert_eq!(
            resolve_allowed_redirect(&source, "/example/release/tool.zip")
                .unwrap()
                .host_str(),
            Some("github.com")
        );
        assert_eq!(
            resolve_allowed_redirect(
                &source,
                "https://release-assets.githubusercontent.com/example/tool.zip",
            )
            .unwrap()
            .host_str(),
            Some("release-assets.githubusercontent.com")
        );
        assert!(
            resolve_allowed_redirect(&source, "https://example.com/tool.zip")
                .unwrap_err()
                .contains("not allowlisted")
        );
        assert!(
            resolve_allowed_redirect(&source, "http://github.com/tool.zip")
                .unwrap_err()
                .contains("unsafe")
        );
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

    #[test]
    fn rejects_download_hash_size_and_cancellation_failures() {
        let payload = b"abcdefghij";

        let hash_destination = temporary_download_path("hash-mismatch");
        let hash_client = FakeDownloadClient::new(vec![Ok(download_response(
            200,
            None,
            false,
            None,
            Box::new(Cursor::new(payload.to_vec())),
        ))]);
        assert!(download_verified_with_client(
            &hash_client,
            "https://github.com/example/tool.zip",
            &hash_destination,
            100,
            &"0".repeat(64),
            || false,
        )
        .unwrap_err()
        .contains("SHA-256 mismatch"));
        fs::remove_file(hash_destination).unwrap();

        let size_destination = temporary_download_path("size-limit");
        let size_client = FakeDownloadClient::new(vec![Ok(download_response(
            200,
            None,
            false,
            None,
            Box::new(Cursor::new(payload.to_vec())),
        ))]);
        assert!(download_verified_with_client(
            &size_client,
            "https://github.com/example/tool.zip",
            &size_destination,
            4,
            &hex::encode(Sha256::digest(payload)),
            || false,
        )
        .unwrap_err()
        .contains("size limit"));
        fs::remove_file(size_destination).unwrap();

        let cancel_destination = temporary_download_path("cancel");
        let cancel_client = FakeDownloadClient::new(vec![Ok(download_response(
            200,
            None,
            false,
            None,
            Box::new(Cursor::new(payload.to_vec())),
        ))]);
        let cancellation_checks = Cell::new(0_u8);
        assert!(download_verified_with_client(
            &cancel_client,
            "https://github.com/example/tool.zip",
            &cancel_destination,
            100,
            &hex::encode(Sha256::digest(payload)),
            || {
                cancellation_checks.set(cancellation_checks.get() + 1);
                cancellation_checks.get() > 1
            },
        )
        .unwrap_err()
        .contains("cancelled"));
        fs::remove_file(cancel_destination).unwrap();
    }

    #[test]
    fn extraction_rejects_traversal_entries() {
        let root = std::env::temp_dir().join(format!(
            "roadwatcher-dependency-zip-traversal-{}",
            Uuid::new_v4()
        ));
        fs::create_dir_all(&root).unwrap();
        let archive_path = root.join("unsafe.zip");
        let archive = File::create(&archive_path).unwrap();
        let mut writer = ZipWriter::new(archive);
        writer
            .start_file("../escape.txt", SimpleFileOptions::default())
            .unwrap();
        writer.write_all(b"escape").unwrap();
        writer.finish().unwrap();

        let destination = root.join("payload");
        fs::create_dir_all(&destination).unwrap();
        assert!(extract_zip(&archive_path, &destination, || false)
            .unwrap_err()
            .contains("unsafe path"));
        assert!(!root.join("escape.txt").exists());
        fs::remove_dir_all(root).unwrap();
    }

    #[test]
    fn atomic_promotion_preserves_or_restores_owned_targets() {
        let root = std::env::temp_dir().join(format!(
            "roadwatcher-dependency-promotion-{}",
            Uuid::new_v4()
        ));
        let target = root.join("tool").join("1");
        fs::create_dir_all(&target).unwrap();
        write_test_marker(&target);
        fs::write(target.join("identity.txt"), b"previous").unwrap();

        let staging = root.join("staging-payload");
        fs::create_dir_all(&staging).unwrap();
        write_test_marker(&staging);
        fs::write(staging.join("identity.txt"), b"promoted").unwrap();
        promote_owned_directory(&staging, &target, &root).unwrap();
        assert_eq!(fs::read(target.join("identity.txt")).unwrap(), b"promoted");

        let missing_staging = root.join("missing-staging");
        assert!(promote_owned_directory(&missing_staging, &target, &root)
            .unwrap_err()
            .contains("atomically"));
        assert_eq!(fs::read(target.join("identity.txt")).unwrap(), b"promoted");
        assert!(read_marker(&target).is_ok());
        fs::remove_dir_all(root).unwrap();
    }

    #[test]
    fn retries_only_known_windows_directory_lock_errors() {
        assert_eq!(
            transient_windows_rename_error(&io::Error::from_raw_os_error(5)),
            cfg!(windows)
        );
        assert_eq!(
            transient_windows_rename_error(&io::Error::from_raw_os_error(32)),
            cfg!(windows)
        );
        assert!(!transient_windows_rename_error(
            &io::Error::from_raw_os_error(3)
        ));
    }

    fn write_test_marker(target: &Path) {
        fs::write(
            target.join(MANAGED_MARKER),
            serde_json::to_vec(&ManagedComponentMarker {
                id: "tool".to_string(),
                version: "1".to_string(),
                artifact_sha256: "a".repeat(64),
                bootstrap_artifact_sha256: None,
                artifact_manifest_sha256: None,
                source_url: "https://github.com/example/tool.zip".to_string(),
                installed_at_unix: 1,
            })
            .unwrap(),
        )
        .unwrap();
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
