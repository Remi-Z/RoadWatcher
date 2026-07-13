use crate::bounded_process::run_bounded_process;
use crate::managed_runtime::{environment_python, probe_managed_environment};
use crate::project_store::{
    claim_gpstitch_render, complete_gpstitch_render, fail_gpstitch_render, queue_gpstitch_render,
    ClaimedGpstitchRender, GpstitchRenderRequest, ProjectStoreError,
};
use chrono::DateTime;
use serde::Serialize;
use std::collections::HashSet;
use std::fs::{self, File, FileTimes};
use std::path::{Path, PathBuf};
use std::process::Command;
use std::sync::{Arc, Mutex};
use std::thread;
use std::time::{Duration, UNIX_EPOCH};
use thiserror::Error;

pub const PINNED_GPSTITCH_VERSION: &str = "0.18.0";
const MAX_PROCESS_OUTPUT_BYTES: u64 = 4 * 1024 * 1024;
const MAX_RENDER_DURATION: Duration = Duration::from_secs(4 * 60 * 60);

#[derive(Clone, Debug)]
pub struct GpstitchWorkerRequest {
    pub store: GpstitchRenderRequest,
    pub sidecar_directory: PathBuf,
    pub environment_directory: PathBuf,
}

#[derive(Clone, Debug, Eq, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct GpstitchStartResponse {
    pub render_id: String,
    pub job_id: String,
    pub status: String,
}

#[derive(Debug, Error)]
pub enum GpstitchWorkerError {
    #[error("GPStitch project store operation failed: {0}")]
    Store(#[from] ProjectStoreError),
    #[error("GPStitch sidecar configuration is invalid: {0}")]
    InvalidConfiguration(String),
    #[error("GPStitch render execution failed: {0}")]
    Execution(String),
    #[error("GPStitch render output is invalid: {0}")]
    InvalidOutput(String),
}

trait GpstitchExecutor: Send + Sync {
    fn execute(
        &self,
        request: &GpstitchWorkerRequest,
        claimed: &ClaimedGpstitchRender,
        input_path: &Path,
        staging_output: &Path,
    ) -> Result<(), GpstitchWorkerError>;
}

struct ProcessGpstitchExecutor;

impl GpstitchExecutor for ProcessGpstitchExecutor {
    fn execute(
        &self,
        request: &GpstitchWorkerRequest,
        claimed: &ClaimedGpstitchRender,
        input_path: &Path,
        staging_output: &Path,
    ) -> Result<(), GpstitchWorkerError> {
        validate_sidecar(request)?;
        // Invoke the installed module through the managed interpreter. On
        // Windows, uv's generated command launcher embeds the pre-promotion
        // staging path and cannot be used after the environment is renamed.
        let mut command = Command::new(environment_python(&request.environment_directory));
        command
            .arg("-m")
            .arg("gpstitch.scripts.gopro_dashboard_wrapper")
            .arg(input_path)
            .arg(staging_output)
            .arg("--use-gpx-only")
            .arg("--gpx")
            .arg(&claimed.route_path);
        if claimed.alignment != "gpx_timestamps" {
            command.arg("--video-time-start").arg("file-modified");
        }
        let overlay_font = overlay_font_path().ok_or_else(|| {
            GpstitchWorkerError::InvalidConfiguration(
                "no supported system TrueType overlay font was found".to_string(),
            )
        })?;
        command
            .arg("--layout")
            .arg(&claimed.layout)
            .arg("--font")
            .arg(overlay_font);
        let result =
            run_bounded_process(&mut command, MAX_RENDER_DURATION, MAX_PROCESS_OUTPUT_BYTES)
                .map_err(|error| GpstitchWorkerError::Execution(error.to_string()))?;
        if !result.status.success() {
            return Err(GpstitchWorkerError::Execution(bounded_text(&result.stderr)));
        }
        if !staging_output.is_file() || fs::metadata(staging_output)?.len() == 0 {
            return Err(GpstitchWorkerError::InvalidOutput(
                "pinned renderer did not create a non-empty output".to_string(),
            ));
        }
        Ok(())
    }
}

pub struct GpstitchWorkerManager {
    active_jobs: Arc<Mutex<HashSet<String>>>,
    executor: Arc<dyn GpstitchExecutor>,
}

impl Default for GpstitchWorkerManager {
    fn default() -> Self {
        Self {
            active_jobs: Arc::new(Mutex::new(HashSet::new())),
            executor: Arc::new(ProcessGpstitchExecutor),
        }
    }
}

impl GpstitchWorkerManager {
    pub fn start(
        &self,
        request: GpstitchWorkerRequest,
    ) -> Result<GpstitchStartResponse, GpstitchWorkerError> {
        queue_gpstitch_render(&request.store)?;
        let job_id = request.store.job_id.clone();
        let response_job_id = job_id.clone();
        let render_id = request.store.render_id.clone();
        {
            let mut active = self.active_jobs.lock().map_err(|_| {
                GpstitchWorkerError::Execution("GPStitch worker state lock is poisoned".to_string())
            })?;
            if !active.insert(job_id.clone()) {
                return Err(GpstitchWorkerError::InvalidConfiguration(
                    "GPStitch job is already active".to_string(),
                ));
            }
        }
        let executor = Arc::clone(&self.executor);
        let active_jobs = Arc::clone(&self.active_jobs);
        thread::spawn(move || {
            if let Err(error) = run_gpstitch_render(&request, executor.as_ref()) {
                let status = if matches!(error, GpstitchWorkerError::InvalidConfiguration(_)) {
                    "blocked"
                } else {
                    "failed"
                };
                let _ = fail_gpstitch_render(&request.store, status, &bounded_error(&error));
            }
            if let Ok(mut active) = active_jobs.lock() {
                active.remove(&job_id);
            }
        });
        Ok(GpstitchStartResponse {
            render_id,
            job_id: response_job_id,
            status: "queued".to_string(),
        })
    }
}

fn run_gpstitch_render(
    request: &GpstitchWorkerRequest,
    executor: &dyn GpstitchExecutor,
) -> Result<(), GpstitchWorkerError> {
    let claimed = claim_gpstitch_render(&request.store)?;
    let proxy_path = fs::canonicalize(&claimed.proxy_path).map_err(|error| {
        GpstitchWorkerError::InvalidConfiguration(format!("proxy path: {error}"))
    })?;
    let route_path = fs::canonicalize(&claimed.route_path).map_err(|error| {
        GpstitchWorkerError::InvalidConfiguration(format!("route path: {error}"))
    })?;
    let mut claimed = claimed;
    claimed.route_path = route_path;
    let output_directory = claimed.output_path.parent().ok_or_else(|| {
        GpstitchWorkerError::InvalidConfiguration("output path has no parent".to_string())
    })?;
    fs::create_dir_all(output_directory)?;
    let staging_output = output_directory.join(format!(".{}.partial.mp4", request.store.render_id));
    let temporary_input = output_directory.join(format!(".{}.input.mp4", request.store.render_id));
    if staging_output.exists() || claimed.output_path.exists() || temporary_input.exists() {
        return Err(GpstitchWorkerError::InvalidOutput(
            "render staging or final path already exists".to_string(),
        ));
    }
    let input_path = if claimed.alignment == "gpx_timestamps" {
        proxy_path
    } else {
        fs::copy(&proxy_path, &temporary_input)?;
        set_alignment_time(&temporary_input, &claimed)?;
        temporary_input.clone()
    };
    let execution = executor.execute(request, &claimed, &input_path, &staging_output);
    if input_path == temporary_input {
        let _ = fs::remove_file(&temporary_input);
    }
    if let Err(error) = execution {
        let _ = fs::remove_file(&staging_output);
        return Err(error);
    }
    fs::rename(&staging_output, &claimed.output_path)?;
    complete_gpstitch_render(&request.store, PINNED_GPSTITCH_VERSION)?;
    Ok(())
}

fn validate_sidecar(request: &GpstitchWorkerRequest) -> Result<(), GpstitchWorkerError> {
    if !request.sidecar_directory.is_dir() {
        return Err(GpstitchWorkerError::InvalidConfiguration(
            "bundled GPStitch source and a prepared managed GPStitch environment are required"
                .to_string(),
        ));
    }
    probe_managed_environment(&request.environment_directory, "gpstitch-0.18.0").map_err(
        |error| {
            GpstitchWorkerError::InvalidConfiguration(format!(
                "managed GPStitch environment module probe failed: {error}"
            ))
        },
    )?;
    let pyproject = fs::read_to_string(request.sidecar_directory.join("pyproject.toml"))?;
    let license = fs::read_to_string(request.sidecar_directory.join("LICENSE"))?;
    if !pyproject.contains(&format!("version = \"{PINNED_GPSTITCH_VERSION}\""))
        || !request.sidecar_directory.join("uv.lock").is_file()
        || !license.contains("GNU GENERAL PUBLIC LICENSE")
    {
        return Err(GpstitchWorkerError::InvalidConfiguration(
            "GPStitch source/version/lock/license does not match the audited v0.18.0 boundary"
                .to_string(),
        ));
    }
    Ok(())
}

fn set_alignment_time(
    path: &Path,
    claimed: &ClaimedGpstitchRender,
) -> Result<(), GpstitchWorkerError> {
    let detected = DateTime::parse_from_rfc3339(&claimed.detected_start).map_err(|error| {
        GpstitchWorkerError::InvalidConfiguration(format!(
            "proxy detected-start timestamp is required for alignment: {error}"
        ))
    })?;
    let timestamp = detected
        .timestamp()
        .checked_add(claimed.time_offset_seconds)
        .ok_or_else(|| {
            GpstitchWorkerError::InvalidConfiguration("alignment timestamp overflow".to_string())
        })?;
    let system_time = if timestamp >= 0 {
        UNIX_EPOCH + Duration::from_secs(timestamp as u64)
    } else {
        UNIX_EPOCH
            .checked_sub(Duration::from_secs(timestamp.unsigned_abs()))
            .ok_or_else(|| {
                GpstitchWorkerError::InvalidConfiguration(
                    "alignment timestamp is before the system epoch".to_string(),
                )
            })?
    };
    File::options()
        .write(true)
        .open(path)?
        .set_times(FileTimes::new().set_modified(system_time))?;
    Ok(())
}

fn bounded_text(bytes: &[u8]) -> String {
    let value = String::from_utf8_lossy(bytes).trim().to_string();
    if value.is_empty() {
        "renderer returned a non-zero exit status".to_string()
    } else {
        value.chars().take(4_096).collect()
    }
}

fn overlay_font_path() -> Option<PathBuf> {
    let mut candidates = Vec::new();
    if let Some(windows) = std::env::var_os("WINDIR") {
        let fonts = PathBuf::from(windows).join("Fonts");
        candidates.push(fonts.join("arial.ttf"));
        candidates.push(fonts.join("segoeui.ttf"));
    }
    candidates.extend([
        PathBuf::from("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"),
        PathBuf::from("/System/Library/Fonts/Supplemental/Arial.ttf"),
    ]);
    candidates.into_iter().find(|path| path.is_file())
}

fn bounded_error(error: &GpstitchWorkerError) -> String {
    error.to_string().chars().take(4_096).collect()
}

impl From<std::io::Error> for GpstitchWorkerError {
    fn from(error: std::io::Error) -> Self {
        Self::Execution(error.to_string())
    }
}

#[cfg(test)]
mod tests {
    use super::{
        overlay_font_path, GpstitchExecutor, GpstitchWorkerError, GpstitchWorkerManager,
        GpstitchWorkerRequest, PINNED_GPSTITCH_VERSION,
    };
    use crate::project_store::{
        create_project_at, import_media_at, import_route_at, read_gpstitch_render_status,
        GpstitchRenderRequest, MediaImportRequest, ProjectCreateRequest, RouteImportRequest,
        RoutePoint,
    };
    use std::fs;
    use std::path::{Path, PathBuf};
    use std::sync::{Arc, Mutex};
    use std::thread;
    use std::time::{Duration, Instant};
    use uuid::Uuid;

    struct FakeExecutor {
        calls: Mutex<Vec<Vec<String>>>,
    }

    #[cfg(windows)]
    #[test]
    fn resolves_an_existing_windows_overlay_font() {
        assert!(overlay_font_path().is_some_and(|path| path.is_file()));
    }

    impl GpstitchExecutor for FakeExecutor {
        fn execute(
            &self,
            _request: &GpstitchWorkerRequest,
            claimed: &crate::project_store::ClaimedGpstitchRender,
            input_path: &Path,
            staging_output: &Path,
        ) -> Result<(), GpstitchWorkerError> {
            self.calls.lock().unwrap().push(vec![
                input_path.to_string_lossy().into_owned(),
                claimed.route_path.to_string_lossy().into_owned(),
                claimed.layout.clone(),
                claimed.alignment.clone(),
            ]);
            fs::write(staging_output, b"rendered-overlay")?;
            Ok(())
        }
    }

    #[test]
    fn manager_queues_and_publishes_confined_overlay_provenance() {
        let (_root, request) = fixture("gpx_timestamps", 0);
        let executor = Arc::new(FakeExecutor {
            calls: Mutex::new(Vec::new()),
        });
        let manager = GpstitchWorkerManager {
            active_jobs: Arc::new(Mutex::new(Default::default())),
            executor: executor.clone(),
        };
        let started = Instant::now();
        assert_eq!(manager.start(request.clone()).unwrap().status, "queued");
        assert!(started.elapsed() < Duration::from_millis(100));
        let status = wait_for_terminal(&request);
        assert_eq!(status.status, "complete");
        assert_eq!(status.gpstitch_version, PINNED_GPSTITCH_VERSION);
        assert_eq!(status.output_size_bytes, 16);
        assert_eq!(status.output_hash.len(), 64);
        assert!(status
            .output_path
            .ends_with(&format!("{}.mp4", request.store.render_id)));
        assert_eq!(executor.calls.lock().unwrap().len(), 1);
    }

    #[test]
    fn auto_alignment_uses_a_temporary_proxy_copy_and_cleans_it() {
        let (_root, request) = fixture("auto", 0);
        let executor = Arc::new(FakeExecutor {
            calls: Mutex::new(Vec::new()),
        });
        let manager = GpstitchWorkerManager {
            active_jobs: Arc::new(Mutex::new(Default::default())),
            executor: executor.clone(),
        };
        manager.start(request.clone()).unwrap();
        let status = wait_for_terminal(&request);
        assert_eq!(status.status, "complete");
        let input = PathBuf::from(&executor.calls.lock().unwrap()[0][0]);
        assert!(input.to_string_lossy().contains(".input.mp4"));
        assert!(!input.exists());
    }

    fn wait_for_terminal(
        request: &GpstitchWorkerRequest,
    ) -> crate::project_store::GpstitchRenderStatus {
        for _ in 0..200 {
            let status = read_gpstitch_render_status(&request.store).unwrap();
            if matches!(status.status.as_str(), "complete" | "failed" | "blocked") {
                return status;
            }
            thread::sleep(Duration::from_millis(10));
        }
        panic!("GPStitch job did not reach terminal state");
    }

    fn fixture(alignment: &str, offset: i64) -> (TestRoot, GpstitchWorkerRequest) {
        let root = TestRoot::new();
        let project_id = Uuid::new_v4();
        let created = create_project_at(
            ProjectCreateRequest {
                project_name: "GPStitch worker".to_string(),
                root_directory: root.path.clone(),
            },
            project_id,
            1_788_000_000,
        )
        .unwrap();
        let source = root.path.join("source.mp4");
        let proxy = root.path.join("proxy.mp4");
        let route = root.path.join("route.gpx");
        fs::write(&source, b"video").unwrap();
        fs::write(&proxy, b"proxy").unwrap();
        fs::write(&route, b"route").unwrap();
        let media_id = Uuid::new_v4();
        let route_id = Uuid::new_v4();
        import_media_at(
            MediaImportRequest {
                sqlite_path: PathBuf::from(&created.sqlite_path),
                project_id: project_id.to_string(),
                source_path: source,
            },
            media_id,
            Uuid::new_v4(),
        )
        .unwrap();
        import_route_at(
            RouteImportRequest {
                sqlite_path: PathBuf::from(&created.sqlite_path),
                project_id: project_id.to_string(),
                source_path: route,
                points: vec![
                    RoutePoint {
                        latitude: 43.0,
                        longitude: -79.0,
                        time_seconds: 0.0,
                    },
                    RoutePoint {
                        latitude: 43.1,
                        longitude: -79.1,
                        time_seconds: 10.0,
                    },
                ],
            },
            route_id,
            Uuid::new_v4(),
            1_788_000_001,
        )
        .unwrap();
        let connection = rusqlite::Connection::open(&created.sqlite_path).unwrap();
        connection
            .execute(
                "UPDATE media_assets SET proxy_status = 'ready', proxy_path = ?1,
             detected_start = '2026-07-10T12:00:00Z' WHERE id = ?2",
                rusqlite::params![proxy.to_string_lossy().into_owned(), media_id.to_string()],
            )
            .unwrap();
        let request = GpstitchWorkerRequest {
            store: GpstitchRenderRequest {
                sqlite_path: PathBuf::from(created.sqlite_path),
                project_id: project_id.to_string(),
                media_id: media_id.to_string(),
                route_id: route_id.to_string(),
                render_id: Uuid::new_v4().to_string(),
                job_id: Uuid::new_v4().to_string(),
                layout: "speed-awareness".to_string(),
                alignment: alignment.to_string(),
                time_offset_seconds: offset,
            },
            sidecar_directory: root.path.join("sidecar"),
            environment_directory: root.path.join("environment"),
        };
        (root, request)
    }

    struct TestRoot {
        path: PathBuf,
    }
    impl TestRoot {
        fn new() -> Self {
            let path =
                std::env::temp_dir().join(format!("roadwatcher-gpstitch-test-{}", Uuid::new_v4()));
            fs::create_dir_all(&path).unwrap();
            Self { path }
        }
    }
    impl Drop for TestRoot {
        fn drop(&mut self) {
            let _ = fs::remove_dir_all(&self.path);
        }
    }
}
