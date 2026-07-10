use rusqlite::{params, Connection};
use serde::{Deserialize, Serialize};
use std::fs;
use std::path::{Path, PathBuf};
use std::time::{SystemTime, UNIX_EPOCH};
use thiserror::Error;
use uuid::Uuid;

const PROJECT_DATABASE_SCHEMA_VERSION: i64 = 2;

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
    if version == 1 {
        let transaction = connection.transaction()?;
        transaction.execute_batch(PROJECT_SNAPSHOT_TABLE_SQL)?;
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
PRAGMA user_version = 2;

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
    file_size_bytes INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE jobs (
    id TEXT PRIMARY KEY NOT NULL,
    project_id TEXT NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
    job_type TEXT NOT NULL,
    label TEXT NOT NULL,
    status TEXT NOT NULL,
    progress REAL NOT NULL DEFAULT 0,
    detail TEXT NOT NULL DEFAULT ''
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

#[cfg(test)]
mod tests {
    use super::{
        create_project_at, load_project_snapshot, save_project_snapshot, ProjectCreateRequest,
        ProjectSaveRequest, ProjectStoreError,
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
        assert_eq!(schema_version, 2);
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
    fn migrates_v1_and_round_trips_the_latest_snapshot() {
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

        let connection = Connection::open(&created.sqlite_path).unwrap();
        connection
            .execute("DROP TABLE project_snapshots", [])
            .unwrap();
        connection
            .execute("UPDATE schema_info SET version = 1", [])
            .unwrap();
        connection.pragma_update(None, "user_version", 1).unwrap();
        drop(connection);

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
        assert_eq!(user_version, 2);
        assert_eq!(schema_version, 2);
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
                supported: 2
            })
        ));
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
