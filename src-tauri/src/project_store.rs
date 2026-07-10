use rusqlite::{params, Connection};
use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256};
use std::fs::{self, File};
use std::io::Read;
use std::path::{Path, PathBuf};
use std::time::{SystemTime, UNIX_EPOCH};
use thiserror::Error;
use uuid::Uuid;

const PROJECT_DATABASE_SCHEMA_VERSION: i64 = 3;

#[derive(Debug)]
pub struct ProjectCreateRequest {
    pub project_name: String,
    pub root_directory: PathBuf,
}

#[derive(Clone, Debug, Eq, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ProjectCreateResponse {
    pub project_id: String,
    pub project_directory: String,
    pub sqlite_path: String,
}

#[derive(Debug)]
pub struct ProjectSaveRequest {
    pub sqlite_path: PathBuf,
    pub snapshot_json: String,
}

#[derive(Clone, Debug, Eq, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ProjectSaveResponse {
    pub project_id: String,
    pub schema_version: i64,
    pub saved_at_iso: String,
}

#[derive(Clone, Debug, Eq, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ProjectLoadResponse {
    pub project_id: String,
    pub schema_version: i64,
    pub saved_at_iso: String,
    pub snapshot_json: String,
}

#[derive(Debug)]
pub struct MediaImportRequest {
    pub sqlite_path: PathBuf,
    pub project_id: String,
    pub source_path: PathBuf,
}

#[derive(Clone, Debug, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct MediaImportResponse {
    pub media_id: String,
    pub file_name: String,
    pub original_path: String,
    pub hash: String,
    pub file_size_bytes: u64,
    pub duration_seconds: f64,
    pub detected_start: String,
    pub proxy_status: String,
    pub proxy_job_id: String,
}

#[derive(Clone, Debug)]
pub struct ProxyJobRequest {
    pub sqlite_path: PathBuf,
    pub project_id: String,
    pub media_id: String,
    pub job_id: String,
    pub profile: String,
    pub binary_directory: String,
}

#[derive(Clone, Debug, PartialEq)]
pub struct ClaimedProxyJob {
    pub source_path: PathBuf,
    pub project_directory: PathBuf,
}

#[derive(Clone, Debug, PartialEq)]
pub struct ProxyCompletion {
    pub duration_seconds: f64,
    pub detected_start: String,
    pub proxy_path: String,
    pub thumbnail_directory: String,
    pub video_codec: String,
}

#[derive(Clone, Debug, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ProxyJobStatus {
    pub job_id: String,
    pub media_id: String,
    pub status: String,
    pub progress: f64,
    pub detail: String,
    pub duration_seconds: f64,
    pub detected_start: String,
    pub proxy_status: String,
    pub proxy_path: String,
    pub thumbnail_directory: String,
    pub video_codec: String,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct SnapshotEnvelope {
    project_id: String,
    schema_version: i64,
    saved_at_iso: String,
}

#[derive(Debug, Error)]
pub enum ProjectStoreError {
    #[error("Project name must not be blank.")]
    InvalidProjectName,
    #[error("Root directory must not be blank.")]
    InvalidRootDirectory,
    #[error("Could not initialize the project filesystem: {0}")]
    Io(#[from] std::io::Error),
    #[error("Could not initialize the SQLite project: {0}")]
    Sqlite(#[from] rusqlite::Error),
    #[error("The selected file is not a RoadWatcher SQLite project: {0}")]
    InvalidDatabase(String),
    #[error(
        "RoadWatcher database schema version {found} is newer than supported version {supported}."
    )]
    UnsupportedDatabaseVersion { found: i64, supported: i64 },
    #[error("Project snapshot is invalid: {0}")]
    InvalidSnapshot(String),
    #[error("Snapshot project ID {snapshot_id} does not match database project ID {database_id}.")]
    ProjectIdentityMismatch {
        snapshot_id: String,
        database_id: String,
    },
    #[error("The RoadWatcher project does not contain a saved snapshot yet.")]
    SnapshotMissing,
    #[error("Media source must be an existing regular file: {0}")]
    InvalidMediaSource(String),
    #[error("Media source is too large for SQLite byte-size metadata: {0}")]
    MediaSourceTooLarge(String),
    #[error("Unsupported proxy profile: {0}")]
    InvalidProxyProfile(String),
    #[error("Proxy job was not found for the supplied project/media identity.")]
    ProxyJobNotFound,
    #[error("Proxy job cannot be claimed from status {0}.")]
    InvalidProxyJobState(String),
    #[error("System clock is before the Unix epoch.")]
    InvalidSystemClock,
}

pub fn create_project(
    request: ProjectCreateRequest,
) -> Result<ProjectCreateResponse, ProjectStoreError> {
    let created_at_unix = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map_err(|_| ProjectStoreError::InvalidSystemClock)?
        .as_secs() as i64;
    create_project_at(request, Uuid::new_v4(), created_at_unix)
}

pub fn create_project_at(
    request: ProjectCreateRequest,
    project_id: Uuid,
    created_at_unix: i64,
) -> Result<ProjectCreateResponse, ProjectStoreError> {
    let display_name = request.project_name.trim();
    if display_name.is_empty() {
        return Err(ProjectStoreError::InvalidProjectName);
    }
    if request.root_directory.to_string_lossy().trim().is_empty() {
        return Err(ProjectStoreError::InvalidRootDirectory);
    }

    fs::create_dir_all(&request.root_directory)?;
    let directory_name = format!("{}-{}", slugify(display_name), project_id.simple());
    let project_directory = request.root_directory.join(directory_name);
    fs::create_dir(&project_directory)?;

    let result = initialize_project_directory(
        &project_directory,
        display_name,
        project_id,
        created_at_unix,
    );
    if let Err(error) = result {
        let _ = fs::remove_dir_all(&project_directory);
        return Err(error);
    }

    let sqlite_path = project_directory.join("project.sqlite");
    Ok(ProjectCreateResponse {
        project_id: project_id.to_string(),
        project_directory: path_string(&project_directory),
        sqlite_path: path_string(&sqlite_path),
    })
}

fn initialize_project_directory(
    project_directory: &Path,
    display_name: &str,
    project_id: Uuid,
    created_at_unix: i64,
) -> Result<(), ProjectStoreError> {
    for directory in ["assets", "proxies", "exports", "logs"] {
        fs::create_dir(project_directory.join(directory))?;
    }

    let sqlite_path = project_directory.join("project.sqlite");
    let connection = Connection::open(sqlite_path)?;
    connection.execute_batch(SCHEMA_SQL)?;
    connection.execute(
        "INSERT INTO schema_info (version) VALUES (?1)",
        params![PROJECT_DATABASE_SCHEMA_VERSION],
    )?;
    connection.execute(
        "INSERT INTO projects (id, display_name, created_at_unix) VALUES (?1, ?2, ?3)",
        params![project_id.to_string(), display_name, created_at_unix],
    )?;
    Ok(())
}

pub fn save_project_snapshot(
    request: ProjectSaveRequest,
) -> Result<ProjectSaveResponse, ProjectStoreError> {
    let envelope: SnapshotEnvelope = serde_json::from_str(&request.snapshot_json)
        .map_err(|error| ProjectStoreError::InvalidSnapshot(error.to_string()))?;
    if envelope.project_id.trim().is_empty() {
        return Err(ProjectStoreError::InvalidSnapshot(
            "projectId must not be blank".to_string(),
        ));
    }
    if envelope.schema_version <= 0 {
        return Err(ProjectStoreError::InvalidSnapshot(
            "schemaVersion must be a positive integer".to_string(),
        ));
    }
    if envelope.saved_at_iso.trim().is_empty() {
        return Err(ProjectStoreError::InvalidSnapshot(
            "savedAtIso must not be blank".to_string(),
        ));
    }

    let mut connection = open_project_database(&request.sqlite_path)?;
    let database_id = project_id(&connection)?;
    if envelope.project_id != database_id {
        return Err(ProjectStoreError::ProjectIdentityMismatch {
            snapshot_id: envelope.project_id,
            database_id,
        });
    }

    let transaction = connection.transaction()?;
    transaction.execute(
        "INSERT INTO project_snapshots (project_id, snapshot_schema_version, saved_at_iso, snapshot_json)
         VALUES (?1, ?2, ?3, ?4)
         ON CONFLICT(project_id) DO UPDATE SET
           snapshot_schema_version = excluded.snapshot_schema_version,
           saved_at_iso = excluded.saved_at_iso,
           snapshot_json = excluded.snapshot_json",
        params![
            envelope.project_id,
            envelope.schema_version,
            envelope.saved_at_iso,
            request.snapshot_json
        ],
    )?;
    transaction.commit()?;

    Ok(ProjectSaveResponse {
        project_id: envelope.project_id,
        schema_version: envelope.schema_version,
        saved_at_iso: envelope.saved_at_iso,
    })
}

pub fn load_project_snapshot(sqlite_path: &Path) -> Result<ProjectLoadResponse, ProjectStoreError> {
    let connection = open_project_database(sqlite_path)?;
    let database_id = project_id(&connection)?;
    let response = connection.query_row(
        "SELECT project_id, snapshot_schema_version, saved_at_iso, snapshot_json
         FROM project_snapshots WHERE project_id = ?1",
        params![database_id],
        |row| {
            Ok(ProjectLoadResponse {
                project_id: row.get(0)?,
                schema_version: row.get(1)?,
                saved_at_iso: row.get(2)?,
                snapshot_json: row.get(3)?,
            })
        },
    );
    match response {
        Ok(response) => Ok(response),
        Err(rusqlite::Error::QueryReturnedNoRows) => Err(ProjectStoreError::SnapshotMissing),
        Err(error) => Err(error.into()),
    }
}

pub fn import_media(request: MediaImportRequest) -> Result<MediaImportResponse, ProjectStoreError> {
    import_media_at(request, Uuid::new_v4(), Uuid::new_v4())
}

pub fn import_media_at(
    request: MediaImportRequest,
    media_id: Uuid,
    proxy_job_id: Uuid,
) -> Result<MediaImportResponse, ProjectStoreError> {
    let mut connection = open_project_database(&request.sqlite_path)?;
    let database_id = project_id(&connection)?;
    if request.project_id != database_id {
        return Err(ProjectStoreError::ProjectIdentityMismatch {
            snapshot_id: request.project_id,
            database_id,
        });
    }

    let metadata = fs::metadata(&request.source_path)
        .map_err(|_| ProjectStoreError::InvalidMediaSource(path_string(&request.source_path)))?;
    if !metadata.is_file() {
        return Err(ProjectStoreError::InvalidMediaSource(path_string(
            &request.source_path,
        )));
    }
    let file_name = request
        .source_path
        .file_name()
        .filter(|name| !name.is_empty())
        .map(|name| name.to_string_lossy().into_owned())
        .ok_or_else(|| ProjectStoreError::InvalidMediaSource(path_string(&request.source_path)))?;
    let hash = sha256_file(&request.source_path)?;
    let original_path = path_string(&request.source_path);
    let file_size_bytes = i64::try_from(metadata.len())
        .map_err(|_| ProjectStoreError::MediaSourceTooLarge(original_path.clone()))?;
    let media_id = media_id.to_string();
    let proxy_job_id = proxy_job_id.to_string();
    let detail = "Original referenced and hashed; ffprobe/FFmpeg pending";

    let transaction = connection.transaction()?;
    transaction.execute(
        "INSERT INTO media_assets (
           id, project_id, file_name, original_path, duration_seconds, detected_start,
           proxy_status, hash, file_size_bytes
         ) VALUES (?1, ?2, ?3, ?4, 0, '', 'queued', ?5, ?6)",
        params![
            media_id,
            request.project_id,
            file_name,
            original_path,
            hash,
            file_size_bytes
        ],
    )?;
    transaction.execute(
        "INSERT INTO jobs (id, project_id, media_id, job_type, label, status, progress, detail)
         VALUES (?1, ?2, ?3, 'proxy', ?4, 'queued', 0, ?5)",
        params![
            proxy_job_id,
            request.project_id,
            media_id,
            format!("Auto proxy: {file_name}"),
            detail
        ],
    )?;
    transaction.commit()?;

    Ok(MediaImportResponse {
        media_id,
        file_name,
        original_path,
        hash,
        file_size_bytes: metadata.len(),
        duration_seconds: 0.0,
        detected_start: String::new(),
        proxy_status: "queued".to_string(),
        proxy_job_id,
    })
}

fn sha256_file(path: &Path) -> Result<String, ProjectStoreError> {
    let mut file = File::open(path)?;
    let mut digest = Sha256::new();
    let mut buffer = vec![0_u8; 1024 * 1024];
    loop {
        let count = file.read(&mut buffer)?;
        if count == 0 {
            break;
        }
        digest.update(&buffer[..count]);
    }
    Ok(hex::encode(digest.finalize()))
}

pub fn claim_proxy_job(request: &ProxyJobRequest) -> Result<ClaimedProxyJob, ProjectStoreError> {
    if request.profile != "review-proxy" {
        return Err(ProjectStoreError::InvalidProxyProfile(
            request.profile.clone(),
        ));
    }
    let mut connection = open_project_database(&request.sqlite_path)?;
    verify_project_identity(&connection, &request.project_id)?;
    let source_path: String = connection
        .query_row(
            "SELECT original_path FROM media_assets WHERE id = ?1 AND project_id = ?2",
            params![request.media_id, request.project_id],
            |row| row.get(0),
        )
        .map_err(|error| match error {
            rusqlite::Error::QueryReturnedNoRows => ProjectStoreError::ProxyJobNotFound,
            other => other.into(),
        })?;
    let current_status: String = connection
        .query_row(
            "SELECT status FROM jobs
             WHERE id = ?1 AND project_id = ?2 AND job_type = 'proxy'
               AND (media_id = '' OR media_id = ?3)",
            params![request.job_id, request.project_id, request.media_id],
            |row| row.get(0),
        )
        .map_err(|error| match error {
            rusqlite::Error::QueryReturnedNoRows => ProjectStoreError::ProxyJobNotFound,
            other => other.into(),
        })?;
    if current_status == "complete" || current_status == "running" {
        return Err(ProjectStoreError::InvalidProxyJobState(current_status));
    }

    let transaction = connection.transaction()?;
    let updated = transaction.execute(
        "UPDATE jobs
         SET media_id = ?1, status = 'running', progress = 1,
             detail = 'probing source metadata', cancellation_requested = 0
         WHERE id = ?2 AND project_id = ?3 AND job_type = 'proxy'
           AND (media_id = '' OR media_id = ?1)",
        params![request.media_id, request.job_id, request.project_id],
    )?;
    if updated != 1 {
        return Err(ProjectStoreError::ProxyJobNotFound);
    }
    transaction.execute(
        "UPDATE media_assets SET proxy_status = 'running'
         WHERE id = ?1 AND project_id = ?2",
        params![request.media_id, request.project_id],
    )?;
    transaction.commit()?;
    let project_directory = request
        .sqlite_path
        .parent()
        .ok_or_else(|| ProjectStoreError::InvalidDatabase(path_string(&request.sqlite_path)))?;
    Ok(ClaimedProxyJob {
        source_path: PathBuf::from(source_path),
        project_directory: project_directory.to_path_buf(),
    })
}

pub fn read_proxy_job_status(
    sqlite_path: &Path,
    expected_project_id: &str,
    job_id: &str,
) -> Result<ProxyJobStatus, ProjectStoreError> {
    let connection = open_project_database(sqlite_path)?;
    verify_project_identity(&connection, expected_project_id)?;
    connection
        .query_row(
            "SELECT j.id, j.media_id, j.status, j.progress, j.detail,
                    m.duration_seconds, m.detected_start, m.proxy_status,
                    m.proxy_path, m.thumbnail_directory, m.video_codec
             FROM jobs j JOIN media_assets m
               ON m.id = j.media_id AND m.project_id = j.project_id
             WHERE j.id = ?1 AND j.project_id = ?2 AND j.job_type = 'proxy'",
            params![job_id, expected_project_id],
            |row| {
                Ok(ProxyJobStatus {
                    job_id: row.get(0)?,
                    media_id: row.get(1)?,
                    status: row.get(2)?,
                    progress: row.get(3)?,
                    detail: row.get(4)?,
                    duration_seconds: row.get(5)?,
                    detected_start: row.get(6)?,
                    proxy_status: row.get(7)?,
                    proxy_path: row.get(8)?,
                    thumbnail_directory: row.get(9)?,
                    video_codec: row.get(10)?,
                })
            },
        )
        .map_err(|error| match error {
            rusqlite::Error::QueryReturnedNoRows => ProjectStoreError::ProxyJobNotFound,
            other => other.into(),
        })
}

pub fn update_proxy_progress(
    request: &ProxyJobRequest,
    progress: f64,
    detail: &str,
) -> Result<(), ProjectStoreError> {
    let connection = open_project_database(&request.sqlite_path)?;
    verify_project_identity(&connection, &request.project_id)?;
    let updated = connection.execute(
        "UPDATE jobs SET progress = ?1, detail = ?2
         WHERE id = ?3 AND project_id = ?4 AND media_id = ?5
           AND job_type = 'proxy' AND status = 'running'",
        params![
            progress.clamp(1.0, 99.0),
            detail,
            request.job_id,
            request.project_id,
            request.media_id
        ],
    )?;
    if updated == 1 {
        Ok(())
    } else {
        Err(ProjectStoreError::ProxyJobNotFound)
    }
}

pub fn complete_proxy_job(
    request: &ProxyJobRequest,
    completion: ProxyCompletion,
) -> Result<(), ProjectStoreError> {
    let mut connection = open_project_database(&request.sqlite_path)?;
    verify_project_identity(&connection, &request.project_id)?;
    let transaction = connection.transaction()?;
    let media_updated = transaction.execute(
        "UPDATE media_assets
         SET duration_seconds = ?1, detected_start = ?2, proxy_status = 'ready',
             proxy_path = ?3, thumbnail_directory = ?4, video_codec = ?5
         WHERE id = ?6 AND project_id = ?7",
        params![
            completion.duration_seconds,
            completion.detected_start,
            completion.proxy_path,
            completion.thumbnail_directory,
            completion.video_codec,
            request.media_id,
            request.project_id
        ],
    )?;
    let job_updated = transaction.execute(
        "UPDATE jobs SET status = 'complete', progress = 100, detail = 'proxy and thumbnails ready'
         WHERE id = ?1 AND project_id = ?2 AND media_id = ?3 AND job_type = 'proxy'",
        params![request.job_id, request.project_id, request.media_id],
    )?;
    if media_updated != 1 || job_updated != 1 {
        return Err(ProjectStoreError::ProxyJobNotFound);
    }
    transaction.commit()?;
    Ok(())
}

pub fn fail_proxy_job(
    request: &ProxyJobRequest,
    status: &str,
    detail: &str,
) -> Result<(), ProjectStoreError> {
    if !matches!(status, "blocked" | "failed" | "cancelled") {
        return Err(ProjectStoreError::InvalidProxyJobState(status.to_string()));
    }
    let mut connection = open_project_database(&request.sqlite_path)?;
    verify_project_identity(&connection, &request.project_id)?;
    let transaction = connection.transaction()?;
    let media_updated = transaction.execute(
        "UPDATE media_assets SET proxy_status = 'blocked'
         WHERE id = ?1 AND project_id = ?2",
        params![request.media_id, request.project_id],
    )?;
    let job_updated = transaction.execute(
        "UPDATE jobs SET status = ?1, detail = ?2
         WHERE id = ?3 AND project_id = ?4 AND media_id = ?5 AND job_type = 'proxy'",
        params![
            status,
            detail,
            request.job_id,
            request.project_id,
            request.media_id
        ],
    )?;
    if media_updated != 1 || job_updated != 1 {
        return Err(ProjectStoreError::ProxyJobNotFound);
    }
    transaction.commit()?;
    Ok(())
}

pub fn request_proxy_job_cancel(
    request: &ProxyJobRequest,
) -> Result<ProxyJobStatus, ProjectStoreError> {
    let connection = open_project_database(&request.sqlite_path)?;
    verify_project_identity(&connection, &request.project_id)?;
    let updated = connection.execute(
        "UPDATE jobs SET cancellation_requested = 1
         WHERE id = ?1 AND project_id = ?2 AND media_id = ?3 AND job_type = 'proxy'",
        params![request.job_id, request.project_id, request.media_id],
    )?;
    if updated != 1 {
        return Err(ProjectStoreError::ProxyJobNotFound);
    }
    read_proxy_job_status(&request.sqlite_path, &request.project_id, &request.job_id)
}

fn verify_project_identity(
    connection: &Connection,
    expected_project_id: &str,
) -> Result<(), ProjectStoreError> {
    let database_id = project_id(connection)?;
    if database_id == expected_project_id {
        Ok(())
    } else {
        Err(ProjectStoreError::ProjectIdentityMismatch {
            snapshot_id: expected_project_id.to_string(),
            database_id,
        })
    }
}

fn open_project_database(sqlite_path: &Path) -> Result<Connection, ProjectStoreError> {
    if !sqlite_path.is_file() {
        return Err(ProjectStoreError::InvalidDatabase(path_string(sqlite_path)));
    }
    let mut connection = Connection::open(sqlite_path)?;
    connection.execute_batch("PRAGMA foreign_keys = ON;")?;
    migrate_database(&mut connection)?;
    Ok(connection)
}

fn migrate_database(connection: &mut Connection) -> Result<(), ProjectStoreError> {
    let version: i64 = connection.query_row("PRAGMA user_version", [], |row| row.get(0))?;
    if version > PROJECT_DATABASE_SCHEMA_VERSION {
        return Err(ProjectStoreError::UnsupportedDatabaseVersion {
            found: version,
            supported: PROJECT_DATABASE_SCHEMA_VERSION,
        });
    }
    if version < 1 {
        return Err(ProjectStoreError::InvalidDatabase(
            "missing RoadWatcher schema version".to_string(),
        ));
    }
    if version < PROJECT_DATABASE_SCHEMA_VERSION {
        let transaction = connection.transaction()?;
        if version == 1 {
            transaction.execute_batch(PROJECT_SNAPSHOT_TABLE_SQL)?;
        }
        if version <= 2 {
            transaction.execute_batch(PROXY_JOB_MIGRATION_SQL)?;
        }
        transaction.execute(
            "UPDATE schema_info SET version = ?1",
            params![PROJECT_DATABASE_SCHEMA_VERSION],
        )?;
        transaction.pragma_update(None, "user_version", PROJECT_DATABASE_SCHEMA_VERSION)?;
        transaction.commit()?;
    }
    let recorded_version: i64 =
        connection.query_row("SELECT version FROM schema_info", [], |row| row.get(0))?;
    if recorded_version != PROJECT_DATABASE_SCHEMA_VERSION {
        return Err(ProjectStoreError::InvalidDatabase(format!(
            "schema_info records version {recorded_version}"
        )));
    }
    Ok(())
}

fn project_id(connection: &Connection) -> Result<String, ProjectStoreError> {
    connection
        .query_row("SELECT id FROM projects LIMIT 1", [], |row| row.get(0))
        .map_err(ProjectStoreError::from)
}

fn slugify(value: &str) -> String {
    let mut slug = String::new();
    let mut pending_separator = false;
    for character in value.chars() {
        if character.is_ascii_alphanumeric() {
            if pending_separator && !slug.is_empty() {
                slug.push('-');
            }
            slug.push(character.to_ascii_lowercase());
            pending_separator = false;
        } else {
            pending_separator = true;
        }
        if slug.len() >= 48 {
            break;
        }
    }
    slug.trim_end_matches('-').to_string()
}

fn path_string(path: &Path) -> String {
    path.to_string_lossy().into_owned()
}

const SCHEMA_SQL: &str = r#"
PRAGMA foreign_keys = ON;
PRAGMA journal_mode = WAL;
PRAGMA user_version = 3;

CREATE TABLE schema_info (
    version INTEGER NOT NULL CHECK (version > 0)
);

CREATE TABLE projects (
    id TEXT PRIMARY KEY NOT NULL,
    display_name TEXT NOT NULL,
    created_at_unix INTEGER NOT NULL
);

CREATE TABLE media_assets (
    id TEXT PRIMARY KEY NOT NULL,
    project_id TEXT NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
    file_name TEXT NOT NULL,
    original_path TEXT NOT NULL,
    duration_seconds REAL NOT NULL DEFAULT 0,
    detected_start TEXT NOT NULL DEFAULT '',
    proxy_status TEXT NOT NULL,
    hash TEXT NOT NULL DEFAULT '',
    file_size_bytes INTEGER NOT NULL DEFAULT 0,
    proxy_path TEXT NOT NULL DEFAULT '',
    thumbnail_directory TEXT NOT NULL DEFAULT '',
    video_codec TEXT NOT NULL DEFAULT ''
);

CREATE TABLE jobs (
    id TEXT PRIMARY KEY NOT NULL,
    project_id TEXT NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
    media_id TEXT NOT NULL DEFAULT '',
    job_type TEXT NOT NULL,
    label TEXT NOT NULL,
    status TEXT NOT NULL,
    progress REAL NOT NULL DEFAULT 0,
    detail TEXT NOT NULL DEFAULT '',
    cancellation_requested INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE timeline_clips (
    id TEXT PRIMARY KEY NOT NULL,
    project_id TEXT NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
    media_id TEXT NOT NULL REFERENCES media_assets(id),
    source_in_seconds REAL NOT NULL,
    source_out_seconds REAL NOT NULL,
    reel_start_seconds REAL NOT NULL,
    label TEXT NOT NULL
);

CREATE TABLE route_points (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    project_id TEXT NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
    sequence INTEGER NOT NULL,
    latitude REAL NOT NULL,
    longitude REAL NOT NULL,
    time_seconds REAL NOT NULL,
    UNIQUE(project_id, sequence)
);

CREATE TABLE official_features (
    id TEXT PRIMARY KEY NOT NULL,
    project_id TEXT NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
    kind TEXT NOT NULL,
    latitude REAL NOT NULL,
    longitude REAL NOT NULL,
    source_layer TEXT NOT NULL
);

CREATE TABLE projected_features (
    feature_id TEXT PRIMARY KEY NOT NULL REFERENCES official_features(id) ON DELETE CASCADE,
    project_id TEXT NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
    kind TEXT NOT NULL,
    source_layer TEXT NOT NULL,
    time_seconds REAL NOT NULL,
    distance_meters REAL NOT NULL,
    confidence REAL NOT NULL,
    review_status TEXT NOT NULL,
    review_note TEXT NOT NULL DEFAULT ''
);

CREATE TABLE component_slots (
    id TEXT PRIMARY KEY NOT NULL,
    project_id TEXT NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
    label TEXT NOT NULL,
    owner_action TEXT NOT NULL,
    status TEXT NOT NULL,
    reference TEXT NOT NULL DEFAULT '',
    notes TEXT NOT NULL DEFAULT ''
);

CREATE TABLE native_command_attempts (
    id TEXT PRIMARY KEY NOT NULL,
    project_id TEXT NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
    command TEXT NOT NULL,
    status TEXT NOT NULL,
    requested_at_iso TEXT NOT NULL,
    request_summary TEXT NOT NULL,
    result_summary TEXT NOT NULL
);

CREATE TABLE project_snapshots (
    project_id TEXT PRIMARY KEY NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
    snapshot_schema_version INTEGER NOT NULL CHECK (snapshot_schema_version > 0),
    saved_at_iso TEXT NOT NULL,
    snapshot_json TEXT NOT NULL
);
"#;

const PROJECT_SNAPSHOT_TABLE_SQL: &str = r#"
CREATE TABLE IF NOT EXISTS project_snapshots (
    project_id TEXT PRIMARY KEY NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
    snapshot_schema_version INTEGER NOT NULL CHECK (snapshot_schema_version > 0),
    saved_at_iso TEXT NOT NULL,
    snapshot_json TEXT NOT NULL
);
"#;

const PROXY_JOB_MIGRATION_SQL: &str = r#"
ALTER TABLE media_assets ADD COLUMN proxy_path TEXT NOT NULL DEFAULT '';
ALTER TABLE media_assets ADD COLUMN thumbnail_directory TEXT NOT NULL DEFAULT '';
ALTER TABLE media_assets ADD COLUMN video_codec TEXT NOT NULL DEFAULT '';
ALTER TABLE jobs ADD COLUMN media_id TEXT NOT NULL DEFAULT '';
ALTER TABLE jobs ADD COLUMN cancellation_requested INTEGER NOT NULL DEFAULT 0;
UPDATE jobs
SET status = 'queued', progress = 0,
    detail = 'Proxy job recovered after restart.', cancellation_requested = 0
WHERE job_type = 'proxy' AND status = 'running';
UPDATE media_assets SET proxy_status = 'queued' WHERE proxy_status = 'running';
"#;

#[cfg(test)]
mod tests {
    use super::{
        claim_proxy_job, complete_proxy_job, create_project_at, fail_proxy_job, import_media_at,
        load_project_snapshot, read_proxy_job_status, request_proxy_job_cancel,
        save_project_snapshot, update_proxy_progress, MediaImportRequest, ProjectCreateRequest,
        ProjectSaveRequest, ProjectStoreError, ProxyCompletion, ProxyJobRequest,
    };
    use rusqlite::Connection;
    use std::collections::BTreeSet;
    use std::fs;
    use std::path::{Path, PathBuf};
    use uuid::Uuid;

    #[test]
    fn creates_sanitized_uuid_layout_and_sqlite_foundation() {
        let root = TestRoot::new();
        let project_id = Uuid::parse_str("11111111-1111-4111-8111-111111111111").unwrap();

        let response = create_project_at(
            ProjectCreateRequest {
                project_name: "  Warden & Hwy 7 Review  ".to_string(),
                root_directory: root.path().to_path_buf(),
            },
            project_id,
            1_788_000_000,
        )
        .unwrap();

        let project_directory = PathBuf::from(&response.project_directory);
        let sqlite_path = PathBuf::from(&response.sqlite_path);
        assert_eq!(response.project_id, project_id.to_string());
        assert_eq!(
            project_directory.file_name().unwrap().to_string_lossy(),
            "warden-hwy-7-review-11111111111141118111111111111111"
        );
        assert!(sqlite_path.is_file());
        for directory in ["assets", "proxies", "exports", "logs"] {
            assert!(
                project_directory.join(directory).is_dir(),
                "missing {directory}"
            );
        }

        let connection = Connection::open(sqlite_path).unwrap();
        let schema_version: i64 = connection
            .query_row("SELECT version FROM schema_info", [], |row| row.get(0))
            .unwrap();
        let metadata: (String, String, i64) = connection
            .query_row(
                "SELECT id, display_name, created_at_unix FROM projects",
                [],
                |row| Ok((row.get(0)?, row.get(1)?, row.get(2)?)),
            )
            .unwrap();
        assert_eq!(schema_version, 3);
        assert_eq!(
            metadata,
            (
                project_id.to_string(),
                "Warden & Hwy 7 Review".to_string(),
                1_788_000_000
            )
        );

        let table_names: BTreeSet<String> = connection
            .prepare("SELECT name FROM sqlite_master WHERE type = 'table'")
            .unwrap()
            .query_map([], |row| row.get(0))
            .unwrap()
            .map(Result::unwrap)
            .collect();
        for table in [
            "schema_info",
            "projects",
            "media_assets",
            "jobs",
            "timeline_clips",
            "route_points",
            "official_features",
            "projected_features",
            "component_slots",
            "native_command_attempts",
            "project_snapshots",
        ] {
            assert!(table_names.contains(table), "missing table {table}");
        }
    }

    #[test]
    fn rejects_blank_project_names_and_root_directories() {
        let root = TestRoot::new();
        let id = Uuid::nil();
        let blank_name = create_project_at(
            ProjectCreateRequest {
                project_name: "   ".to_string(),
                root_directory: root.path().to_path_buf(),
            },
            id,
            0,
        );
        let blank_root = create_project_at(
            ProjectCreateRequest {
                project_name: "Review".to_string(),
                root_directory: PathBuf::from("   "),
            },
            id,
            0,
        );

        assert!(matches!(
            blank_name,
            Err(ProjectStoreError::InvalidProjectName)
        ));
        assert!(matches!(
            blank_root,
            Err(ProjectStoreError::InvalidRootDirectory)
        ));
    }

    #[test]
    fn serializes_the_registered_camel_case_response_contract() {
        let response = super::ProjectCreateResponse {
            project_id: "local-native".to_string(),
            project_directory: "C:/RoadWatcher/native".to_string(),
            sqlite_path: "C:/RoadWatcher/native/project.sqlite".to_string(),
        };

        assert_eq!(
            serde_json::to_value(response).unwrap(),
            serde_json::json!({
                "projectId": "local-native",
                "projectDirectory": "C:/RoadWatcher/native",
                "sqlitePath": "C:/RoadWatcher/native/project.sqlite"
            })
        );
    }

    #[test]
    fn round_trips_the_latest_snapshot_in_current_schema() {
        let root = TestRoot::new();
        let project_id = Uuid::parse_str("22222222-2222-4222-8222-222222222222").unwrap();
        let created = create_project_at(
            ProjectCreateRequest {
                project_name: "Persistence review".to_string(),
                root_directory: root.path().to_path_buf(),
            },
            project_id,
            1_788_000_000,
        )
        .unwrap();

        let first = snapshot_json(project_id, 2, "2026-07-10T12:00:00.000Z", "First");
        let saved = save_project_snapshot(ProjectSaveRequest {
            sqlite_path: PathBuf::from(&created.sqlite_path),
            snapshot_json: first,
        })
        .unwrap();
        assert_eq!(saved.project_id, project_id.to_string());
        assert_eq!(saved.schema_version, 2);
        assert_eq!(saved.saved_at_iso, "2026-07-10T12:00:00.000Z");

        let second = snapshot_json(project_id, 2, "2026-07-10T13:00:00.000Z", "Updated");
        save_project_snapshot(ProjectSaveRequest {
            sqlite_path: PathBuf::from(&created.sqlite_path),
            snapshot_json: second.clone(),
        })
        .unwrap();
        let loaded = load_project_snapshot(Path::new(&created.sqlite_path)).unwrap();
        assert_eq!(loaded.project_id, project_id.to_string());
        assert_eq!(loaded.schema_version, 2);
        assert_eq!(loaded.saved_at_iso, "2026-07-10T13:00:00.000Z");
        assert_eq!(loaded.snapshot_json, second);

        let connection = Connection::open(&created.sqlite_path).unwrap();
        let user_version: i64 = connection
            .query_row("PRAGMA user_version", [], |row| row.get(0))
            .unwrap();
        let schema_version: i64 = connection
            .query_row("SELECT version FROM schema_info", [], |row| row.get(0))
            .unwrap();
        assert_eq!(user_version, 3);
        assert_eq!(schema_version, 3);
    }

    #[test]
    fn rejects_invalid_or_mismatched_snapshot_envelopes_without_writing() {
        let root = TestRoot::new();
        let project_id = Uuid::parse_str("33333333-3333-4333-8333-333333333333").unwrap();
        let created = create_project_at(
            ProjectCreateRequest {
                project_name: "Envelope review".to_string(),
                root_directory: root.path().to_path_buf(),
            },
            project_id,
            1_788_000_000,
        )
        .unwrap();

        let invalid = save_project_snapshot(ProjectSaveRequest {
            sqlite_path: PathBuf::from(&created.sqlite_path),
            snapshot_json: "{".to_string(),
        });
        assert!(matches!(
            invalid,
            Err(ProjectStoreError::InvalidSnapshot(_))
        ));

        let mismatched = save_project_snapshot(ProjectSaveRequest {
            sqlite_path: PathBuf::from(&created.sqlite_path),
            snapshot_json: snapshot_json(Uuid::nil(), 2, "2026-07-10T12:00:00.000Z", "Wrong"),
        });
        assert!(matches!(
            mismatched,
            Err(ProjectStoreError::ProjectIdentityMismatch { .. })
        ));
        assert!(matches!(
            load_project_snapshot(Path::new(&created.sqlite_path)),
            Err(ProjectStoreError::SnapshotMissing)
        ));
    }

    #[test]
    fn rejects_missing_and_future_database_files_without_creating_them() {
        let root = TestRoot::new();
        let missing = root.path().join("missing.sqlite");
        assert!(matches!(
            load_project_snapshot(&missing),
            Err(ProjectStoreError::InvalidDatabase(_))
        ));
        assert!(!missing.exists());

        let project_id = Uuid::parse_str("44444444-4444-4444-8444-444444444444").unwrap();
        let created = create_project_at(
            ProjectCreateRequest {
                project_name: "Future review".to_string(),
                root_directory: root.path().to_path_buf(),
            },
            project_id,
            1_788_000_000,
        )
        .unwrap();
        let connection = Connection::open(&created.sqlite_path).unwrap();
        connection.pragma_update(None, "user_version", 99).unwrap();
        drop(connection);

        assert!(matches!(
            load_project_snapshot(Path::new(&created.sqlite_path)),
            Err(ProjectStoreError::UnsupportedDatabaseVersion {
                found: 99,
                supported: 3
            })
        ));
    }

    #[test]
    fn imports_referenced_media_with_streaming_hash_and_atomic_proxy_job() {
        let root = TestRoot::new();
        let project_id = Uuid::parse_str("55555555-5555-4555-8555-555555555555").unwrap();
        let media_id = Uuid::parse_str("66666666-6666-4666-8666-666666666666").unwrap();
        let job_id = Uuid::parse_str("77777777-7777-4777-8777-777777777777").unwrap();
        let created = create_project_at(
            ProjectCreateRequest {
                project_name: "Media review".to_string(),
                root_directory: root.path().to_path_buf(),
            },
            project_id,
            1_788_000_000,
        )
        .unwrap();
        let source_path = root.path().join("front camera.mp4");
        fs::write(&source_path, b"abc").unwrap();

        let response = import_media_at(
            MediaImportRequest {
                sqlite_path: PathBuf::from(&created.sqlite_path),
                project_id: project_id.to_string(),
                source_path: source_path.clone(),
            },
            media_id,
            job_id,
        )
        .unwrap();

        assert_eq!(response.media_id, media_id.to_string());
        assert_eq!(response.proxy_job_id, job_id.to_string());
        assert_eq!(response.file_name, "front camera.mp4");
        assert_eq!(response.original_path, source_path.to_string_lossy());
        assert_eq!(response.file_size_bytes, 3);
        assert_eq!(
            response.hash,
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"
        );
        assert_eq!(response.duration_seconds, 0.0);
        assert_eq!(response.detected_start, "");
        assert_eq!(response.proxy_status, "queued");

        let connection = Connection::open(&created.sqlite_path).unwrap();
        let media: (String, String, String, i64) = connection
            .query_row(
                "SELECT file_name, original_path, hash, file_size_bytes FROM media_assets WHERE id = ?1",
                [media_id.to_string()],
                |row| Ok((row.get(0)?, row.get(1)?, row.get(2)?, row.get(3)?)),
            )
            .unwrap();
        assert_eq!(
            media,
            (
                "front camera.mp4".to_string(),
                source_path.to_string_lossy().into_owned(),
                response.hash.clone(),
                3
            )
        );
        let job: (String, String, String) = connection
            .query_row(
                "SELECT job_type, status, detail FROM jobs WHERE id = ?1",
                [job_id.to_string()],
                |row| Ok((row.get(0)?, row.get(1)?, row.get(2)?)),
            )
            .unwrap();
        assert_eq!(job.0, "proxy");
        assert_eq!(job.1, "queued");
        assert!(job.2.contains("ffprobe/FFmpeg pending"));
    }

    #[test]
    fn rejects_missing_media_and_project_mismatch_without_partial_rows() {
        let root = TestRoot::new();
        let project_id = Uuid::parse_str("88888888-8888-4888-8888-888888888888").unwrap();
        let created = create_project_at(
            ProjectCreateRequest {
                project_name: "Rejected media".to_string(),
                root_directory: root.path().to_path_buf(),
            },
            project_id,
            1_788_000_000,
        )
        .unwrap();
        let source_path = root.path().join("source.mp4");
        fs::write(&source_path, b"evidence").unwrap();

        let mismatch = import_media_at(
            MediaImportRequest {
                sqlite_path: PathBuf::from(&created.sqlite_path),
                project_id: Uuid::nil().to_string(),
                source_path,
            },
            Uuid::new_v4(),
            Uuid::new_v4(),
        );
        assert!(matches!(
            mismatch,
            Err(ProjectStoreError::ProjectIdentityMismatch { .. })
        ));

        let missing = import_media_at(
            MediaImportRequest {
                sqlite_path: PathBuf::from(&created.sqlite_path),
                project_id: project_id.to_string(),
                source_path: root.path().join("missing.mp4"),
            },
            Uuid::new_v4(),
            Uuid::new_v4(),
        );
        assert!(matches!(
            missing,
            Err(ProjectStoreError::InvalidMediaSource(_))
        ));

        let connection = Connection::open(&created.sqlite_path).unwrap();
        let media_count: i64 = connection
            .query_row("SELECT COUNT(*) FROM media_assets", [], |row| row.get(0))
            .unwrap();
        let job_count: i64 = connection
            .query_row("SELECT COUNT(*) FROM jobs", [], |row| row.get(0))
            .unwrap();
        assert_eq!((media_count, job_count), (0, 0));
    }

    #[test]
    fn migrates_v2_proxy_state_and_recovers_stale_running_jobs() {
        let root = TestRoot::new();
        let sqlite_path = root.path().join("v2-project.sqlite");
        let project_id = "99999999-9999-4999-8999-999999999999";
        let connection = Connection::open(&sqlite_path).unwrap();
        connection
            .execute_batch(
                "PRAGMA user_version = 2;
                 CREATE TABLE schema_info (version INTEGER NOT NULL);
                 INSERT INTO schema_info VALUES (2);
                 CREATE TABLE projects (id TEXT PRIMARY KEY, display_name TEXT NOT NULL, created_at_unix INTEGER NOT NULL);
                 INSERT INTO projects VALUES ('99999999-9999-4999-8999-999999999999', 'V2 review', 0);
                 CREATE TABLE project_snapshots (
                   project_id TEXT PRIMARY KEY, snapshot_schema_version INTEGER NOT NULL,
                   saved_at_iso TEXT NOT NULL, snapshot_json TEXT NOT NULL
                 );
                 CREATE TABLE media_assets (
                   id TEXT PRIMARY KEY, project_id TEXT NOT NULL, file_name TEXT NOT NULL,
                   original_path TEXT NOT NULL, duration_seconds REAL NOT NULL DEFAULT 0,
                   detected_start TEXT NOT NULL DEFAULT '', proxy_status TEXT NOT NULL,
                   hash TEXT NOT NULL DEFAULT '', file_size_bytes INTEGER NOT NULL DEFAULT 0
                 );
                 INSERT INTO media_assets VALUES ('media-v2', '99999999-9999-4999-8999-999999999999', 'v2.mp4', 'D:/v2.mp4', 0, '', 'running', '', 1);
                 CREATE TABLE jobs (
                   id TEXT PRIMARY KEY, project_id TEXT NOT NULL, job_type TEXT NOT NULL,
                   label TEXT NOT NULL, status TEXT NOT NULL, progress REAL NOT NULL DEFAULT 0,
                   detail TEXT NOT NULL DEFAULT ''
                 );
                 INSERT INTO jobs VALUES ('job-v2', '99999999-9999-4999-8999-999999999999', 'proxy', 'Auto proxy: v2.mp4', 'running', 44, 'interrupted');",
            )
            .unwrap();
        drop(connection);

        assert!(matches!(
            load_project_snapshot(&sqlite_path),
            Err(ProjectStoreError::SnapshotMissing)
        ));
        let connection = Connection::open(&sqlite_path).unwrap();
        let recovered: (String, f64, String) = connection
            .query_row(
                "SELECT status, progress, detail FROM jobs WHERE id = 'job-v2'",
                [],
                |row| Ok((row.get(0)?, row.get(1)?, row.get(2)?)),
            )
            .unwrap();
        assert_eq!(recovered.0, "queued");
        assert_eq!(recovered.1, 0.0);
        assert!(recovered.2.contains("recovered after restart"));
        drop(connection);

        let migrated_request = ProxyJobRequest {
            sqlite_path: sqlite_path.clone(),
            project_id: project_id.to_string(),
            media_id: "media-v2".to_string(),
            job_id: "job-v2".to_string(),
            profile: "review-proxy".to_string(),
            binary_directory: String::new(),
        };
        claim_proxy_job(&migrated_request).unwrap();
        let status = read_proxy_job_status(&sqlite_path, project_id, "job-v2").unwrap();
        assert_eq!(status.media_id, "media-v2");
        assert_eq!(status.status, "running");

        let connection = Connection::open(&sqlite_path).unwrap();
        let version: i64 = connection
            .query_row("PRAGMA user_version", [], |row| row.get(0))
            .unwrap();
        assert_eq!(version, 3);
        let media_columns: BTreeSet<String> = connection
            .prepare("PRAGMA table_info(media_assets)")
            .unwrap()
            .query_map([], |row| row.get(1))
            .unwrap()
            .map(Result::unwrap)
            .collect();
        for column in ["proxy_path", "thumbnail_directory", "video_codec"] {
            assert!(media_columns.contains(column), "missing {column}");
        }
        let job_columns: BTreeSet<String> = connection
            .prepare("PRAGMA table_info(jobs)")
            .unwrap()
            .query_map([], |row| row.get(1))
            .unwrap()
            .map(Result::unwrap)
            .collect();
        assert!(job_columns.contains("cancellation_requested"));
    }

    #[test]
    fn claims_updates_completes_and_cancels_proxy_jobs_with_identity_guards() {
        let root = TestRoot::new();
        let project_id = Uuid::parse_str("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa").unwrap();
        let media_id = Uuid::parse_str("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb").unwrap();
        let job_id = Uuid::parse_str("cccccccc-cccc-4ccc-8ccc-cccccccccccc").unwrap();
        let created = create_project_at(
            ProjectCreateRequest {
                project_name: "Proxy state".to_string(),
                root_directory: root.path().to_path_buf(),
            },
            project_id,
            1_788_000_000,
        )
        .unwrap();
        let source_path = root.path().join("source.mp4");
        fs::write(&source_path, b"source").unwrap();
        import_media_at(
            MediaImportRequest {
                sqlite_path: PathBuf::from(&created.sqlite_path),
                project_id: project_id.to_string(),
                source_path: source_path.clone(),
            },
            media_id,
            job_id,
        )
        .unwrap();
        let request = ProxyJobRequest {
            sqlite_path: PathBuf::from(&created.sqlite_path),
            project_id: project_id.to_string(),
            media_id: media_id.to_string(),
            job_id: job_id.to_string(),
            profile: "review-proxy".to_string(),
            binary_directory: String::new(),
        };

        let claimed = claim_proxy_job(&request).unwrap();
        assert_eq!(claimed.source_path, source_path);
        assert_eq!(
            claimed.project_directory,
            PathBuf::from(&created.project_directory)
        );
        assert_eq!(
            read_proxy_job_status(&request.sqlite_path, &request.project_id, &request.job_id)
                .unwrap()
                .status,
            "running"
        );

        update_proxy_progress(&request, 42.5, "rendering with libx264").unwrap();
        let running =
            read_proxy_job_status(&request.sqlite_path, &request.project_id, &request.job_id)
                .unwrap();
        assert_eq!(running.progress, 42.5);
        assert_eq!(running.detail, "rendering with libx264");

        fail_proxy_job(&request, "failed", "hardware and CPU encoders failed").unwrap();
        let failed =
            read_proxy_job_status(&request.sqlite_path, &request.project_id, &request.job_id)
                .unwrap();
        assert_eq!(failed.status, "failed");
        assert_eq!(failed.proxy_status, "blocked");
        assert_eq!(failed.detail, "hardware and CPU encoders failed");
        claim_proxy_job(&request).unwrap();

        complete_proxy_job(
            &request,
            ProxyCompletion {
                duration_seconds: 12.5,
                detected_start: "2026-07-10T12:00:00Z".to_string(),
                proxy_path: "D:/project/proxies/review.mp4".to_string(),
                thumbnail_directory: "D:/project/proxies/thumbs".to_string(),
                video_codec: "libx264".to_string(),
            },
        )
        .unwrap();
        let complete =
            read_proxy_job_status(&request.sqlite_path, &request.project_id, &request.job_id)
                .unwrap();
        assert_eq!(complete.status, "complete");
        assert_eq!(complete.progress, 100.0);
        assert_eq!(complete.proxy_status, "ready");
        assert_eq!(complete.duration_seconds, 12.5);
        assert_eq!(complete.video_codec, "libx264");

        let wrong = ProxyJobRequest {
            project_id: Uuid::nil().to_string(),
            ..request.clone()
        };
        assert!(matches!(
            claim_proxy_job(&wrong),
            Err(ProjectStoreError::ProjectIdentityMismatch { .. })
        ));

        let cancellation = request_proxy_job_cancel(&request).unwrap();
        assert_eq!(cancellation.status, "complete");
        let connection = Connection::open(&created.sqlite_path).unwrap();
        let cancellation_requested: i64 = connection
            .query_row(
                "SELECT cancellation_requested FROM jobs WHERE id = ?1",
                [job_id.to_string()],
                |row| row.get(0),
            )
            .unwrap();
        assert_eq!(cancellation_requested, 1);
    }

    fn snapshot_json(
        project_id: Uuid,
        schema_version: i64,
        saved_at_iso: &str,
        title: &str,
    ) -> String {
        serde_json::json!({
            "schemaVersion": schema_version,
            "projectId": project_id.to_string(),
            "savedAtIso": saved_at_iso,
            "incident": { "title": title }
        })
        .to_string()
    }

    struct TestRoot(PathBuf);

    impl TestRoot {
        fn new() -> Self {
            let path = std::env::temp_dir()
                .join(format!("roadwatcher-project-store-test-{}", Uuid::new_v4()));
            fs::create_dir_all(&path).unwrap();
            Self(path)
        }

        fn path(&self) -> &Path {
            &self.0
        }
    }

    impl Drop for TestRoot {
        fn drop(&mut self) {
            let _ = fs::remove_dir_all(&self.0);
        }
    }
}
