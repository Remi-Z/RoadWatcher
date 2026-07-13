use crate::bounded_process::run_bounded_process;
use crate::managed_runtime::{
    environment_probe_code, environment_python, environment_structure_ready, environment_version,
};
use serde::Serialize;
use std::ffi::OsString;
use std::path::{Component, Path, PathBuf};
use std::process::Command;
use std::sync::Arc;
use std::thread;
use std::time::{Duration, SystemTime, UNIX_EPOCH};

const PROBE_TIMEOUT: Duration = Duration::from_secs(10);
const PROBE_OUTPUT_LIMIT: u64 = 64 * 1024;

#[derive(Clone, Debug)]
pub struct RuntimePreflightRequest {
    pub uv_executable: String,
    pub ffmpeg_binary_directory: String,
    pub gdal_binary_directory: String,
    pub gpstitch_source: PathBuf,
    pub cv_source: PathBuf,
    pub gpstitch_environment: PathBuf,
    pub cv_environment: PathBuf,
}

#[derive(Clone, Debug, Eq, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct RuntimeComponentStatus {
    pub id: String,
    pub label: String,
    pub required: bool,
    pub status: String,
    pub executable: String,
    pub version: String,
    pub detail: String,
}

#[derive(Clone, Debug, Eq, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct RuntimePreflightResponse {
    pub checked_at_unix: u64,
    pub status: String,
    pub components: Vec<RuntimeComponentStatus>,
}

trait ToolProbeExecutor: Send + Sync {
    fn run(&self, executable: &Path, args: &[&str]) -> Result<String, String>;
}

struct ProcessToolProbeExecutor;

impl ToolProbeExecutor for ProcessToolProbeExecutor {
    fn run(&self, executable: &Path, args: &[&str]) -> Result<String, String> {
        let mut command = Command::new(executable);
        command.args(args);
        let output = run_bounded_process(&mut command, PROBE_TIMEOUT, PROBE_OUTPUT_LIMIT)
            .map_err(|error| error.to_string())?;
        let text = first_nonblank_line(&output.stdout)
            .or_else(|| first_nonblank_line(&output.stderr))
            .unwrap_or_default();
        if !output.status.success() {
            return Err(if text.is_empty() {
                "probe returned a non-zero exit status".to_string()
            } else {
                text
            });
        }
        if text.is_empty() {
            return Err("probe returned no version or path output".to_string());
        }
        Ok(text)
    }
}

pub fn run_runtime_preflight(request: RuntimePreflightRequest) -> RuntimePreflightResponse {
    run_with_executor(request, Arc::new(ProcessToolProbeExecutor))
}

fn run_with_executor(
    request: RuntimePreflightRequest,
    executor: Arc<dyn ToolProbeExecutor>,
) -> RuntimePreflightResponse {
    let mut components = vec![
        source_status(
            "gpstitch-source",
            "GPStitch bundled source",
            true,
            &request.gpstitch_source,
            "0.18.0",
            Some("GNU GENERAL PUBLIC LICENSE"),
        ),
        source_status(
            "cv-source",
            "RoadWatcher CV bundled source",
            true,
            &request.cv_source,
            "0.1.0",
            None,
        ),
    ];
    let specs = vec![
        environment_tool_spec(
            "gpstitch-environment",
            "Managed GPStitch environment",
            &request.gpstitch_environment,
            "gpstitch-0.18.0",
        ),
        environment_tool_spec(
            "cv-environment",
            "Managed RoadWatcher CV environment",
            &request.cv_environment,
            "roadwatcher-cv-0.1.0",
        ),
        tool_spec(
            "uv",
            "uv environment preparer",
            false,
            executable(&request.uv_executable, "uv"),
            vec!["--version"],
        ),
        tool_spec(
            "python",
            "Python resolver through uv",
            false,
            executable(&request.uv_executable, "uv"),
            vec!["python", "find"],
        ),
        tool_spec(
            "ffmpeg",
            "FFmpeg video processor",
            true,
            directory_executable(&request.ffmpeg_binary_directory, "ffmpeg"),
            vec!["-version"],
        ),
        tool_spec(
            "ffprobe",
            "ffprobe metadata reader",
            true,
            directory_executable(&request.ffmpeg_binary_directory, "ffprobe"),
            vec!["-version"],
        ),
        tool_spec(
            "ogrinfo",
            "GDAL ogrinfo",
            false,
            directory_executable(&request.gdal_binary_directory, "ogrinfo"),
            vec!["--version"],
        ),
        tool_spec(
            "ogr2ogr",
            "GDAL ogr2ogr",
            false,
            directory_executable(&request.gdal_binary_directory, "ogr2ogr"),
            vec!["--version"],
        ),
    ];
    let handles = specs
        .into_iter()
        .map(|spec| {
            let executor = Arc::clone(&executor);
            thread::spawn(move || probe_tool(spec, executor.as_ref()))
        })
        .collect::<Vec<_>>();
    components.extend(handles.into_iter().map(|handle| {
        handle.join().unwrap_or_else(|_| RuntimeComponentStatus {
            id: "probe-thread".to_string(),
            label: "Runtime probe".to_string(),
            required: true,
            status: "invalid".to_string(),
            executable: String::new(),
            version: String::new(),
            detail: "runtime probe thread failed".to_string(),
        })
    }));
    let required_ready = components
        .iter()
        .filter(|item| item.required)
        .all(|item| item.status == "ready");
    RuntimePreflightResponse {
        checked_at_unix: SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .unwrap_or_default()
            .as_secs(),
        status: if required_ready {
            "ready"
        } else {
            "incomplete"
        }
        .to_string(),
        components,
    }
}

fn environment_tool_spec(
    id: &'static str,
    label: &'static str,
    environment: &Path,
    marker: &'static str,
) -> ToolSpec {
    let executable = if environment_structure_ready(environment, marker) {
        Ok(environment_python(environment))
    } else {
        Err(
            "managed environment structure or ownership marker is invalid; run runtime preparation"
                .to_string(),
        )
    };
    let mut spec = tool_spec(
        id,
        label,
        true,
        executable,
        vec![
            "-c",
            environment_probe_code(marker).expect("known environment marker"),
        ],
    );
    spec.expected_version = environment_version(marker);
    spec
}

struct ToolSpec {
    id: &'static str,
    label: &'static str,
    required: bool,
    executable: Result<PathBuf, String>,
    args: Vec<&'static str>,
    expected_version: Option<&'static str>,
}

fn tool_spec(
    id: &'static str,
    label: &'static str,
    required: bool,
    executable: Result<PathBuf, String>,
    args: Vec<&'static str>,
) -> ToolSpec {
    ToolSpec {
        id,
        label,
        required,
        executable,
        args,
        expected_version: None,
    }
}

fn probe_tool(spec: ToolSpec, executor: &dyn ToolProbeExecutor) -> RuntimeComponentStatus {
    let executable = match spec.executable {
        Ok(path) => path,
        Err(detail) => {
            return RuntimeComponentStatus {
                id: spec.id.to_string(),
                label: spec.label.to_string(),
                required: spec.required,
                status: "invalid".to_string(),
                executable: String::new(),
                version: String::new(),
                detail,
            }
        }
    };
    match executor.run(&executable, &spec.args) {
        Ok(version)
            if spec
                .expected_version
                .is_some_and(|expected| version != expected) =>
        {
            RuntimeComponentStatus {
                id: spec.id.to_string(),
                label: spec.label.to_string(),
                required: spec.required,
                status: "missing".to_string(),
                executable: executable.to_string_lossy().into_owned(),
                version,
                detail: format!(
                    "probe returned an unexpected version; expected {}",
                    spec.expected_version.unwrap_or_default()
                ),
            }
        }
        Ok(version) => RuntimeComponentStatus {
            id: spec.id.to_string(),
            label: spec.label.to_string(),
            required: spec.required,
            status: "ready".to_string(),
            executable: executable.to_string_lossy().into_owned(),
            version,
            detail: "probe succeeded".to_string(),
        },
        Err(detail) => RuntimeComponentStatus {
            id: spec.id.to_string(),
            label: spec.label.to_string(),
            required: spec.required,
            status: "missing".to_string(),
            executable: executable.to_string_lossy().into_owned(),
            version: String::new(),
            detail: detail.chars().take(1024).collect(),
        },
    }
}

fn source_status(
    id: &str,
    label: &str,
    required: bool,
    root: &Path,
    version: &str,
    license_marker: Option<&str>,
) -> RuntimeComponentStatus {
    let pyproject = std::fs::read_to_string(root.join("pyproject.toml")).unwrap_or_default();
    let version_matches = pyproject.contains(&format!("version = \"{version}\""));
    let license_matches = license_marker.is_none_or(|marker| {
        std::fs::read_to_string(root.join("LICENSE")).is_ok_and(|license| license.contains(marker))
    });
    let ready = root.is_dir()
        && root.join("pyproject.toml").is_file()
        && root.join("uv.lock").is_file()
        && root.join("src").is_dir()
        && version_matches
        && license_matches;
    RuntimeComponentStatus {
        id: id.to_string(),
        label: label.to_string(),
        required,
        status: if ready { "ready" } else { "missing" }.to_string(),
        executable: root.to_string_lossy().into_owned(),
        version: if ready {
            version.to_string()
        } else {
            String::new()
        },
        detail: if ready {
            "packaged source and lock are present"
        } else {
            "packaged source, version, lock, or license is missing or invalid"
        }
        .to_string(),
    }
}

fn executable(configured: &str, fallback: &str) -> Result<PathBuf, String> {
    let value = if configured.trim().is_empty() || configured.starts_with("slot:") {
        fallback
    } else {
        configured.trim()
    };
    let path = PathBuf::from(value);
    if path.is_absolute()
        || path.components().count() == 1
            && path
                .components()
                .all(|part| matches!(part, Component::Normal(_)))
    {
        Ok(path)
    } else {
        Err(format!("invalid executable path: {value}"))
    }
}

fn directory_executable(directory: &str, name: &str) -> Result<PathBuf, String> {
    if directory.trim().is_empty() || directory.starts_with("slot:") {
        return Ok(PathBuf::from(platform_executable(name)));
    }
    let root = PathBuf::from(directory.trim());
    if !root.is_absolute() || !root.is_dir() {
        return Err(format!(
            "binary directory is not an existing absolute directory: {}",
            root.display()
        ));
    }
    Ok(root.join(platform_executable(name)))
}

fn platform_executable(name: &str) -> OsString {
    if cfg!(windows) {
        OsString::from(format!("{name}.exe"))
    } else {
        OsString::from(name)
    }
}

fn first_nonblank_line(bytes: &[u8]) -> Option<String> {
    String::from_utf8_lossy(bytes)
        .lines()
        .map(str::trim)
        .find(|line| !line.is_empty())
        .map(|line| line.chars().take(512).collect())
}

#[cfg(test)]
mod tests {
    use super::{run_with_executor, RuntimePreflightRequest, ToolProbeExecutor};
    use std::fs;
    use std::path::{Path, PathBuf};
    use std::sync::Arc;

    struct FakeExecutor;
    impl ToolProbeExecutor for FakeExecutor {
        fn run(&self, executable: &Path, args: &[&str]) -> Result<String, String> {
            let name = executable.to_string_lossy();
            if name == "uv" || name.contains("ogr") {
                Err("not installed".to_string())
            } else if args.iter().any(|value| value.contains("import gpstitch")) {
                Ok("0.18.0".to_string())
            } else if args
                .iter()
                .any(|value| value.contains("import roadwatcher_cv"))
            {
                Ok("0.1.0".to_string())
            } else {
                Ok(format!("{name} 1.0"))
            }
        }
    }

    struct MissingCvModuleExecutor;
    impl ToolProbeExecutor for MissingCvModuleExecutor {
        fn run(&self, executable: &Path, args: &[&str]) -> Result<String, String> {
            if executable
                .to_string_lossy()
                .contains("roadwatcher-cv-0.1.0")
                && args
                    .iter()
                    .any(|value| value.contains("import roadwatcher_cv"))
            {
                Err("No module named roadwatcher_cv".to_string())
            } else {
                FakeExecutor.run(executable, args)
            }
        }
    }

    #[test]
    fn reports_runtime_ready_when_only_preparation_and_optional_tools_are_missing() {
        let root =
            std::env::temp_dir().join(format!("roadwatcher-preflight-{}", std::process::id()));
        let gpstitch = source(&root.join("gpstitch"), "0.18.0", true);
        let cv = source(&root.join("cv"), "0.1.0", false);
        let environments = crate::managed_runtime::managed_environment_paths(&root);
        environment(&environments.gpstitch, "gpstitch-0.18.0");
        environment(&environments.cv, "roadwatcher-cv-0.1.0");
        let response = run_with_executor(
            RuntimePreflightRequest {
                uv_executable: "uv".to_string(),
                ffmpeg_binary_directory: String::new(),
                gdal_binary_directory: String::new(),
                gpstitch_source: gpstitch,
                cv_source: cv,
                gpstitch_environment: environments.gpstitch,
                cv_environment: environments.cv,
            },
            Arc::new(FakeExecutor),
        );
        assert_eq!(response.status, "ready");
        assert_eq!(response.components.len(), 10);
        assert!(response
            .components
            .iter()
            .filter(|item| !item.required)
            .all(|item| item.status == "missing"));
        assert!(response
            .components
            .iter()
            .any(|item| item.id == "uv" && !item.required && item.status == "missing"));
        assert!(response
            .components
            .iter()
            .any(|item| item.id == "python" && !item.required && item.status == "missing"));
        let _ = fs::remove_dir_all(root);
    }

    #[test]
    fn rejects_a_structurally_present_environment_when_its_module_cannot_import() {
        let root = std::env::temp_dir().join(format!(
            "roadwatcher-preflight-module-{}",
            uuid::Uuid::new_v4()
        ));
        let gpstitch = source(&root.join("gpstitch"), "0.18.0", true);
        let cv = source(&root.join("cv"), "0.1.0", false);
        let environments = crate::managed_runtime::managed_environment_paths(&root);
        environment(&environments.gpstitch, "gpstitch-0.18.0");
        environment(&environments.cv, "roadwatcher-cv-0.1.0");

        let response = run_with_executor(
            RuntimePreflightRequest {
                uv_executable: "uv".to_string(),
                ffmpeg_binary_directory: String::new(),
                gdal_binary_directory: String::new(),
                gpstitch_source: gpstitch,
                cv_source: cv,
                gpstitch_environment: environments.gpstitch,
                cv_environment: environments.cv,
            },
            Arc::new(MissingCvModuleExecutor),
        );

        assert_eq!(response.status, "incomplete");
        assert!(response
            .components
            .iter()
            .any(|item| item.id == "cv-environment"
                && item.status == "missing"
                && item.detail.contains("No module named")));
        let _ = fs::remove_dir_all(root);
    }

    fn source(root: &Path, version: &str, license: bool) -> PathBuf {
        fs::create_dir_all(root.join("src")).unwrap();
        fs::write(
            root.join("pyproject.toml"),
            format!("version = \"{version}\""),
        )
        .unwrap();
        fs::write(root.join("uv.lock"), "lock").unwrap();
        if license {
            fs::write(root.join("LICENSE"), "GNU GENERAL PUBLIC LICENSE").unwrap();
        }
        root.to_path_buf()
    }

    fn environment(root: &Path, marker: &str) {
        fs::create_dir_all(root.join(if cfg!(windows) { "Scripts" } else { "bin" })).unwrap();
        fs::write(root.join(".roadwatcher-managed-environment"), marker).unwrap();
        fs::write(root.join("pyvenv.cfg"), "home=test").unwrap();
        fs::write(
            root.join(if cfg!(windows) {
                "Scripts/python.exe"
            } else {
                "bin/python"
            }),
            "python",
        )
        .unwrap();
    }
}
