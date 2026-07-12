use crate::bounded_process::run_bounded_process;
use serde::Serialize;
use std::fs;
use std::path::{Path, PathBuf};
use std::process::Command;
use std::sync::Arc;
use std::thread;
use std::time::{Duration, SystemTime, UNIX_EPOCH};

const PREPARE_TIMEOUT: Duration = Duration::from_secs(20 * 60);
const PREPARE_OUTPUT_LIMIT: u64 = 16 * 1024 * 1024;
const MANAGED_MARKER: &str = ".roadwatcher-managed-environment";

#[derive(Clone, Debug)]
pub struct ManagedEnvironmentPaths {
    pub root: PathBuf,
    pub gpstitch: PathBuf,
    pub cv: PathBuf,
}

#[derive(Clone, Debug)]
pub struct RuntimePrepareRequest {
    pub uv_executable: String,
    pub gpstitch_source: PathBuf,
    pub cv_source: PathBuf,
    pub environments: ManagedEnvironmentPaths,
}

#[derive(Clone, Debug, Eq, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct RuntimeEnvironmentPreparation {
    pub id: String,
    pub status: String,
    pub environment_path: String,
    pub detail: String,
}

#[derive(Clone, Debug, Eq, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct RuntimePrepareResponse {
    pub prepared_at_unix: u64,
    pub status: String,
    pub environments: Vec<RuntimeEnvironmentPreparation>,
}

trait EnvironmentSyncExecutor: Send + Sync {
    fn sync(&self, uv_executable: &str, source: &Path, staging: &Path) -> Result<String, String>;
}

struct ProcessEnvironmentSyncExecutor;

impl EnvironmentSyncExecutor for ProcessEnvironmentSyncExecutor {
    fn sync(&self, uv_executable: &str, source: &Path, staging: &Path) -> Result<String, String> {
        let mut command = Command::new(uv_executable);
        command
            .arg("sync")
            .arg("--locked")
            .arg("--no-dev")
            .arg("--project")
            .arg(source)
            .env("UV_PROJECT_ENVIRONMENT", staging);
        let output = run_bounded_process(&mut command, PREPARE_TIMEOUT, PREPARE_OUTPUT_LIMIT)
            .map_err(|error| error.to_string())?;
        let detail = bounded_detail(&output.stderr, &output.stdout);
        if output.status.success() {
            Ok(if detail.is_empty() {
                "locked environment synchronized".to_string()
            } else {
                detail
            })
        } else {
            Err(if detail.is_empty() {
                "uv sync returned a non-zero exit status".to_string()
            } else {
                detail
            })
        }
    }
}

pub fn managed_environment_paths(app_local_data: &Path) -> ManagedEnvironmentPaths {
    let root = app_local_data.join("sidecar-environments");
    ManagedEnvironmentPaths {
        gpstitch: root.join("gpstitch-0.18.0"),
        cv: root.join("roadwatcher-cv-0.1.0"),
        root,
    }
}

pub fn prepare_runtime_environments(request: RuntimePrepareRequest) -> RuntimePrepareResponse {
    prepare_with_executor(request, Arc::new(ProcessEnvironmentSyncExecutor))
}

fn prepare_with_executor(
    request: RuntimePrepareRequest,
    executor: Arc<dyn EnvironmentSyncExecutor>,
) -> RuntimePrepareResponse {
    let specs = [
        (
            "gpstitch-environment",
            "gpstitch-0.18.0",
            request.gpstitch_source,
            request.environments.gpstitch,
        ),
        (
            "cv-environment",
            "roadwatcher-cv-0.1.0",
            request.cv_source,
            request.environments.cv,
        ),
    ];
    let handles = specs
        .into_iter()
        .map(|(id, marker, source, target)| {
            let executor = Arc::clone(&executor);
            let uv = request.uv_executable.clone();
            let root = request.environments.root.clone();
            thread::spawn(move || {
                prepare_one(id, marker, &uv, &source, &root, &target, executor.as_ref())
            })
        })
        .collect::<Vec<_>>();
    let environments = handles
        .into_iter()
        .map(|handle| {
            handle
                .join()
                .unwrap_or_else(|_| RuntimeEnvironmentPreparation {
                    id: "preparation-thread".to_string(),
                    status: "failed".to_string(),
                    environment_path: String::new(),
                    detail: "environment preparation thread failed".to_string(),
                })
        })
        .collect::<Vec<_>>();
    RuntimePrepareResponse {
        prepared_at_unix: SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .unwrap_or_default()
            .as_secs(),
        status: if environments.iter().all(|item| item.status == "ready") {
            "ready"
        } else {
            "incomplete"
        }
        .to_string(),
        environments,
    }
}

fn prepare_one(
    id: &str,
    marker: &str,
    uv_executable: &str,
    source: &Path,
    root: &Path,
    target: &Path,
    executor: &dyn EnvironmentSyncExecutor,
) -> RuntimeEnvironmentPreparation {
    let result = (|| -> Result<String, String> {
        fs::create_dir_all(root).map_err(|error| error.to_string())?;
        if environment_ready(target, marker) {
            return Ok("managed environment is already ready".to_string());
        }
        if target.exists()
            && !matches!(
                fs::read_to_string(target.join(MANAGED_MARKER)),
                Ok(value) if value == marker
            )
        {
            return Err(
                "target exists without the expected RoadWatcher ownership marker".to_string(),
            );
        }
        let nonce = uuid::Uuid::new_v4();
        let staging = root.join(format!(".{marker}.staging-{nonce}"));
        let quarantined = root.join(format!(".{marker}.invalid-{nonce}"));
        if staging.exists() || quarantined.exists() {
            return Err("generated environment staging path already exists".to_string());
        }
        let sync_detail = match executor.sync(uv_executable, source, &staging) {
            Ok(detail) => detail,
            Err(error) => {
                let _ = remove_owned_staging(root, &staging);
                return Err(error);
            }
        };
        if let Err(error) = fs::write(staging.join(MANAGED_MARKER), marker) {
            let _ = remove_owned_staging(root, &staging);
            return Err(error.to_string());
        }
        if !environment_ready(&staging, marker) {
            let _ = remove_owned_staging(root, &staging);
            return Err("uv sync did not produce the expected managed environment".to_string());
        }
        let had_target = target.exists();
        if had_target {
            fs::rename(target, &quarantined).map_err(|error| error.to_string())?;
        }
        if let Err(error) = fs::rename(&staging, target) {
            if had_target && !target.exists() {
                let _ = fs::rename(&quarantined, target);
            }
            let _ = remove_owned_staging(root, &staging);
            return Err(error.to_string());
        }
        if had_target {
            let _ = remove_owned_staging(root, &quarantined);
        }
        Ok(sync_detail)
    })();
    RuntimeEnvironmentPreparation {
        id: id.to_string(),
        status: if result.is_ok() { "ready" } else { "failed" }.to_string(),
        environment_path: target.to_string_lossy().into_owned(),
        detail: result.unwrap_or_else(|error| error.chars().take(2048).collect()),
    }
}

pub fn environment_ready(environment: &Path, marker: &str) -> bool {
    fs::read_to_string(environment.join(MANAGED_MARKER)).is_ok_and(|value| value == marker)
        && environment.join("pyvenv.cfg").is_file()
        && environment.join(python_relative_path()).is_file()
        && environment.join(entrypoint_relative_path(marker)).is_file()
}

pub fn environment_python(environment: &Path) -> PathBuf {
    environment.join(python_relative_path())
}

fn python_relative_path() -> PathBuf {
    if cfg!(windows) {
        PathBuf::from("Scripts/python.exe")
    } else {
        PathBuf::from("bin/python")
    }
}

fn entrypoint_relative_path(marker: &str) -> PathBuf {
    let command = if marker.starts_with("gpstitch-") {
        "gpstitch-dashboard"
    } else {
        "roadwatcher-cv"
    };
    if cfg!(windows) {
        PathBuf::from(format!("Scripts/{command}.exe"))
    } else {
        PathBuf::from(format!("bin/{command}"))
    }
}

fn remove_owned_staging(root: &Path, candidate: &Path) -> Result<(), String> {
    if candidate.parent() != Some(root)
        || !candidate
            .file_name()
            .is_some_and(|name| name.to_string_lossy().starts_with('.'))
    {
        return Err("refused to remove an unconfined environment staging path".to_string());
    }
    if candidate.exists() {
        fs::remove_dir_all(candidate).map_err(|error| error.to_string())?;
    }
    Ok(())
}

fn bounded_detail(stderr: &[u8], stdout: &[u8]) -> String {
    let bytes = if stderr.is_empty() { stdout } else { stderr };
    String::from_utf8_lossy(bytes)
        .trim()
        .chars()
        .take(2048)
        .collect()
}

#[cfg(test)]
mod tests {
    use super::{
        environment_ready, managed_environment_paths, prepare_with_executor,
        EnvironmentSyncExecutor, RuntimePrepareRequest,
    };
    use std::fs;
    use std::path::Path;
    use std::sync::Arc;

    struct FakeSync;
    impl EnvironmentSyncExecutor for FakeSync {
        fn sync(&self, _uv: &str, _source: &Path, staging: &Path) -> Result<String, String> {
            fs::create_dir_all(staging.join(if cfg!(windows) { "Scripts" } else { "bin" }))
                .unwrap();
            fs::write(staging.join("pyvenv.cfg"), "home=test").unwrap();
            fs::write(
                staging.join(if cfg!(windows) {
                    "Scripts/python.exe"
                } else {
                    "bin/python"
                }),
                "python",
            )
            .unwrap();
            let command = if staging.to_string_lossy().contains("gpstitch") {
                "gpstitch-dashboard"
            } else {
                "roadwatcher-cv"
            };
            fs::write(
                staging.join(if cfg!(windows) {
                    format!("Scripts/{command}.exe")
                } else {
                    format!("bin/{command}")
                }),
                "command",
            )
            .unwrap();
            Ok("prepared".to_string())
        }
    }

    #[test]
    fn prepares_versioned_owned_environments_and_reuses_ready_results() {
        let root = std::env::temp_dir().join(format!(
            "roadwatcher-managed-runtime-{}",
            uuid::Uuid::new_v4()
        ));
        let paths = managed_environment_paths(&root);
        let request = RuntimePrepareRequest {
            uv_executable: "uv".to_string(),
            gpstitch_source: root.join("gp-source"),
            cv_source: root.join("cv-source"),
            environments: paths.clone(),
        };
        let first = prepare_with_executor(request.clone(), Arc::new(FakeSync));
        assert_eq!(first.status, "ready");
        assert!(environment_ready(&paths.gpstitch, "gpstitch-0.18.0"));
        assert!(environment_ready(&paths.cv, "roadwatcher-cv-0.1.0"));
        let second = prepare_with_executor(request, Arc::new(FakeSync));
        assert!(second
            .environments
            .iter()
            .all(|item| item.detail.contains("already ready")));
        fs::remove_dir_all(root).unwrap();
    }

    #[test]
    fn refuses_to_replace_an_unowned_environment_directory() {
        let root = std::env::temp_dir().join(format!(
            "roadwatcher-unowned-runtime-{}",
            uuid::Uuid::new_v4()
        ));
        let paths = managed_environment_paths(&root);
        fs::create_dir_all(&paths.gpstitch).unwrap();
        fs::write(paths.gpstitch.join("foreign.txt"), "keep").unwrap();
        fs::write(
            paths.gpstitch.join(".roadwatcher-managed-environment"),
            "some-other-environment",
        )
        .unwrap();
        let response = prepare_with_executor(
            RuntimePrepareRequest {
                uv_executable: "uv".to_string(),
                gpstitch_source: root.join("gp-source"),
                cv_source: root.join("cv-source"),
                environments: paths,
            },
            Arc::new(FakeSync),
        );
        assert_eq!(response.status, "incomplete");
        assert!(response
            .environments
            .iter()
            .any(|item| item.id == "gpstitch-environment"
                && item.detail.contains("ownership marker")));
        assert!(root
            .join("sidecar-environments/gpstitch-0.18.0/foreign.txt")
            .is_file());
        let _ = fs::remove_dir_all(root);
    }

    #[test]
    #[ignore = "requires installed uv and may populate its dependency cache"]
    fn real_uv_smoke_prepares_both_locked_environments() {
        let root = std::env::temp_dir().join(format!(
            "roadwatcher-real-managed-runtime-{}",
            uuid::Uuid::new_v4()
        ));
        let paths = managed_environment_paths(&root);
        let response = super::prepare_runtime_environments(RuntimePrepareRequest {
            uv_executable: "uv".to_string(),
            gpstitch_source: Path::new("../sidecars/roadwatcher-gpstitch")
                .canonicalize()
                .unwrap(),
            cv_source: Path::new("../sidecars/roadwatcher-cv")
                .canonicalize()
                .unwrap(),
            environments: paths.clone(),
        });
        assert_eq!(response.status, "ready", "{response:?}");
        assert!(environment_ready(&paths.gpstitch, "gpstitch-0.18.0"));
        assert!(environment_ready(&paths.cv, "roadwatcher-cv-0.1.0"));
        fs::remove_dir_all(root).unwrap();
    }
}
