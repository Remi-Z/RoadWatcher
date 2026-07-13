use crate::{
    cv_worker::{CvWorkerManager, CvWorkerRequest},
    gpstitch_worker::{GpstitchWorkerManager, GpstitchWorkerRequest},
    managed_runtime,
    project_store::{
        self, CvScanRequest, GpstitchRenderRequest, MediaImportRequest, ProjectCreateRequest,
        ProxyJobRequest,
    },
    proxy_worker::ProxyWorkerManager,
    route_import::{self, GpxImportRequest},
};
use std::{
    env, fs,
    path::{Path, PathBuf},
    thread,
    time::{Duration, Instant},
};
use uuid::Uuid;

struct TemporaryRideProject(PathBuf);

impl Drop for TemporaryRideProject {
    fn drop(&mut self) {
        let _ = fs::remove_dir_all(&self.0);
    }
}

fn required_path(variable: &str) -> PathBuf {
    let path = env::var_os(variable)
        .map(PathBuf::from)
        .unwrap_or_else(|| panic!("{variable} must point to a real-test dependency"));
    assert!(
        path.exists(),
        "{variable} does not exist: {}",
        path.display()
    );
    path
}

fn smallest_file_with_extension(root: &Path, extension: &str) -> Option<PathBuf> {
    let mut pending = vec![root.to_path_buf()];
    let mut smallest: Option<(u64, PathBuf)> = None;
    while let Some(directory) = pending.pop() {
        for entry in fs::read_dir(directory).ok()? {
            let entry = entry.ok()?;
            let file_type = entry.file_type().ok()?;
            if file_type.is_dir() {
                pending.push(entry.path());
            } else if entry
                .path()
                .extension()
                .and_then(|value| value.to_str())
                .is_some_and(|value| value.eq_ignore_ascii_case(extension))
            {
                let size = entry.metadata().ok()?.len();
                if smallest.as_ref().is_none_or(|current| size < current.0) {
                    smallest = Some((size, entry.path()));
                }
            }
        }
    }
    smallest.map(|(_, path)| path)
}

fn wait_for_terminal_status<T>(
    label: &str,
    timeout: Duration,
    mut read: impl FnMut() -> T,
    status: impl Fn(&T) -> &str,
    detail: impl Fn(&T) -> &str,
) -> T {
    let deadline = Instant::now() + timeout;
    loop {
        let current = read();
        let state = status(&current);
        if matches!(state, "complete" | "failed" | "blocked" | "cancelled") {
            assert_eq!(
                state,
                "complete",
                "{label} ended as {state}: {}",
                detail(&current)
            );
            return current;
        }
        assert!(
            Instant::now() < deadline,
            "{label} did not finish before timeout"
        );
        thread::sleep(Duration::from_millis(500));
    }
}

#[test]
#[ignore = "requires the private ride dataset, uv, FFmpeg, an ONNX model, and several minutes"]
fn real_ride_dataset_exercises_ingest_proxy_cv_and_gpstitch() {
    let dataset = required_path("ROADWATCHER_RIDE_DATASET");
    let model_path = required_path("ROADWATCHER_CV_MODEL");
    let labels_path = required_path("ROADWATCHER_CV_LABELS");
    let gpx_path = dataset.join("Ride.gpx");
    assert!(
        gpx_path.is_file(),
        "Ride.gpx is missing from {}",
        dataset.display()
    );
    let lrv_path = smallest_file_with_extension(&dataset, "lrv")
        .expect("the ride dataset does not contain an LRV file");

    let temp_root = env::temp_dir().join(format!("roadwatcher-real-ride-{}", Uuid::new_v4()));
    fs::create_dir_all(&temp_root).expect("could not create isolated ride-test directory");
    let _cleanup = TemporaryRideProject(temp_root.clone());
    let media_path = temp_root.join("ride-sample.mp4");
    fs::copy(&lrv_path, &media_path).expect("could not copy the representative LRV sample");

    let project = project_store::create_project(ProjectCreateRequest {
        project_name: "Real ride dataset smoke".to_string(),
        root_directory: temp_root.clone(),
    })
    .expect("could not create isolated RoadWatcher project");
    let sqlite_path = PathBuf::from(&project.sqlite_path);
    let media = project_store::import_media(MediaImportRequest {
        sqlite_path: sqlite_path.clone(),
        project_id: project.project_id.clone(),
        source_path: media_path,
    })
    .expect("real ride media import failed");
    assert!(media.file_size_bytes > 100_000_000);

    let proxy_request = ProxyJobRequest {
        sqlite_path: sqlite_path.clone(),
        project_id: project.project_id.clone(),
        media_id: media.media_id.clone(),
        job_id: media.proxy_job_id.clone(),
        profile: "review-proxy".to_string(),
        binary_directory: String::new(),
    };
    ProxyWorkerManager::default()
        .start(proxy_request.clone())
        .expect("real ride proxy did not start");
    let proxy = wait_for_terminal_status(
        "proxy generation",
        Duration::from_secs(10 * 60),
        || {
            project_store::read_proxy_job_status(
                &sqlite_path,
                &project.project_id,
                &proxy_request.job_id,
            )
            .expect("could not read proxy status")
        },
        |status| &status.status,
        |status| &status.detail,
    );
    assert!((140.0..150.0).contains(&proxy.duration_seconds));
    assert!(Path::new(&proxy.proxy_path).is_file());

    let route = route_import::import_gpx(GpxImportRequest {
        sqlite_path: sqlite_path.clone(),
        project_id: project.project_id.clone(),
        source_path: gpx_path,
    })
    .expect("real Ride.gpx import failed");
    assert_eq!(route.route.len(), 4_484);
    assert!(route.route.last().expect("route is empty").time_seconds > 4_480.0);

    let manifest_directory = PathBuf::from(env!("CARGO_MANIFEST_DIR"));
    let gpstitch_source = manifest_directory.join("../sidecars/roadwatcher-gpstitch");
    let cv_source = manifest_directory.join("../sidecars/roadwatcher-cv");
    let valhalla_source = manifest_directory.join("../sidecars/roadwatcher-valhalla");
    let environments = managed_runtime::managed_environment_paths(&temp_root.join("app-data"));
    let preparation =
        managed_runtime::prepare_runtime_environments(managed_runtime::RuntimePrepareRequest {
            uv_executable: "uv".to_string(),
            gpstitch_source: gpstitch_source.clone(),
            cv_source: cv_source.clone(),
            valhalla_source,
            environments: environments.clone(),
            allow_valhalla_sync: true,
        });
    assert_eq!(
        preparation.status, "ready",
        "runtime preparation failed: {preparation:?}"
    );

    let cv_store = CvScanRequest {
        sqlite_path: sqlite_path.clone(),
        project_id: project.project_id.clone(),
        media_id: media.media_id.clone(),
        scan_id: format!("scan-{}", Uuid::new_v4()),
        job_id: format!("cv-{}", Uuid::new_v4()),
        model_path,
        labels_path,
        confidence_threshold: 0.20,
        sample_interval_seconds: 10.0,
        max_findings: 100,
    };
    CvWorkerManager::default()
        .start(CvWorkerRequest {
            store: cv_store.clone(),
            sidecar_directory: cv_source,
            environment_directory: environments.cv,
        })
        .expect("real ride CV scan did not start");
    let cv = wait_for_terminal_status(
        "CV scan",
        Duration::from_secs(10 * 60),
        || project_store::read_cv_scan_status(&cv_store).expect("could not read CV status"),
        |status| &status.status,
        |status| &status.detail,
    );
    assert_eq!(cv.media_id, media.media_id);
    assert!(cv.finding_count <= 100);

    let render_store = GpstitchRenderRequest {
        sqlite_path: sqlite_path.clone(),
        project_id: project.project_id.clone(),
        media_id: media.media_id,
        route_id: route.route_id,
        render_id: format!("render-{}", Uuid::new_v4()),
        job_id: format!("gpstitch-{}", Uuid::new_v4()),
        layout: "speed-awareness".to_string(),
        alignment: "auto".to_string(),
        time_offset_seconds: 0,
    };
    GpstitchWorkerManager::default()
        .start(GpstitchWorkerRequest {
            store: render_store.clone(),
            sidecar_directory: gpstitch_source,
            environment_directory: environments.gpstitch,
            ffmpeg_binary_directory: None,
        })
        .expect("real ride GPStitch render did not start");
    let render = wait_for_terminal_status(
        "GPStitch render",
        Duration::from_secs(20 * 60),
        || {
            project_store::read_gpstitch_render_status(&render_store)
                .expect("could not read GPStitch status")
        },
        |status| &status.status,
        |status| &status.detail,
    );
    assert!(render.output_size_bytes > 0);
    assert!(Path::new(&render.output_path).is_file());
    assert!(!render.output_hash.is_empty());
    eprintln!(
        "real ride smoke: sample={} bytes={} duration={:.2}s route_points={} cv_findings={} overlay_bytes={} overlay_sha256={}",
        lrv_path
            .file_name()
            .and_then(|value| value.to_str())
            .unwrap_or("<unknown>"),
        media.file_size_bytes,
        proxy.duration_seconds,
        route.route.len(),
        cv.finding_count,
        render.output_size_bytes,
        render.output_hash,
    );
}
