use rusqlite::{params, Connection};
use serde::Serialize;
use std::fs;
use std::path::{Path, PathBuf};
use std::time::{SystemTime, UNIX_EPOCH};
use thiserror::Error;
use uuid::Uuid;

const PROJECT_SCHEMA_VERSION: i64 = 1;

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
        params![PROJECT_SCHEMA_VERSION],
    )?;
    connection.execute(
        "INSERT INTO projects (id, display_name, created_at_unix) VALUES (?1, ?2, ?3)",
        params![project_id.to_string(), display_name, created_at_unix],
    )?;
    Ok(())
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
PRAGMA user_version = 1;

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
"#;

#[cfg(test)]
mod tests {
    use super::{create_project_at, ProjectCreateRequest, ProjectStoreError};
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
        assert_eq!(schema_version, 1);
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
