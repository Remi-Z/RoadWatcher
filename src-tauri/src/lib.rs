use cv_worker::{CvStartResponse, CvWorkerManager, CvWorkerRequest};
use gis_import::{import_gis as store_import_gis, GisImportRequest};
use gis_projector::GisProjectorManager;
use native_export::{
    export_native as store_export_native, NativeExportRequest, NativeExportResponse,
};
use project_store::{
    create_project, import_media as store_import_media, load_project_snapshot,
    read_proxy_job_status, save_project_snapshot, MediaImportRequest, MediaImportResponse,
    ProjectCreateRequest, ProjectCreateResponse, ProjectLoadResponse, ProjectSaveRequest,
    ProjectSaveResponse, ProxyJobRequest, ProxyJobStatus,
};
use proxy_worker::{ProxyStartResponse, ProxyWorkerManager};
use route_import::{import_gpx as store_import_gpx, GpxImportRequest};
use route_matcher::{RouteMatcherManager, RouteMatcherRequest};
use std::path::PathBuf;

mod cv_worker;
mod gis_import;
mod gis_projector;
mod native_export;
mod project_store;
mod proxy_worker;
mod route_import;
mod route_matcher;

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
fn native_export(
    sqlite_path: String,
    project_id: String,
    file_base_name: String,
    artifacts_json: String,
) -> Result<NativeExportResponse, String> {
    store_export_native(NativeExportRequest {
        sqlite_path: PathBuf::from(sqlite_path),
        project_id,
        file_base_name,
        artifacts_json,
    })
    .map_err(|error| error.to_string())
}

#[tauri::command]
fn cv_scan(
    state: tauri::State<'_, CvWorkerManager>,
    sqlite_path: String,
    project_id: String,
    media_id: String,
    model_path: String,
    labels_path: String,
    uv_executable: String,
    sidecar_directory: String,
    confidence_threshold: f64,
    sample_interval_seconds: f64,
    max_findings: i64,
) -> Result<CvStartResponse, String> {
    state
        .start(CvWorkerRequest {
            store: project_store::CvScanRequest {
                sqlite_path: PathBuf::from(sqlite_path),
                project_id,
                media_id,
                scan_id: uuid::Uuid::new_v4().to_string(),
                job_id: uuid::Uuid::new_v4().to_string(),
                model_path: PathBuf::from(model_path),
                labels_path: PathBuf::from(labels_path),
                confidence_threshold,
                sample_interval_seconds,
                max_findings,
            },
            uv_executable,
            sidecar_directory: PathBuf::from(sidecar_directory),
        })
        .map_err(|error| error.to_string())
}

#[tauri::command]
fn cv_job_status(
    sqlite_path: String,
    project_id: String,
    media_id: String,
    scan_id: String,
    job_id: String,
) -> Result<project_store::CvScanStatus, String> {
    project_store::read_cv_scan_status(&project_store::CvScanRequest {
        sqlite_path: PathBuf::from(sqlite_path),
        project_id,
        media_id,
        scan_id,
        job_id,
        model_path: PathBuf::new(),
        labels_path: PathBuf::new(),
        confidence_threshold: 0.5,
        sample_interval_seconds: 1.0,
        max_findings: 500,
    })
    .map_err(|error| error.to_string())
}

#[tauri::command]
fn cv_finding_review(
    sqlite_path: String,
    project_id: String,
    media_id: String,
    scan_id: String,
    finding_id: String,
    review_status: String,
    review_note: String,
) -> Result<project_store::CvFindingReviewResponse, String> {
    project_store::review_cv_finding(&project_store::CvFindingReviewRequest {
        sqlite_path: PathBuf::from(sqlite_path),
        project_id,
        media_id,
        scan_id,
        finding_id,
        review_status,
        review_note,
    })
    .map_err(|error| error.to_string())
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

#[tauri::command]
fn gpx_import(
    sqlite_path: String,
    project_id: String,
    source_path: String,
) -> Result<project_store::RouteImportResponse, String> {
    store_import_gpx(GpxImportRequest {
        sqlite_path: PathBuf::from(sqlite_path),
        project_id,
        source_path: PathBuf::from(source_path),
    })
    .map_err(|error| error.to_string())
}

#[tauri::command]
fn gis_import(
    sqlite_path: String,
    project_id: String,
    source_path: String,
    source_crs: String,
    layer_kind: String,
) -> Result<project_store::FeatureImportResponse, String> {
    store_import_gis(GisImportRequest {
        sqlite_path: PathBuf::from(sqlite_path),
        project_id,
        source_path: PathBuf::from(source_path),
        source_crs,
        layer_kind,
    })
    .map_err(|error| error.to_string())
}

#[tauri::command]
fn gis_project(
    state: tauri::State<'_, GisProjectorManager>,
    sqlite_path: String,
    project_id: String,
    feature_source_id: String,
    job_id: String,
    route_id: String,
    corridor_meters: f64,
) -> Result<gis_projector::GisProjectionStartResponse, String> {
    state
        .start(project_store::GisProjectionRequest {
            sqlite_path: PathBuf::from(sqlite_path),
            project_id,
            feature_source_id,
            job_id,
            route_id,
            corridor_meters,
        })
        .map_err(|error| error.to_string())
}

#[tauri::command]
fn gis_job_status(
    sqlite_path: String,
    project_id: String,
    feature_source_id: String,
    job_id: String,
) -> Result<project_store::GisProjectionStatus, String> {
    project_store::read_gis_projection_status(
        &PathBuf::from(sqlite_path),
        &project_id,
        &feature_source_id,
        &job_id,
    )
    .map_err(|error| error.to_string())
}

#[tauri::command]
fn gpx_match(
    state: tauri::State<'_, RouteMatcherManager>,
    sqlite_path: String,
    project_id: String,
    route_id: String,
    job_id: String,
    matcher: String,
    valhalla_endpoint: String,
    osrm_endpoint: String,
) -> Result<route_matcher::RouteMatchStartResponse, String> {
    state
        .start(RouteMatcherRequest {
            store: project_store::RouteMatchRequest {
                sqlite_path: PathBuf::from(sqlite_path),
                project_id,
                route_id,
                job_id,
            },
            matcher,
            valhalla_endpoint,
            osrm_endpoint,
        })
        .map_err(|error| error.to_string())
}

#[tauri::command]
fn gpx_job_status(
    sqlite_path: String,
    project_id: String,
    route_id: String,
    job_id: String,
) -> Result<project_store::RouteMatchStatus, String> {
    project_store::read_route_match_status(
        &PathBuf::from(sqlite_path),
        &project_id,
        &route_id,
        &job_id,
    )
    .map_err(|error| error.to_string())
}

#[tauri::command]
fn ffmpeg_proxy(
    state: tauri::State<'_, ProxyWorkerManager>,
    sqlite_path: String,
    project_id: String,
    media_id: String,
    job_id: String,
    profile: String,
    binary_directory: String,
) -> Result<ProxyStartResponse, String> {
    state
        .start(ProxyJobRequest {
            sqlite_path: PathBuf::from(sqlite_path),
            project_id,
            media_id,
            job_id,
            profile,
            binary_directory,
        })
        .map_err(|error| error.to_string())
}

#[tauri::command]
fn job_status(
    sqlite_path: String,
    project_id: String,
    job_id: String,
) -> Result<ProxyJobStatus, String> {
    read_proxy_job_status(&PathBuf::from(sqlite_path), &project_id, &job_id)
        .map_err(|error| error.to_string())
}

#[tauri::command]
fn job_cancel(
    state: tauri::State<'_, ProxyWorkerManager>,
    sqlite_path: String,
    project_id: String,
    media_id: String,
    job_id: String,
) -> Result<ProxyJobStatus, String> {
    state
        .cancel(&ProxyJobRequest {
            sqlite_path: PathBuf::from(sqlite_path),
            project_id,
            media_id,
            job_id,
            profile: "review-proxy".to_string(),
            binary_directory: String::new(),
        })
        .map_err(|error| error.to_string())
}

pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_dialog::init())
        .manage(ProxyWorkerManager::default())
        .manage(RouteMatcherManager::default())
        .manage(GisProjectorManager::default())
        .manage(CvWorkerManager::default())
        .invoke_handler(tauri::generate_handler![
            project_create,
            project_save,
            project_load,
            native_export,
            cv_scan,
            cv_job_status,
            cv_finding_review,
            media_import,
            gpx_import,
            gis_import,
            gis_project,
            gis_job_status,
            gpx_match,
            gpx_job_status,
            ffmpeg_proxy,
            job_status,
            job_cancel
        ])
        .run(tauri::generate_context!())
        .expect("error while running RoadWatcher Tauri app");
}

#[cfg(test)]
mod tests {
    use super::{job_status, media_import, project_create, project_load, project_save};

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

    #[test]
    fn job_status_surfaces_missing_project_errors() {
        let error = job_status(
            "missing.sqlite".to_string(),
            "project-1".to_string(),
            "job-1".to_string(),
        )
        .unwrap_err();
        assert!(error.starts_with("The selected file is not a RoadWatcher SQLite project:"));
    }
}
