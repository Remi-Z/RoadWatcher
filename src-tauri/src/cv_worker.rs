use crate::bounded_process::{run_bounded_process, BoundedProcessError};
use crate::managed_runtime::{environment_python, probe_managed_environment};
use crate::project_store::{
    claim_cv_scan, complete_cv_scan, fail_cv_scan, queue_cv_scan, CvFinding, CvScanRequest,
    ProjectStoreError,
};
use serde::{Deserialize, Serialize};
use std::collections::HashSet;
use std::path::{Path, PathBuf};
use std::process::Command;
use std::sync::{Arc, Mutex};
use std::time::Duration;
use thiserror::Error;

const MAX_SIDECAR_OUTPUT_BYTES: u64 = 16 * 1024 * 1024;
const MAX_SCAN_DURATION: Duration = Duration::from_secs(4 * 60 * 60);

#[derive(Clone, Debug)]
pub struct CvWorkerRequest {
    pub store: CvScanRequest,
    pub sidecar_directory: PathBuf,
    pub environment_directory: PathBuf,
}

#[derive(Clone, Debug, Eq, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct CvStartResponse {
    pub scan_id: String,
    pub job_id: String,
    pub status: String,
    pub finding_count: i64,
    pub review_required: bool,
}

#[derive(Debug, Error)]
pub enum CvWorkerError {
    #[error("CV project store operation failed: {0}")]
    Store(#[from] ProjectStoreError),
    #[error("CV sidecar configuration is invalid: {0}")]
    InvalidConfiguration(String),
    #[error("CV sidecar execution failed: {0}")]
    Execution(String),
    #[error("CV sidecar output is invalid: {0}")]
    InvalidOutput(String),
}

trait CvExecutor: Send + Sync {
    fn execute(
        &self,
        request: &CvWorkerRequest,
        source_path: &Path,
    ) -> Result<String, CvWorkerError>;
}

struct ProcessCvExecutor;

impl CvExecutor for ProcessCvExecutor {
    fn execute(
        &self,
        request: &CvWorkerRequest,
        source_path: &Path,
    ) -> Result<String, CvWorkerError> {
        if !request.sidecar_directory.is_dir() {
            return Err(CvWorkerError::InvalidConfiguration(
                "bundled CV source and a prepared managed CV environment are required".to_string(),
            ));
        }
        probe_managed_environment(&request.environment_directory, "roadwatcher-cv-0.1.0").map_err(
            |error| {
                CvWorkerError::InvalidConfiguration(format!(
                    "managed CV environment module probe failed: {error}"
                ))
            },
        )?;
        // uv's Windows script launchers retain the absolute staging path after a
        // managed environment is atomically promoted. The environment's Python
        // interpreter is relocatable and avoids that stale launcher boundary.
        let mut command = Command::new(environment_python(&request.environment_directory));
        command
            .arg("-m")
            .arg("roadwatcher_cv.cli")
            .arg("scan")
            .arg("--source")
            .arg(source_path)
            .arg("--model")
            .arg(&request.store.model_path)
            .arg("--labels")
            .arg(&request.store.labels_path)
            .arg("--confidence")
            .arg(request.store.confidence_threshold.to_string())
            .arg("--interval-seconds")
            .arg(request.store.sample_interval_seconds.to_string())
            .arg("--max-findings")
            .arg(request.store.max_findings.to_string());
        let output = run_bounded_process(&mut command, MAX_SCAN_DURATION, MAX_SIDECAR_OUTPUT_BYTES)
            .map_err(map_process_error)?;
        if !output.status.success() {
            return Err(CvWorkerError::Execution(bounded_text(&output.stderr)));
        }
        String::from_utf8(output.stdout)
            .map_err(|error| CvWorkerError::InvalidOutput(format!("stdout is not UTF-8: {error}")))
    }
}

fn map_process_error(error: BoundedProcessError) -> CvWorkerError {
    match error {
        BoundedProcessError::OutputLimit { .. } => {
            CvWorkerError::InvalidOutput("sidecar output exceeded 16 MiB".to_string())
        }
        other => CvWorkerError::Execution(other.to_string()),
    }
}

pub struct CvWorkerManager {
    active_jobs: Arc<Mutex<HashSet<String>>>,
    executor: Arc<dyn CvExecutor>,
}

impl Default for CvWorkerManager {
    fn default() -> Self {
        Self {
            active_jobs: Arc::new(Mutex::new(HashSet::new())),
            executor: Arc::new(ProcessCvExecutor),
        }
    }
}

impl CvWorkerManager {
    pub fn start(&self, request: CvWorkerRequest) -> Result<CvStartResponse, CvWorkerError> {
        queue_cv_scan(&request.store)?;
        let job_id = request.store.job_id.clone();
        let response_job_id = job_id.clone();
        let scan_id = request.store.scan_id.clone();
        {
            let mut active = self.active_jobs.lock().map_err(|_| {
                CvWorkerError::Execution("CV worker state lock is poisoned".to_string())
            })?;
            if !active.insert(job_id.clone()) {
                return Err(CvWorkerError::InvalidConfiguration(
                    "CV job is already active".to_string(),
                ));
            }
        }
        let executor = Arc::clone(&self.executor);
        let active_jobs = Arc::clone(&self.active_jobs);
        std::thread::spawn(move || {
            let result = run_cv_scan(&request, executor.as_ref());
            if let Err(error) = result {
                let status = if matches!(
                    error,
                    CvWorkerError::InvalidConfiguration(_) | CvWorkerError::Execution(_)
                ) {
                    "blocked"
                } else {
                    "failed"
                };
                let _ = fail_cv_scan(&request.store, status, &bounded_error(&error));
            }
            if let Ok(mut active) = active_jobs.lock() {
                active.remove(&job_id);
            }
        });
        Ok(CvStartResponse {
            scan_id,
            job_id: response_job_id,
            status: "queued".to_string(),
            finding_count: 0,
            review_required: false,
        })
    }
}

fn run_cv_scan(request: &CvWorkerRequest, executor: &dyn CvExecutor) -> Result<(), CvWorkerError> {
    let claimed = claim_cv_scan(&request.store)?;
    let source_path = std::fs::canonicalize(&claimed.source_path)
        .map_err(|error| CvWorkerError::InvalidConfiguration(format!("source path: {error}")))?;
    let model_path = std::fs::canonicalize(&claimed.model_path)
        .map_err(|error| CvWorkerError::InvalidConfiguration(format!("model path: {error}")))?;
    let labels_path = std::fs::canonicalize(&claimed.labels_path)
        .map_err(|error| CvWorkerError::InvalidConfiguration(format!("labels path: {error}")))?;
    let output = executor.execute(request, &source_path)?;
    let parsed: SidecarResult = serde_json::from_str(&output)
        .map_err(|error| CvWorkerError::InvalidOutput(format!("JSON: {error}")))?;
    if parsed.status != "complete"
        || !same_canonical_path(&parsed.source_path, &source_path)
        || !same_canonical_path(&parsed.model_path, &model_path)
        || !same_canonical_path(&parsed.labels_path, &labels_path)
        || parsed.finding_count != parsed.findings.len() as i64
        || parsed.review_required != !parsed.findings.is_empty()
    {
        return Err(CvWorkerError::InvalidOutput(
            "sidecar identity or aggregate metadata does not match the claimed scan".to_string(),
        ));
    }
    let findings = parsed
        .findings
        .into_iter()
        .enumerate()
        .map(|(sequence, finding)| finding.into_store(&request.store.scan_id, sequence))
        .collect::<Vec<_>>();
    complete_cv_scan(&request.store, &parsed.engine, &output, &findings)?;
    Ok(())
}

fn same_canonical_path(value: &str, expected: &Path) -> bool {
    std::fs::canonicalize(value).is_ok_and(|path| path == expected)
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct SidecarResult {
    status: String,
    engine: String,
    source_path: String,
    model_path: String,
    labels_path: String,
    finding_count: i64,
    review_required: bool,
    findings: Vec<SidecarFinding>,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct SidecarFinding {
    label: String,
    confidence: f64,
    time_seconds: f64,
    x: f64,
    y: f64,
    width: f64,
    height: f64,
    frame_width: i64,
    frame_height: i64,
}

impl SidecarFinding {
    fn into_store(self, scan_id: &str, sequence: usize) -> CvFinding {
        CvFinding {
            id: format!("{scan_id}:{sequence}"),
            label: self.label,
            confidence: self.confidence,
            time_seconds: self.time_seconds,
            x: self.x,
            y: self.y,
            width: self.width,
            height: self.height,
            frame_width: self.frame_width,
            frame_height: self.frame_height,
            review_status: "needs_review".to_string(),
            review_note: String::new(),
        }
    }
}

fn bounded_text(bytes: &[u8]) -> String {
    String::from_utf8_lossy(&bytes[..bytes.len().min(4096)])
        .trim()
        .to_string()
}

fn bounded_error(error: &CvWorkerError) -> String {
    error.to_string().chars().take(4096).collect()
}

#[cfg(test)]
mod tests {
    use super::{CvExecutor, CvWorkerError, CvWorkerManager, CvWorkerRequest};
    use crate::project_store::{
        create_project_at, import_media_at, read_cv_scan_status, CvScanRequest, MediaImportRequest,
        ProjectCreateRequest,
    };
    use serde_json::json;
    use std::fs;
    use std::path::{Path, PathBuf};
    use std::sync::{Arc, Mutex};
    use std::time::{Duration, Instant};
    use uuid::Uuid;

    struct FakeExecutor {
        invalid_identity: bool,
        calls: Mutex<usize>,
    }

    impl CvExecutor for FakeExecutor {
        fn execute(
            &self,
            request: &CvWorkerRequest,
            source_path: &Path,
        ) -> Result<String, CvWorkerError> {
            *self.calls.lock().unwrap() += 1;
            Ok(json!({
                "status": "complete",
                "engine": "onnxruntime-cpu",
                "sourcePath": if self.invalid_identity { "wrong.mp4".to_string() } else { source_path.to_string_lossy().into_owned() },
                "modelPath": fs::canonicalize(&request.store.model_path).unwrap().to_string_lossy().into_owned(),
                "labelsPath": fs::canonicalize(&request.store.labels_path).unwrap().to_string_lossy().into_owned(),
                "findingCount": 1,
                "reviewRequired": true,
                "findings": [{
                    "label": "car", "confidence": 0.9, "timeSeconds": 2.0,
                    "x": 10.0, "y": 20.0, "width": 30.0, "height": 40.0,
                    "frameWidth": 1920, "frameHeight": 1080
                }]
            }).to_string())
        }
    }

    #[test]
    fn manager_returns_promptly_and_publishes_validated_findings_in_background() {
        let (_root, request) = fixture();
        let executor = Arc::new(FakeExecutor {
            invalid_identity: false,
            calls: Mutex::new(0),
        });
        let manager = CvWorkerManager {
            active_jobs: Arc::new(Mutex::new(Default::default())),
            executor: executor.clone(),
        };
        let started_at = Instant::now();
        let response = manager.start(request.clone()).unwrap();
        assert!(started_at.elapsed() < Duration::from_millis(100));
        assert_eq!(response.status, "queued");
        let status = wait_for_terminal(&request);
        assert_eq!(status.status, "complete");
        assert_eq!(status.finding_count, 1);
        assert_eq!(status.findings[0].label, "car");
        assert_eq!(*executor.calls.lock().unwrap(), 1);
    }

    #[test]
    fn identity_mismatch_fails_without_publishing_findings() {
        let (_root, request) = fixture();
        let manager = CvWorkerManager {
            active_jobs: Arc::new(Mutex::new(Default::default())),
            executor: Arc::new(FakeExecutor {
                invalid_identity: true,
                calls: Mutex::new(0),
            }),
        };
        manager.start(request.clone()).unwrap();
        let status = wait_for_terminal(&request);
        assert_eq!(status.status, "failed");
        assert!(status.findings.is_empty());
        assert!(status.detail.contains("identity"));
    }

    fn wait_for_terminal(request: &CvWorkerRequest) -> crate::project_store::CvScanStatus {
        for _ in 0..100 {
            let status = read_cv_scan_status(&request.store).unwrap();
            if matches!(status.status.as_str(), "complete" | "failed" | "blocked") {
                return status;
            }
            std::thread::sleep(Duration::from_millis(10));
        }
        panic!("CV job did not reach terminal state");
    }

    fn fixture() -> (TestRoot, CvWorkerRequest) {
        let root = TestRoot::new();
        let project_id = Uuid::new_v4();
        let created = create_project_at(
            ProjectCreateRequest {
                project_name: "CV worker".to_string(),
                root_directory: root.path.clone(),
            },
            project_id,
            1_788_000_000,
        )
        .unwrap();
        let source = root.path.join("source.mp4");
        let model = root.path.join("model.onnx");
        let labels = root.path.join("labels.txt");
        fs::write(&source, b"video").unwrap();
        fs::write(&model, b"onnx").unwrap();
        fs::write(&labels, b"car\n").unwrap();
        let media_id = Uuid::new_v4();
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
        let request = CvWorkerRequest {
            store: CvScanRequest {
                sqlite_path: PathBuf::from(created.sqlite_path),
                project_id: project_id.to_string(),
                media_id: media_id.to_string(),
                scan_id: Uuid::new_v4().to_string(),
                job_id: Uuid::new_v4().to_string(),
                model_path: model,
                labels_path: labels,
                confidence_threshold: 0.5,
                sample_interval_seconds: 1.0,
                max_findings: 500,
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
                std::env::temp_dir().join(format!("roadwatcher-cv-worker-test-{}", Uuid::new_v4()));
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
