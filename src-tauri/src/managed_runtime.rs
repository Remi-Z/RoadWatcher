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
const PROBE_TIMEOUT: Duration = Duration::from_secs(30);
const PROBE_OUTPUT_LIMIT: u64 = 64 * 1024;
const MANAGED_MARKER: &str = ".roadwatcher-managed-environment";
const MANAGED_PYTHON_VERSION: &str = "3.12.13";

#[derive(Clone, Debug)]
pub struct ManagedEnvironmentPaths {
    pub root: PathBuf,
    pub gpstitch: PathBuf,
    pub cv: PathBuf,
    pub python_install_root: PathBuf,
    pub uv_cache_root: PathBuf,
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
    fn sync(
        &self,
        uv_executable: &str,
        source: &Path,
        staging: &Path,
        python_install_root: &Path,
        uv_cache_root: &Path,
    ) -> Result<String, String>;
    fn validate(&self, environment: &Path, marker: &str) -> Result<String, String>;
}

struct ProcessEnvironmentSyncExecutor;

impl EnvironmentSyncExecutor for ProcessEnvironmentSyncExecutor {
    fn sync(
        &self,
        uv_executable: &str,
        source: &Path,
        staging: &Path,
        python_install_root: &Path,
        uv_cache_root: &Path,
    ) -> Result<String, String> {
        let mut command = sync_command(
            uv_executable,
            source,
            staging,
            python_install_root,
            uv_cache_root,
        );
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

    fn validate(&self, environment: &Path, marker: &str) -> Result<String, String> {
        probe_managed_environment(environment, marker)
    }
}

pub fn managed_environment_paths(app_local_data: &Path) -> ManagedEnvironmentPaths {
    let root = app_local_data.join("sidecar-environments");
    ManagedEnvironmentPaths {
        gpstitch: root.join("gpstitch-0.18.0"),
        cv: root.join("roadwatcher-cv-0.1.0"),
        root,
        python_install_root: app_local_data
            .join("python-installations")
            .join(MANAGED_PYTHON_VERSION),
        uv_cache_root: app_local_data.join("uv-cache"),
    }
}

fn sync_command(
    uv_executable: &str,
    source: &Path,
    staging: &Path,
    python_install_root: &Path,
    uv_cache_root: &Path,
) -> Command {
    let mut command = Command::new(uv_executable);
    command
        .arg("sync")
        .arg("--locked")
        .arg("--no-dev")
        .arg("--python")
        .arg(MANAGED_PYTHON_VERSION)
        .arg("--managed-python")
        .arg("--link-mode")
        .arg("copy")
        .arg("--no-progress")
        .arg("--project")
        .arg(source)
        .env("UV_PROJECT_ENVIRONMENT", staging)
        .env("UV_PYTHON_INSTALL_DIR", python_install_root)
        .env("UV_CACHE_DIR", uv_cache_root);
    command
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
            let python_install_root = request.environments.python_install_root.clone();
            let uv_cache_root = request.environments.uv_cache_root.clone();
            thread::spawn(move || {
                prepare_one(
                    id,
                    marker,
                    &uv,
                    &source,
                    &root,
                    &target,
                    &python_install_root,
                    &uv_cache_root,
                    executor.as_ref(),
                )
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
    python_install_root: &Path,
    uv_cache_root: &Path,
    executor: &dyn EnvironmentSyncExecutor,
) -> RuntimeEnvironmentPreparation {
    let result = (|| -> Result<String, String> {
        fs::create_dir_all(root).map_err(|error| error.to_string())?;
        fs::create_dir_all(python_install_root).map_err(|error| error.to_string())?;
        fs::create_dir_all(uv_cache_root).map_err(|error| error.to_string())?;
        if environment_structure_ready(target, marker) {
            if let Ok(detail) = executor.validate(target, marker) {
                return Ok(format!("managed environment is already ready; {detail}"));
            }
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
        let sync_detail = match executor.sync(
            uv_executable,
            source,
            &staging,
            python_install_root,
            uv_cache_root,
        ) {
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
        if !environment_structure_ready(&staging, marker) {
            let _ = remove_owned_staging(root, &staging);
            return Err("uv sync did not produce the expected managed environment".to_string());
        }
        if let Err(error) = executor.validate(&staging, marker) {
            let _ = remove_owned_staging(root, &staging);
            return Err(format!("staged environment module probe failed: {error}"));
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
        if let Err(error) = executor.validate(target, marker) {
            let rollback =
                rollback_failed_promotion(root, target, &staging, &quarantined, had_target);
            return Err(match rollback {
                Ok(()) if had_target => format!(
                    "promoted environment module probe failed; previous environment restored: {error}"
                ),
                Ok(()) => format!(
                    "promoted environment module probe failed; invalid environment removed: {error}"
                ),
                Err(rollback_error) => format!(
                    "promoted environment module probe failed: {error}; rollback failed: {rollback_error}"
                ),
            });
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

pub(crate) fn environment_structure_ready(environment: &Path, marker: &str) -> bool {
    environment_probe_code(marker).is_some()
        && fs::read_to_string(environment.join(MANAGED_MARKER)).is_ok_and(|value| value == marker)
        && environment.join("pyvenv.cfg").is_file()
        && environment.join(python_relative_path()).is_file()
}

pub(crate) fn environment_python(environment: &Path) -> PathBuf {
    environment.join(python_relative_path())
}

pub(crate) fn environment_probe_code(marker: &str) -> Option<&'static str> {
    match marker {
        "gpstitch-0.18.0" => Some(
            "import platform; from importlib.metadata import version; import gpstitch; print(version('gpstitch')); print(platform.python_version())",
        ),
        "roadwatcher-cv-0.1.0" => Some(
            "import platform; from importlib.metadata import version; import roadwatcher_cv; print(version('roadwatcher-cv')); print(platform.python_version())",
        ),
        _ => None,
    }
}

pub(crate) fn environment_version(marker: &str) -> Option<&'static str> {
    match marker {
        "gpstitch-0.18.0" => Some("0.18.0"),
        "roadwatcher-cv-0.1.0" => Some("0.1.0"),
        _ => None,
    }
}

pub(crate) fn probe_managed_environment(
    environment: &Path,
    marker: &str,
) -> Result<String, String> {
    if !environment_structure_ready(environment, marker) {
        return Err("managed environment structure or ownership marker is invalid".to_string());
    }
    let code = environment_probe_code(marker)
        .ok_or_else(|| "managed environment identity is unsupported".to_string())?;
    let mut command = Command::new(environment_python(environment));
    command.arg("-c").arg(code);
    let output = run_bounded_process(&mut command, PROBE_TIMEOUT, PROBE_OUTPUT_LIMIT)
        .map_err(|error| error.to_string())?;
    if !output.status.success() {
        let detail = bounded_detail(&output.stderr, &output.stdout);
        return Err(if detail.is_empty() {
            "managed Python module probe returned a non-zero exit status".to_string()
        } else {
            detail
        });
    }
    validate_environment_probe_output(&output.stdout, marker)
}

fn validate_environment_probe_output(output: &[u8], marker: &str) -> Result<String, String> {
    let lines = nonblank_lines(output);
    let expected = environment_version(marker).unwrap_or_default();
    let actual = lines.first().map(String::as_str).unwrap_or_default();
    if actual != expected {
        return Err(format!(
            "managed Python module probe returned version {actual:?}; expected {expected}"
        ));
    }
    let actual_python = lines.get(1).map(String::as_str).unwrap_or_default();
    if actual_python != MANAGED_PYTHON_VERSION {
        return Err(format!(
            "managed Python probe returned version {actual_python:?}; expected {MANAGED_PYTHON_VERSION}"
        ));
    }
    Ok(format!(
        "managed Python {actual_python}; module {expected} probe succeeded"
    ))
}

fn python_relative_path() -> PathBuf {
    if cfg!(windows) {
        PathBuf::from("Scripts/python.exe")
    } else {
        PathBuf::from("bin/python")
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

fn rollback_failed_promotion(
    root: &Path,
    target: &Path,
    staging: &Path,
    quarantined: &Path,
    had_target: bool,
) -> Result<(), String> {
    fs::rename(target, staging)
        .map_err(|error| format!("could not quarantine failed promoted environment: {error}"))?;
    if had_target {
        if let Err(error) = fs::rename(quarantined, target) {
            let _ = fs::rename(staging, target);
            return Err(format!("could not restore previous environment: {error}"));
        }
    }
    remove_owned_staging(root, staging)
}

fn bounded_detail(stderr: &[u8], stdout: &[u8]) -> String {
    let bytes = if stderr.is_empty() { stdout } else { stderr };
    String::from_utf8_lossy(bytes)
        .trim()
        .chars()
        .take(2048)
        .collect()
}

fn nonblank_lines(bytes: &[u8]) -> Vec<String> {
    String::from_utf8_lossy(bytes)
        .lines()
        .map(str::trim)
        .filter(|line| !line.is_empty())
        .take(3)
        .map(|line| line.chars().take(512).collect())
        .collect()
}

#[cfg(test)]
mod tests {
    use super::{
        environment_structure_ready, managed_environment_paths, prepare_with_executor,
        sync_command, validate_environment_probe_output, EnvironmentSyncExecutor,
        RuntimePrepareRequest,
    };
    use std::fs;
    use std::path::Path;
    use std::sync::Arc;

    struct FakeSync;
    impl EnvironmentSyncExecutor for FakeSync {
        fn sync(
            &self,
            _uv: &str,
            _source: &Path,
            staging: &Path,
            _python_install_root: &Path,
            _uv_cache_root: &Path,
        ) -> Result<String, String> {
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
            Ok("prepared".to_string())
        }

        fn validate(&self, _environment: &Path, marker: &str) -> Result<String, String> {
            Ok(format!("{marker} module probe succeeded"))
        }
    }

    struct FailAfterPromotion;
    impl EnvironmentSyncExecutor for FailAfterPromotion {
        fn sync(
            &self,
            uv: &str,
            source: &Path,
            staging: &Path,
            python_install_root: &Path,
            uv_cache_root: &Path,
        ) -> Result<String, String> {
            FakeSync.sync(uv, source, staging, python_install_root, uv_cache_root)
        }

        fn validate(&self, environment: &Path, marker: &str) -> Result<String, String> {
            if environment
                .file_name()
                .is_some_and(|name| name.to_string_lossy().starts_with('.'))
            {
                Ok(format!("{marker} staged module probe succeeded"))
            } else {
                Err("simulated post-promotion import failure".to_string())
            }
        }
    }

    #[test]
    fn pins_managed_python_and_app_local_uv_storage() {
        let root = Path::new("C:/RoadWatcher/AppData");
        let paths = managed_environment_paths(root);
        let command = sync_command(
            "uv.exe",
            Path::new("C:/RoadWatcher/resources/sidecar"),
            Path::new("C:/RoadWatcher/AppData/staging"),
            &paths.python_install_root,
            &paths.uv_cache_root,
        );
        let arguments = command
            .get_args()
            .map(|value| value.to_string_lossy().into_owned())
            .collect::<Vec<_>>();
        assert!(arguments
            .windows(2)
            .any(|pair| pair == ["--python", "3.12.13"]));
        assert!(arguments.contains(&"--managed-python".to_string()));
        assert!(arguments
            .windows(2)
            .any(|pair| pair == ["--link-mode", "copy"]));
        let environment = command
            .get_envs()
            .filter_map(|(key, value)| {
                value.map(|item| (key.to_string_lossy(), item.to_string_lossy()))
            })
            .collect::<std::collections::HashMap<_, _>>();
        assert_eq!(
            environment
                .get("UV_PYTHON_INSTALL_DIR")
                .map(|value| value.as_ref()),
            Some(paths.python_install_root.to_string_lossy().as_ref())
        );
        assert_eq!(
            environment.get("UV_CACHE_DIR").map(|value| value.as_ref()),
            Some(paths.uv_cache_root.to_string_lossy().as_ref())
        );
    }

    #[test]
    fn probe_requires_the_locked_module_and_python_3_12() {
        assert!(
            validate_environment_probe_output(b"0.18.0\n3.12.13\n", "gpstitch-0.18.0")
                .unwrap()
                .contains("3.12.13")
        );
        assert!(
            validate_environment_probe_output(b"0.18.0\n3.14.4\n", "gpstitch-0.18.0")
                .unwrap_err()
                .contains("expected 3.12.13")
        );
        assert!(
            validate_environment_probe_output(b"0.19.0\n3.12.13\n", "gpstitch-0.18.0")
                .unwrap_err()
                .contains("expected 0.18.0")
        );
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
        assert!(environment_structure_ready(
            &paths.gpstitch,
            "gpstitch-0.18.0"
        ));
        assert!(environment_structure_ready(
            &paths.cv,
            "roadwatcher-cv-0.1.0"
        ));
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
    fn restores_the_previous_owned_environment_when_post_promotion_probe_fails() {
        let root = std::env::temp_dir().join(format!(
            "roadwatcher-promotion-rollback-{}",
            uuid::Uuid::new_v4()
        ));
        let paths = managed_environment_paths(&root);
        fs::create_dir_all(
            paths
                .gpstitch
                .join(if cfg!(windows) { "Scripts" } else { "bin" }),
        )
        .unwrap();
        fs::write(
            paths.gpstitch.join(".roadwatcher-managed-environment"),
            "gpstitch-0.18.0",
        )
        .unwrap();
        fs::write(paths.gpstitch.join("pyvenv.cfg"), "previous").unwrap();
        fs::write(
            paths.gpstitch.join(if cfg!(windows) {
                "Scripts/python.exe"
            } else {
                "bin/python"
            }),
            "previous-python",
        )
        .unwrap();
        fs::write(paths.gpstitch.join("previous.txt"), "preserve").unwrap();

        let response = prepare_with_executor(
            RuntimePrepareRequest {
                uv_executable: "uv".to_string(),
                gpstitch_source: root.join("gp-source"),
                cv_source: root.join("cv-source"),
                environments: paths.clone(),
            },
            Arc::new(FailAfterPromotion),
        );

        assert_eq!(response.status, "incomplete");
        assert!(response
            .environments
            .iter()
            .any(|item| item.id == "gpstitch-environment"
                && item.detail.contains("previous environment restored")));
        assert!(response
            .environments
            .iter()
            .any(|item| item.id == "cv-environment"
                && item.detail.contains("invalid environment removed")));
        assert!(paths.gpstitch.join("previous.txt").is_file());
        assert!(!paths.cv.exists());
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
        assert!(environment_structure_ready(
            &paths.gpstitch,
            "gpstitch-0.18.0"
        ));
        assert!(environment_structure_ready(
            &paths.cv,
            "roadwatcher-cv-0.1.0"
        ));
        assert!(
            super::probe_managed_environment(&paths.gpstitch, "gpstitch-0.18.0")
                .unwrap()
                .contains("0.18.0")
        );
        assert!(
            super::probe_managed_environment(&paths.cv, "roadwatcher-cv-0.1.0")
                .unwrap()
                .contains("0.1.0")
        );
        fs::remove_dir_all(root).unwrap();
    }
}
