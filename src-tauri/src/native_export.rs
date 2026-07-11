use crate::project_store::{
    begin_export_manifest, complete_export_manifest, fail_export_manifest, ExportArtifactRecord,
    ExportManifestCompletionRequest, ExportManifestStartRequest, ProjectStoreError,
};
use serde::{Deserialize, Serialize};
use serde_json::Value;
use sha2::{Digest, Sha256};
use std::collections::BTreeSet;
use std::fs::{self, File, OpenOptions};
use std::io::{Read, Write};
use std::path::{Path, PathBuf};
use std::time::{SystemTime, UNIX_EPOCH};
use thiserror::Error;
use uuid::Uuid;

const MAX_ARTIFACT_COUNT: usize = 16;
const MAX_ARTIFACT_BYTES: usize = 16 * 1024 * 1024;
const MAX_TOTAL_BYTES: usize = 48 * 1024 * 1024;

#[derive(Clone, Debug)]
pub struct NativeExportRequest {
    pub sqlite_path: PathBuf,
    pub project_id: String,
    pub file_base_name: String,
    pub artifacts_json: String,
}

#[derive(Clone, Debug, Eq, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct NativeExportArtifactResponse {
    pub file_name: String,
    pub mime_type: String,
    pub sha256: String,
    pub byte_size: u64,
    pub path: String,
}

#[derive(Clone, Debug, Eq, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct NativeExportResponse {
    pub export_id: String,
    pub export_directory: String,
    pub manifest_path: String,
    pub artifacts: Vec<NativeExportArtifactResponse>,
}

#[derive(Debug, Error)]
pub enum NativeExportError {
    #[error("Native export request is invalid: {0}")]
    InvalidRequest(String),
    #[error("Native export filesystem operation failed: {0}")]
    Io(#[from] std::io::Error),
    #[error("Native export project store operation failed: {0}")]
    Store(#[from] ProjectStoreError),
    #[error("System clock is before the Unix epoch.")]
    InvalidSystemClock,
}

#[derive(Clone, Debug, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
struct InputArtifact {
    file_name: String,
    mime_type: String,
    content: String,
}

#[derive(Clone, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct ExportManifest<'a> {
    export_id: &'a str,
    project_id: &'a str,
    created_at_unix: i64,
    file_base_name: &'a str,
    artifacts: &'a [NativeExportArtifactResponse],
}

pub fn export_native(
    request: NativeExportRequest,
) -> Result<NativeExportResponse, NativeExportError> {
    let created_at_unix = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map_err(|_| NativeExportError::InvalidSystemClock)?
        .as_secs() as i64;
    export_native_at(request, Uuid::new_v4(), created_at_unix)
}

fn export_native_at(
    request: NativeExportRequest,
    export_id: Uuid,
    created_at_unix: i64,
) -> Result<NativeExportResponse, NativeExportError> {
    let safe_base_name = slugify(&request.file_base_name);
    if safe_base_name.is_empty() {
        return Err(NativeExportError::InvalidRequest(
            "fileBaseName must contain an ASCII letter or number".to_string(),
        ));
    }
    let artifacts: Vec<InputArtifact> = serde_json::from_str(&request.artifacts_json)
        .map_err(|error| NativeExportError::InvalidRequest(format!("artifactsJson: {error}")))?;
    validate_artifacts(&artifacts, &request.project_id)?;

    let project_directory = request.sqlite_path.parent().ok_or_else(|| {
        NativeExportError::InvalidRequest("sqlitePath has no project directory".to_string())
    })?;
    let exports_directory = project_directory.join("exports");
    validate_exports_directory(&exports_directory)?;
    let export_id_string = export_id.to_string();
    let staging_directory = exports_directory.join(format!(".staging-{export_id_string}"));
    let final_directory =
        exports_directory.join(format!("{}-{}", safe_base_name, export_id.simple()));

    begin_export_manifest(&ExportManifestStartRequest {
        sqlite_path: request.sqlite_path.clone(),
        export_id: export_id_string.clone(),
        project_id: request.project_id.clone(),
        file_base_name: safe_base_name.clone(),
        created_at_unix,
        staging_directory: path_string(&staging_directory),
    })?;

    let staged_result = stage_export(
        &artifacts,
        &staging_directory,
        &final_directory,
        &export_id_string,
        &request.project_id,
        &safe_base_name,
        created_at_unix,
    );
    let (artifact_responses, manifest_json) = match staged_result {
        Ok(result) => result,
        Err(error) => {
            let cleanup = remove_flat_staging_directory(&staging_directory);
            let detail = match cleanup {
                Ok(()) => error.to_string(),
                Err(cleanup_error) => format!("{error}; staging cleanup failed: {cleanup_error}"),
            };
            let _ = fail_export_manifest(
                &request.sqlite_path,
                &request.project_id,
                &export_id_string,
                &detail,
            );
            return Err(error);
        }
    };

    fs::rename(&staging_directory, &final_directory).map_err(|error| {
        let export_error = NativeExportError::Io(error);
        let _ = remove_flat_staging_directory(&staging_directory);
        let _ = fail_export_manifest(
            &request.sqlite_path,
            &request.project_id,
            &export_id_string,
            &export_error.to_string(),
        );
        export_error
    })?;
    if let Err(error) = sync_directory(&exports_directory) {
        let _ = fs::rename(&final_directory, &staging_directory);
        return Err(NativeExportError::Io(error));
    }

    let manifest_path = final_directory.join("manifest.json");
    let records = artifact_responses
        .iter()
        .map(|artifact| ExportArtifactRecord {
            file_name: artifact.file_name.clone(),
            mime_type: artifact.mime_type.clone(),
            sha256: artifact.sha256.clone(),
            byte_size: artifact.byte_size as i64,
            final_path: artifact.path.clone(),
        })
        .collect();
    let completion = complete_export_manifest(&ExportManifestCompletionRequest {
        sqlite_path: request.sqlite_path,
        export_id: export_id_string.clone(),
        project_id: request.project_id,
        final_directory: path_string(&final_directory),
        manifest_path: path_string(&manifest_path),
        manifest_json,
        artifacts: records,
    });
    if let Err(error) = completion {
        // Keep recoverable staging semantics when publication cannot be recorded.
        // A failed rename-back leaves the already synced final directory intact;
        // it is never reported as a successful export.
        let _ = fs::rename(&final_directory, &staging_directory);
        return Err(NativeExportError::Store(error));
    }

    Ok(NativeExportResponse {
        export_id: export_id_string,
        export_directory: path_string(&final_directory),
        manifest_path: path_string(&manifest_path),
        artifacts: artifact_responses,
    })
}

fn stage_export(
    artifacts: &[InputArtifact],
    staging_directory: &Path,
    final_directory: &Path,
    export_id: &str,
    project_id: &str,
    file_base_name: &str,
    created_at_unix: i64,
) -> Result<(Vec<NativeExportArtifactResponse>, String), NativeExportError> {
    fs::create_dir(staging_directory)?;
    let mut responses = Vec::with_capacity(artifacts.len());
    for artifact in artifacts {
        let staged_path = staging_directory.join(&artifact.file_name);
        let mut file = OpenOptions::new()
            .write(true)
            .create_new(true)
            .open(&staged_path)?;
        file.write_all(artifact.content.as_bytes())?;
        file.sync_all()?;
        drop(file);
        let (sha256, byte_size) = sha256_file(&staged_path)?;
        responses.push(NativeExportArtifactResponse {
            file_name: artifact.file_name.clone(),
            mime_type: artifact.mime_type.clone(),
            sha256,
            byte_size,
            path: path_string(&final_directory.join(&artifact.file_name)),
        });
    }
    let manifest_json = serde_json::to_string_pretty(&ExportManifest {
        export_id,
        project_id,
        created_at_unix,
        file_base_name,
        artifacts: &responses,
    })
    .map_err(|error| NativeExportError::InvalidRequest(error.to_string()))?;
    let manifest_path = staging_directory.join("manifest.json");
    let mut manifest_file = OpenOptions::new()
        .write(true)
        .create_new(true)
        .open(manifest_path)?;
    manifest_file.write_all(manifest_json.as_bytes())?;
    manifest_file.sync_all()?;
    sync_directory(staging_directory)?;
    Ok((responses, manifest_json))
}

fn validate_artifacts(
    artifacts: &[InputArtifact],
    expected_project_id: &str,
) -> Result<(), NativeExportError> {
    if artifacts.is_empty() || artifacts.len() > MAX_ARTIFACT_COUNT {
        return Err(NativeExportError::InvalidRequest(format!(
            "artifact count must be between 1 and {MAX_ARTIFACT_COUNT}"
        )));
    }
    let mut names = BTreeSet::new();
    let mut total_bytes = 0usize;
    let mut project_artifact_count = 0;
    let mut packet_json_count = 0;
    let mut packet_markdown_count = 0;
    for artifact in artifacts {
        validate_file_name(&artifact.file_name)?;
        if !names.insert(artifact.file_name.to_ascii_lowercase()) {
            return Err(NativeExportError::InvalidRequest(
                "artifact filenames must be unique (case-insensitive)".to_string(),
            ));
        }
        if !matches!(
            artifact.mime_type.as_str(),
            "application/json" | "text/markdown"
        ) {
            return Err(NativeExportError::InvalidRequest(format!(
                "unsupported MIME type for {}",
                artifact.file_name
            )));
        }
        let byte_size = artifact.content.len();
        if byte_size > MAX_ARTIFACT_BYTES {
            return Err(NativeExportError::InvalidRequest(format!(
                "{} exceeds the 16 MiB artifact limit",
                artifact.file_name
            )));
        }
        total_bytes = total_bytes.checked_add(byte_size).ok_or_else(|| {
            NativeExportError::InvalidRequest("artifact byte total overflowed".to_string())
        })?;
        if total_bytes > MAX_TOTAL_BYTES {
            return Err(NativeExportError::InvalidRequest(
                "artifacts exceed the 48 MiB total limit".to_string(),
            ));
        }
        if artifact.mime_type == "application/json" {
            let json: Value = serde_json::from_str(&artifact.content).map_err(|error| {
                NativeExportError::InvalidRequest(format!(
                    "{} is not valid JSON: {error}",
                    artifact.file_name
                ))
            })?;
            if artifact.file_name.ends_with("-project.json") {
                project_artifact_count += 1;
                let project_id = json.get("projectId").and_then(Value::as_str).unwrap_or("");
                if project_id != expected_project_id {
                    return Err(NativeExportError::InvalidRequest(format!(
                        "portable project artifact projectId {project_id:?} does not match {expected_project_id:?}"
                    )));
                }
            } else {
                packet_json_count += 1;
            }
        } else if !artifact.file_name.ends_with("-native-setup.md") {
            packet_markdown_count += 1;
        }
    }
    if project_artifact_count != 1 || packet_json_count == 0 || packet_markdown_count == 0 {
        return Err(NativeExportError::InvalidRequest(
            "artifacts require exactly one *-project.json plus packet JSON and Markdown"
                .to_string(),
        ));
    }
    Ok(())
}

fn validate_file_name(file_name: &str) -> Result<(), NativeExportError> {
    let character_count = file_name.chars().count();
    if character_count == 0 || character_count > 160 {
        return Err(NativeExportError::InvalidRequest(
            "artifact filenames must contain 1–160 characters".to_string(),
        ));
    }
    if file_name == "."
        || file_name == ".."
        || file_name.ends_with([' ', '.'])
        || file_name.contains(['/', '\\', ':'])
        || file_name.chars().any(char::is_control)
        || file_name.eq_ignore_ascii_case("manifest.json")
    {
        return Err(NativeExportError::InvalidRequest(format!(
            "unsafe artifact filename: {file_name}"
        )));
    }
    let stem = file_name
        .split('.')
        .next()
        .unwrap_or("")
        .to_ascii_uppercase();
    let reserved = matches!(stem.as_str(), "CON" | "PRN" | "AUX" | "NUL")
        || (stem.len() == 4
            && matches!(&stem[..3], "COM" | "LPT")
            && matches!(stem.as_bytes()[3], b'1'..=b'9'));
    if reserved {
        return Err(NativeExportError::InvalidRequest(format!(
            "reserved Windows artifact filename: {file_name}"
        )));
    }
    Ok(())
}

fn validate_exports_directory(path: &Path) -> Result<(), NativeExportError> {
    let metadata = fs::symlink_metadata(path)?;
    if !metadata.is_dir() || metadata.file_type().is_symlink() {
        return Err(NativeExportError::InvalidRequest(
            "project exports path is not a regular directory".to_string(),
        ));
    }
    Ok(())
}

fn sha256_file(path: &Path) -> Result<(String, u64), std::io::Error> {
    let mut file = File::open(path)?;
    let mut hasher = Sha256::new();
    let mut buffer = [0u8; 64 * 1024];
    let mut byte_size = 0u64;
    loop {
        let read = file.read(&mut buffer)?;
        if read == 0 {
            break;
        }
        hasher.update(&buffer[..read]);
        byte_size += read as u64;
    }
    Ok((hex::encode(hasher.finalize()), byte_size))
}

#[cfg(not(windows))]
fn sync_directory(path: &Path) -> std::io::Result<()> {
    File::open(path)?.sync_all()
}

#[cfg(windows)]
fn sync_directory(_path: &Path) -> std::io::Result<()> {
    Ok(())
}

fn remove_flat_staging_directory(path: &Path) -> std::io::Result<()> {
    if !path.exists() {
        return Ok(());
    }
    for entry in fs::read_dir(path)? {
        let entry = entry?;
        if !entry.file_type()?.is_file() {
            return Err(std::io::Error::new(
                std::io::ErrorKind::InvalidData,
                "staging directory contains a non-file entry",
            ));
        }
        fs::remove_file(entry.path())?;
    }
    fs::remove_dir(path)
}

fn slugify(value: &str) -> String {
    let mut slug = String::new();
    let mut separator = false;
    for character in value.chars() {
        if character.is_ascii_alphanumeric() {
            if separator && !slug.is_empty() {
                slug.push('-');
            }
            slug.push(character.to_ascii_lowercase());
            separator = false;
        } else {
            separator = true;
        }
        if slug.len() >= 64 {
            break;
        }
    }
    slug.trim_end_matches('-').to_string()
}

fn path_string(path: &Path) -> String {
    path.to_string_lossy().into_owned()
}

#[cfg(test)]
mod tests {
    use super::{export_native_at, NativeExportError, NativeExportRequest};
    use crate::project_store::{create_project_at, ProjectCreateRequest};
    use rusqlite::Connection;
    use serde_json::json;
    use sha2::{Digest, Sha256};
    use std::fs;
    use std::path::{Path, PathBuf};
    use uuid::Uuid;

    #[test]
    fn publishes_exact_artifacts_and_completes_durable_manifest() {
        let fixture = ExportFixture::new();
        let export_id = Uuid::parse_str("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa").unwrap();
        let response = export_native_at(fixture.request(), export_id, 1_788_000_111).unwrap();

        assert_eq!(response.export_id, export_id.to_string());
        assert_eq!(response.artifacts.len(), 4);
        let export_directory = PathBuf::from(&response.export_directory);
        assert!(export_directory.is_dir());
        assert!(Path::new(&response.manifest_path).is_file());
        for (expected, actual) in fixture.artifacts().iter().zip(&response.artifacts) {
            let content = fs::read(&actual.path).unwrap();
            assert_eq!(content, expected["content"].as_str().unwrap().as_bytes());
            assert_eq!(actual.byte_size, content.len() as u64);
            assert_eq!(actual.sha256, hex::encode(Sha256::digest(&content)));
        }
        let manifest: serde_json::Value =
            serde_json::from_slice(&fs::read(&response.manifest_path).unwrap()).unwrap();
        assert_eq!(manifest["exportId"], export_id.to_string());
        assert_eq!(manifest["projectId"], fixture.project_id.to_string());
        assert_eq!(manifest["createdAtUnix"], 1_788_000_111i64);
        assert_eq!(manifest["artifacts"].as_array().unwrap().len(), 4);

        let connection = Connection::open(&fixture.sqlite_path).unwrap();
        let stored: (String, String, String, String) = connection
            .query_row(
                "SELECT status, final_directory, manifest_path, manifest_json
                 FROM export_manifests WHERE id = ?1",
                [export_id.to_string()],
                |row| Ok((row.get(0)?, row.get(1)?, row.get(2)?, row.get(3)?)),
            )
            .unwrap();
        assert_eq!(stored.0, "complete");
        assert_eq!(stored.1, response.export_directory);
        assert_eq!(stored.2, response.manifest_path);
        assert_eq!(
            serde_json::from_str::<serde_json::Value>(&stored.3).unwrap(),
            manifest
        );
        let artifact_count: i64 = connection
            .query_row(
                "SELECT COUNT(*) FROM export_artifacts WHERE export_id = ?1",
                [export_id.to_string()],
                |row| row.get(0),
            )
            .unwrap();
        assert_eq!(artifact_count, 4);
    }

    #[test]
    fn rejects_traversal_duplicates_malformed_json_and_project_mismatch_before_writing() {
        let fixture = ExportFixture::new();
        for artifacts in [
            json!([
                {"fileName":"../packet.json","mimeType":"application/json","content":"{}"},
                {"fileName":"packet.md","mimeType":"text/markdown","content":"packet"},
                {"fileName":format!("{}-project.json", fixture.project_id),"mimeType":"application/json","content":json!({"projectId":fixture.project_id}).to_string()}
            ]),
            json!([
                {"fileName":"packet.json","mimeType":"application/json","content":"{}"},
                {"fileName":"PACKET.JSON","mimeType":"application/json","content":"{}"},
                {"fileName":"packet.md","mimeType":"text/markdown","content":"packet"},
                {"fileName":format!("{}-project.json", fixture.project_id),"mimeType":"application/json","content":json!({"projectId":fixture.project_id}).to_string()}
            ]),
            json!([
                {"fileName":"packet.json","mimeType":"application/json","content":"{"},
                {"fileName":"packet.md","mimeType":"text/markdown","content":"packet"},
                {"fileName":format!("{}-project.json", fixture.project_id),"mimeType":"application/json","content":json!({"projectId":fixture.project_id}).to_string()}
            ]),
            json!([
                {"fileName":"packet.json","mimeType":"application/json","content":"{}"},
                {"fileName":"packet.md","mimeType":"text/markdown","content":"packet"},
                {"fileName":format!("{}-project.json", fixture.project_id),"mimeType":"application/json","content":json!({"projectId":"wrong-project"}).to_string()}
            ]),
        ] {
            let mut request = fixture.request();
            request.artifacts_json = artifacts.to_string();
            assert!(matches!(
                export_native_at(request, Uuid::new_v4(), 1_788_000_112),
                Err(NativeExportError::InvalidRequest(_))
            ));
        }
        let connection = Connection::open(&fixture.sqlite_path).unwrap();
        let count: i64 = connection
            .query_row("SELECT COUNT(*) FROM export_manifests", [], |row| {
                row.get(0)
            })
            .unwrap();
        assert_eq!(count, 0);
        assert_eq!(
            fs::read_dir(fixture.project_directory.join("exports"))
                .unwrap()
                .count(),
            0
        );
    }

    #[test]
    fn final_directory_collision_fails_manifest_and_cleans_staging_without_overwrite() {
        let fixture = ExportFixture::new();
        let export_id = Uuid::parse_str("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb").unwrap();
        let final_directory = fixture
            .project_directory
            .join("exports")
            .join(format!("review-packet-{}", export_id.simple()));
        fs::create_dir(&final_directory).unwrap();
        fs::write(final_directory.join("existing.txt"), b"keep").unwrap();

        assert!(matches!(
            export_native_at(fixture.request(), export_id, 1_788_000_113),
            Err(NativeExportError::Io(_))
        ));
        assert_eq!(
            fs::read(final_directory.join("existing.txt")).unwrap(),
            b"keep"
        );
        assert!(!fixture
            .project_directory
            .join("exports")
            .join(format!(".staging-{export_id}"))
            .exists());
        let connection = Connection::open(&fixture.sqlite_path).unwrap();
        let status: String = connection
            .query_row(
                "SELECT status FROM export_manifests WHERE id = ?1",
                [export_id.to_string()],
                |row| row.get(0),
            )
            .unwrap();
        assert_eq!(status, "failed");
    }

    struct ExportFixture {
        root: PathBuf,
        project_id: Uuid,
        project_directory: PathBuf,
        sqlite_path: PathBuf,
    }

    impl ExportFixture {
        fn new() -> Self {
            let root =
                std::env::temp_dir().join(format!("roadwatcher-export-test-{}", Uuid::new_v4()));
            fs::create_dir_all(&root).unwrap();
            let project_id = Uuid::new_v4();
            let created = create_project_at(
                ProjectCreateRequest {
                    project_name: "Native export".to_string(),
                    root_directory: root.clone(),
                },
                project_id,
                1_788_000_000,
            )
            .unwrap();
            Self {
                root,
                project_id,
                project_directory: PathBuf::from(created.project_directory),
                sqlite_path: PathBuf::from(created.sqlite_path),
            }
        }

        fn artifacts(&self) -> Vec<serde_json::Value> {
            vec![
                json!({"fileName":"review-packet.json","mimeType":"application/json","content":"{\n  \"packet\": true\n}"}),
                json!({"fileName":"review-packet.md","mimeType":"text/markdown","content":"# Packet\n"}),
                json!({"fileName":"review-packet-native-setup.md","mimeType":"text/markdown","content":"# Setup\n"}),
                json!({"fileName":format!("{}-project.json", self.project_id),"mimeType":"application/json","content":json!({"projectId":self.project_id,"schemaVersion":2}).to_string()}),
            ]
        }

        fn request(&self) -> NativeExportRequest {
            NativeExportRequest {
                sqlite_path: self.sqlite_path.clone(),
                project_id: self.project_id.to_string(),
                file_base_name: "Review Packet".to_string(),
                artifacts_json: serde_json::to_string(&self.artifacts()).unwrap(),
            }
        }
    }

    impl Drop for ExportFixture {
        fn drop(&mut self) {
            let _ = fs::remove_dir_all(&self.root);
        }
    }
}
