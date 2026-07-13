use crate::bounded_process::run_bounded_process;
use crate::gdal_adapter::{managed_process_environment, GdalRuntimeEnvironment};
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
    pub gdal_runtime_environment: GdalRuntimeEnvironment,
    pub gpstitch_source: PathBuf,
    pub cv_source: PathBuf,
    pub valhalla_source: PathBuf,
    pub gpstitch_environment: PathBuf,
    pub cv_environment: PathBuf,
    pub valhalla_environment: PathBuf,
    pub python_install_root: PathBuf,
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
    fn run(
        &self,
        executable: &Path,
        args: &[&str],
        environment: &ToolProbeEnvironment,
    ) -> Result<String, String>;
}

struct ProcessToolProbeExecutor;

impl ToolProbeExecutor for ProcessToolProbeExecutor {
    fn run(
        &self,
        executable: &Path,
        args: &[&str],
        environment: &ToolProbeEnvironment,
    ) -> Result<String, String> {
        let mut command = Command::new(executable);
        for name in &environment.removed {
            command.env_remove(name);
        }
        command.args(args).envs(environment.values.iter().cloned());
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
            true,
        ),
        source_status(
            "cv-source",
            "RoadWatcher CV bundled source",
            true,
            &request.cv_source,
            "0.1.0",
            None,
            true,
        ),
        source_status(
            "valhalla-source",
            "RoadWatcher Valhalla lock definition",
            true,
            &request.valhalla_source,
            "0.1.0",
            None,
            false,
        ),
    ];
    let mut python_spec = tool_spec(
        "python",
        "Python resolver through uv",
        false,
        executable(&request.uv_executable, "uv"),
        vec!["python", "find", "3.12.13", "--managed-python"],
    );
    python_spec.environment.values.push((
        OsString::from("UV_PYTHON_INSTALL_DIR"),
        request.python_install_root.as_os_str().to_owned(),
    ));
    let managed_gdal_environment = managed_process_environment(&request.gdal_runtime_environment)
        .map(ToolProbeEnvironment::from)
        .map_err(|error| error.to_string());
    let specs = vec![
        environment_tool_spec(
            "gpstitch-environment",
            "Managed GPStitch environment",
            &request.gpstitch_environment,
            "gpstitch-0.18.0",
            true,
        ),
        environment_tool_spec(
            "cv-environment",
            "Managed RoadWatcher CV environment",
            &request.cv_environment,
            "roadwatcher-cv-0.1.0",
            false,
        ),
        environment_tool_spec(
            "valhalla-environment",
            "Managed pyvalhalla environment",
            &request.valhalla_environment,
            "pyvalhalla-3.7.0",
            true,
        ),
        tool_spec(
            "uv",
            "uv environment preparer",
            false,
            executable(&request.uv_executable, "uv"),
            vec!["--version"],
        ),
        python_spec,
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
        gdal_tool_spec(
            "ogrinfo",
            "GDAL ogrinfo",
            false,
            &request.gdal_binary_directory,
            "ogrinfo",
            &managed_gdal_environment,
            vec!["--version"],
        ),
        gdal_tool_spec(
            "ogr2ogr",
            "GDAL ogr2ogr",
            false,
            &request.gdal_binary_directory,
            "ogr2ogr",
            &managed_gdal_environment,
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
    required: bool,
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
        required,
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
    environment: ToolProbeEnvironment,
    expected_version: Option<&'static str>,
}

#[derive(Clone, Default)]
struct ToolProbeEnvironment {
    values: Vec<(OsString, OsString)>,
    removed: Vec<OsString>,
}

impl From<crate::gdal_adapter::GdalProcessEnvironment> for ToolProbeEnvironment {
    fn from(environment: crate::gdal_adapter::GdalProcessEnvironment) -> Self {
        Self {
            values: environment.values().to_vec(),
            removed: environment.removed().to_vec(),
        }
    }
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
        environment: ToolProbeEnvironment::default(),
        expected_version: None,
    }
}

fn gdal_tool_spec(
    id: &'static str,
    label: &'static str,
    required: bool,
    directory: &str,
    executable_name: &str,
    environment: &Result<ToolProbeEnvironment, String>,
    args: Vec<&'static str>,
) -> ToolSpec {
    let executable = match environment {
        Ok(_) => directory_executable(directory, executable_name),
        Err(detail) => Err(detail.clone()),
    };
    let mut spec = tool_spec(id, label, required, executable, args);
    if let Ok(environment) = environment {
        spec.environment = environment.clone();
    }
    spec
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
    match executor.run(&executable, &spec.args, &spec.environment) {
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
    source_directory_required: bool,
) -> RuntimeComponentStatus {
    let pyproject = std::fs::read_to_string(root.join("pyproject.toml")).unwrap_or_default();
    let version_matches = pyproject.contains(&format!("version = \"{version}\""));
    let license_matches = license_marker.is_none_or(|marker| {
        std::fs::read_to_string(root.join("LICENSE")).is_ok_and(|license| license.contains(marker))
    });
    let ready = root.is_dir()
        && root.join("pyproject.toml").is_file()
        && root.join("uv.lock").is_file()
        && (!source_directory_required || root.join("src").is_dir())
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
    use crate::gdal_adapter::GdalRuntimeEnvironment;
    use std::fs;
    use std::path::{Path, PathBuf};
    use std::sync::Arc;

    struct FakeExecutor;
    impl ToolProbeExecutor for FakeExecutor {
        fn run(
            &self,
            executable: &Path,
            args: &[&str],
            _environment: &super::ToolProbeEnvironment,
        ) -> Result<String, String> {
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
            } else if args.iter().any(|value| value.contains("pyvalhalla")) {
                Ok("3.7.0".to_string())
            } else {
                Ok(format!("{name} 1.0"))
            }
        }
    }

    struct MissingModuleExecutor(&'static str);
    impl ToolProbeExecutor for MissingModuleExecutor {
        fn run(
            &self,
            executable: &Path,
            args: &[&str],
            environment: &super::ToolProbeEnvironment,
        ) -> Result<String, String> {
            if args.iter().any(|value| value.contains(self.0)) {
                Err(format!("No module for probe {}", self.0))
            } else {
                FakeExecutor.run(executable, args, environment)
            }
        }
    }

    struct GdalProbeExecutor {
        environments: std::sync::Mutex<Vec<super::ToolProbeEnvironment>>,
    }

    impl ToolProbeExecutor for GdalProbeExecutor {
        fn run(
            &self,
            executable: &Path,
            args: &[&str],
            environment: &super::ToolProbeEnvironment,
        ) -> Result<String, String> {
            if executable.to_string_lossy().contains("ogr") {
                self.environments.lock().unwrap().push(environment.clone());
                Ok("GDAL 3.12.4".to_string())
            } else {
                FakeExecutor.run(executable, args, environment)
            }
        }
    }

    #[test]
    fn reports_runtime_ready_when_only_preparation_and_optional_tools_are_missing() {
        let root =
            std::env::temp_dir().join(format!("roadwatcher-preflight-{}", std::process::id()));
        let gpstitch = source(&root.join("gpstitch"), "0.18.0", true);
        let cv = source(&root.join("cv"), "0.1.0", false);
        let valhalla = source(&root.join("valhalla"), "0.1.0", false);
        let environments = crate::managed_runtime::managed_environment_paths(&root);
        environment(&environments.gpstitch, "gpstitch-0.18.0");
        environment(&environments.cv, "roadwatcher-cv-0.1.0");
        environment(&environments.valhalla, "pyvalhalla-3.7.0");
        let response = run_with_executor(
            RuntimePreflightRequest {
                uv_executable: "uv".to_string(),
                ffmpeg_binary_directory: String::new(),
                gdal_binary_directory: String::new(),
                gdal_runtime_environment: GdalRuntimeEnvironment::default(),
                gpstitch_source: gpstitch,
                cv_source: cv,
                valhalla_source: valhalla,
                gpstitch_environment: environments.gpstitch,
                cv_environment: environments.cv,
                valhalla_environment: environments.valhalla,
                python_install_root: environments.python_install_root,
            },
            Arc::new(FakeExecutor),
        );
        assert_eq!(response.status, "ready");
        assert_eq!(response.components.len(), 12);
        assert!(response
            .components
            .iter()
            .any(|item| item.id == "cv-environment" && !item.required && item.status == "ready"));
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
    fn distinguishes_optional_cv_from_required_gpstitch_module_failures() {
        let root = std::env::temp_dir().join(format!(
            "roadwatcher-preflight-module-{}",
            uuid::Uuid::new_v4()
        ));
        let gpstitch = source(&root.join("gpstitch"), "0.18.0", true);
        let cv = source(&root.join("cv"), "0.1.0", false);
        let valhalla = source(&root.join("valhalla"), "0.1.0", false);
        let environments = crate::managed_runtime::managed_environment_paths(&root);
        environment(&environments.gpstitch, "gpstitch-0.18.0");
        environment(&environments.cv, "roadwatcher-cv-0.1.0");
        environment(&environments.valhalla, "pyvalhalla-3.7.0");

        let request = RuntimePreflightRequest {
            uv_executable: "uv".to_string(),
            ffmpeg_binary_directory: String::new(),
            gdal_binary_directory: String::new(),
            gdal_runtime_environment: GdalRuntimeEnvironment::default(),
            gpstitch_source: gpstitch,
            cv_source: cv,
            valhalla_source: valhalla,
            gpstitch_environment: environments.gpstitch.clone(),
            cv_environment: environments.cv.clone(),
            valhalla_environment: environments.valhalla.clone(),
            python_install_root: environments.python_install_root.clone(),
        };
        let optional_cv_missing = run_with_executor(
            request.clone(),
            Arc::new(MissingModuleExecutor("import roadwatcher_cv")),
        );
        assert_eq!(optional_cv_missing.status, "ready");
        assert!(optional_cv_missing
            .components
            .iter()
            .any(|item| item.id == "cv-environment"
                && !item.required
                && item.status == "missing"
                && item.detail.contains("No module")));

        let required_gpstitch_missing =
            run_with_executor(request, Arc::new(MissingModuleExecutor("import gpstitch")));
        assert_eq!(required_gpstitch_missing.status, "incomplete");
        assert!(required_gpstitch_missing
            .components
            .iter()
            .any(|item| item.id == "gpstitch-environment"
                && item.required
                && item.status == "missing"));

        let valhalla_request = RuntimePreflightRequest {
            uv_executable: "uv".to_string(),
            ffmpeg_binary_directory: String::new(),
            gdal_binary_directory: String::new(),
            gdal_runtime_environment: GdalRuntimeEnvironment::default(),
            gpstitch_source: source(&root.join("gpstitch-again"), "0.18.0", true),
            cv_source: source(&root.join("cv-again"), "0.1.0", false),
            valhalla_source: source(&root.join("valhalla-again"), "0.1.0", false),
            gpstitch_environment: environments.gpstitch,
            cv_environment: environments.cv,
            valhalla_environment: environments.valhalla,
            python_install_root: environments.python_install_root,
        };
        let required_valhalla_missing = run_with_executor(
            valhalla_request,
            Arc::new(MissingModuleExecutor("pyvalhalla")),
        );
        assert_eq!(required_valhalla_missing.status, "incomplete");
        assert!(required_valhalla_missing.components.iter().any(|item| {
            item.id == "valhalla-environment" && item.required && item.status == "missing"
        }));
        let _ = fs::remove_dir_all(root);
    }

    #[test]
    fn confines_managed_gdal_preflight_to_its_owned_environment() {
        let root = std::env::temp_dir().join(format!(
            "roadwatcher-preflight-gdal-{}",
            uuid::Uuid::new_v4()
        ));
        let gpstitch = source(&root.join("gpstitch"), "0.18.0", true);
        let cv = source(&root.join("cv"), "0.1.0", false);
        let valhalla = source(&root.join("valhalla"), "0.1.0", false);
        let environments = crate::managed_runtime::managed_environment_paths(&root);
        environment(&environments.gpstitch, "gpstitch-0.18.0");
        environment(&environments.cv, "roadwatcher-cv-0.1.0");
        environment(&environments.valhalla, "pyvalhalla-3.7.0");
        let gdal_binary_directory = root.join("gdal/bin");
        let gdal_data = root.join("gdal/share/gdal");
        let proj_data = root.join("gdal/share/proj");
        fs::create_dir_all(&gdal_binary_directory).unwrap();
        fs::create_dir_all(&gdal_data).unwrap();
        fs::create_dir_all(&proj_data).unwrap();
        let executor = Arc::new(GdalProbeExecutor {
            environments: std::sync::Mutex::new(Vec::new()),
        });
        let response = run_with_executor(
            RuntimePreflightRequest {
                uv_executable: "uv".to_string(),
                ffmpeg_binary_directory: String::new(),
                gdal_binary_directory: gdal_binary_directory.display().to_string(),
                gdal_runtime_environment: GdalRuntimeEnvironment {
                    gdal_data_directory: Some(gdal_data.canonicalize().unwrap()),
                    proj_data_directory: Some(proj_data.canonicalize().unwrap()),
                },
                gpstitch_source: gpstitch,
                cv_source: cv,
                valhalla_source: valhalla,
                gpstitch_environment: environments.gpstitch,
                cv_environment: environments.cv,
                valhalla_environment: environments.valhalla,
                python_install_root: environments.python_install_root,
            },
            Arc::clone(&executor) as Arc<dyn ToolProbeExecutor>,
        );
        assert!(response
            .components
            .iter()
            .filter(|component| component.id == "ogrinfo" || component.id == "ogr2ogr")
            .all(|component| component.status == "ready"));
        let expected_gdal_data = gdal_data.canonicalize().unwrap().display().to_string();
        let expected_proj_data = proj_data.canonicalize().unwrap().display().to_string();
        let environments = executor.environments.lock().unwrap();
        assert_eq!(environments.len(), 2);
        for environment in environments.iter() {
            assert!(environment.values.iter().any(|(name, value)| {
                name == "GDAL_DATA" && value.to_string_lossy() == expected_gdal_data
            }));
            assert!(environment.values.iter().any(|(name, value)| {
                name == "PROJ_DATA" && value.to_string_lossy() == expected_proj_data
            }));
            assert!(environment
                .values
                .iter()
                .any(|(name, value)| name == "PROJ_NETWORK" && value == "OFF"));
            assert!(environment
                .removed
                .iter()
                .any(|name| name == "GDAL_CONFIG_FILE"));
            assert!(environment.removed.iter().any(|name| name == "PROJ_LIB"));
        }
        drop(environments);
        fs::remove_dir_all(root).unwrap();
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
        if marker == "pyvalhalla-3.7.0" {
            fs::write(
                root.join(if cfg!(windows) {
                    "Scripts/valhalla_service.exe"
                } else {
                    "bin/valhalla_service"
                }),
                "valhalla",
            )
            .unwrap();
        }
    }
}
