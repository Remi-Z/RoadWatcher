use project_store::{
    create_project, import_media as store_import_media, load_project_snapshot,
    save_project_snapshot, MediaImportRequest, MediaImportResponse, ProjectCreateRequest,
    ProjectCreateResponse, ProjectLoadResponse, ProjectSaveRequest, ProjectSaveResponse,
};
use std::path::PathBuf;

mod project_store;

#[tauri::command]
fn project_create(
    project_name: String,
    root_directory: String,
) -> Result<ProjectCreateResponse, String> {
    create_project(ProjectCreateRequest {
        project_name,
        root_directory: PathBuf::from(root_directory),
    })
    .map_err(|error| error.to_string())
}

#[tauri::command]
fn project_save(sqlite_path: String, snapshot_json: String) -> Result<ProjectSaveResponse, String> {
    save_project_snapshot(ProjectSaveRequest {
        sqlite_path: PathBuf::from(sqlite_path),
        snapshot_json,
    })
    .map_err(|error| error.to_string())
}

#[tauri::command]
fn project_load(sqlite_path: String) -> Result<ProjectLoadResponse, String> {
    load_project_snapshot(&PathBuf::from(sqlite_path)).map_err(|error| error.to_string())
}

#[tauri::command]
fn media_import(
    sqlite_path: String,
    project_id: String,
    source_path: String,
) -> Result<MediaImportResponse, String> {
    store_import_media(MediaImportRequest {
        sqlite_path: PathBuf::from(sqlite_path),
        project_id,
        source_path: PathBuf::from(source_path),
    })
    .map_err(|error| error.to_string())
}

pub fn run() {
    tauri::Builder::default()
        .invoke_handler(tauri::generate_handler![
            project_create,
            project_save,
            project_load,
            media_import
        ])
        .run(tauri::generate_context!())
        .expect("error while running RoadWatcher Tauri app");
}

#[cfg(test)]
mod tests {
    use super::{media_import, project_create, project_load, project_save};

    #[test]
    fn project_create_surfaces_store_validation_errors() {
        let error = project_create("   ".to_string(), "C:/RoadWatcher".to_string()).unwrap_err();
        assert_eq!(error, "Project name must not be blank.");
    }

    #[test]
    fn project_save_and_load_surface_store_validation_errors() {
        let save_error = project_save("missing.sqlite".to_string(), "{".to_string()).unwrap_err();
        assert!(save_error.starts_with("Project snapshot is invalid:"));

        let load_error = project_load("missing.sqlite".to_string()).unwrap_err();
        assert!(load_error.starts_with("The selected file is not a RoadWatcher SQLite project:"));
    }

    #[test]
    fn media_import_surfaces_missing_source_errors() {
        let error = media_import(
            "missing.sqlite".to_string(),
            "project-1".to_string(),
            "missing.mp4".to_string(),
        )
        .unwrap_err();
        assert!(error.starts_with("The selected file is not a RoadWatcher SQLite project:"));
    }
}
