use serde_json::Value;
use std::ffi::OsString;
use std::io::Read;
use std::path::{Path, PathBuf};
use std::process::{Command, Stdio};
use std::thread;
use std::time::{Duration, Instant};
use thiserror::Error;

const MAX_INSPECTION_BYTES: u64 = 2 * 1024 * 1024;
const MAX_GEOJSON_BYTES: u64 = 64 * 1024 * 1024;
const PROCESS_TIMEOUT: Duration = Duration::from_secs(120);
const MANAGED_ENVIRONMENT_REMOVALS: &[&str] = &[
    "GDAL_CONFIG_FILE",
    "GDAL_DATA",
    "GDAL_DRIVER_PATH",
    "GDAL_PYTHON_DRIVER_PATH",
    "GDAL_SKIP",
    "OGR_DRIVER_PATH",
    "OGR_SKIP",
    "PROJ_AUX_DB",
    "PROJ_CURL_CA_BUNDLE",
    "PROJ_DATA",
    "PROJ_LIB",
    "PROJ_NETWORK",
    "PROJ_NETWORK_ENDPOINT",
    "PROJ_USER_WRITABLE_DIRECTORY",
    "PYTHONSO",
];

#[derive(Clone, Debug)]
pub struct GdalNormalizeRequest {
    pub source_path: PathBuf,
    pub source_crs: String,
    pub layer_name: String,
    pub binary_directory: String,
    pub runtime_environment: GdalRuntimeEnvironment,
}

#[derive(Clone, Debug, Default, PartialEq, Eq)]
pub struct GdalRuntimeEnvironment {
    pub gdal_data_directory: Option<PathBuf>,
    pub proj_data_directory: Option<PathBuf>,
}

#[derive(Clone, Debug, PartialEq)]
pub struct GdalNormalizedDataset {
    pub geojson: String,
    pub source_crs: String,
    pub layer_name: String,
}

#[derive(Debug, Error)]
pub enum GdalAdapterError {
    #[error("GDAL binary directory is not an existing directory: {0}")]
    InvalidBinaryDirectory(String),
    #[error("GDAL runtime data directory is not an existing directory: {0}")]
    InvalidRuntimeDataDirectory(String),
    #[error("Managed GDAL requires both GDAL_DATA and PROJ_DATA directories.")]
    IncompleteRuntimeEnvironment,
    #[error("Could not launch {executable}: {detail}")]
    Launch { executable: String, detail: String },
    #[error("{executable} exceeded the 120 second execution limit.")]
    Timeout { executable: String },
    #[error("{executable} output exceeded its bounded import limit.")]
    OutputTooLarge { executable: String },
    #[error("{executable} failed: {detail}")]
    Failed { executable: String, detail: String },
    #[error("GDAL dataset metadata is invalid: {0}")]
    InvalidMetadata(String),
    #[error("GIS dataset contains multiple layers; provide one of: {0}")]
    LayerRequired(String),
    #[error("GIS layer was not found: {0}")]
    LayerNotFound(String),
    #[error("GDAL could not determine the selected layer CRS; provide an explicit CRS override.")]
    MissingCrs,
}

pub fn normalize_with_gdal(
    request: &GdalNormalizeRequest,
) -> Result<GdalNormalizedDataset, GdalAdapterError> {
    normalize_with_executor(request, &ProcessExecutor)
}

trait GdalExecutor {
    fn run(
        &self,
        executable: &Path,
        args: &[OsString],
        environment: &GdalProcessEnvironment,
        max_stdout_bytes: u64,
    ) -> Result<ProcessResult, GdalAdapterError>;
}

#[derive(Clone, Debug, Default)]
pub(crate) struct GdalProcessEnvironment {
    values: Vec<(OsString, OsString)>,
    removed: Vec<OsString>,
}

impl GdalProcessEnvironment {
    pub(crate) fn values(&self) -> &[(OsString, OsString)] {
        &self.values
    }

    pub(crate) fn removed(&self) -> &[OsString] {
        &self.removed
    }
}

#[derive(Debug)]
struct ProcessResult {
    success: bool,
    stdout: Vec<u8>,
    stderr: Vec<u8>,
}

struct ProcessExecutor;

impl GdalExecutor for ProcessExecutor {
    fn run(
        &self,
        executable: &Path,
        args: &[OsString],
        environment: &GdalProcessEnvironment,
        max_stdout_bytes: u64,
    ) -> Result<ProcessResult, GdalAdapterError> {
        let executable_label = executable.to_string_lossy().into_owned();
        let mut command = Command::new(executable);
        for name in &environment.removed {
            command.env_remove(name);
        }
        command
            .args(args)
            .envs(environment.values.iter().cloned())
            .stdin(Stdio::null())
            .stdout(Stdio::piped())
            .stderr(Stdio::piped());
        let mut child = command.spawn().map_err(|error| GdalAdapterError::Launch {
            executable: executable_label.clone(),
            detail: error.to_string(),
        })?;
        let stdout = child.stdout.take().expect("piped GDAL stdout");
        let stderr = child.stderr.take().expect("piped GDAL stderr");
        let stdout_reader = thread::spawn(move || read_bounded(stdout, max_stdout_bytes));
        let stderr_reader = thread::spawn(move || read_bounded(stderr, MAX_INSPECTION_BYTES));
        let started = Instant::now();
        let status = loop {
            match child.try_wait() {
                Ok(Some(status)) => break status,
                Ok(None) if started.elapsed() < PROCESS_TIMEOUT => {
                    thread::sleep(Duration::from_millis(25));
                }
                Ok(None) => {
                    let _ = child.kill();
                    let _ = child.wait();
                    let _ = stdout_reader.join();
                    let _ = stderr_reader.join();
                    return Err(GdalAdapterError::Timeout {
                        executable: executable_label,
                    });
                }
                Err(error) => {
                    let _ = child.kill();
                    let _ = child.wait();
                    let _ = stdout_reader.join();
                    let _ = stderr_reader.join();
                    return Err(GdalAdapterError::Launch {
                        executable: executable_label,
                        detail: error.to_string(),
                    });
                }
            }
        };
        let stdout = stdout_reader
            .join()
            .map_err(|_| GdalAdapterError::Launch {
                executable: executable_label.clone(),
                detail: "stdout reader failed".to_string(),
            })??;
        let stderr = stderr_reader
            .join()
            .map_err(|_| GdalAdapterError::Launch {
                executable: executable_label.clone(),
                detail: "stderr reader failed".to_string(),
            })??;
        Ok(ProcessResult {
            success: status.success(),
            stdout,
            stderr,
        })
    }
}

fn normalize_with_executor(
    request: &GdalNormalizeRequest,
    executor: &dyn GdalExecutor,
) -> Result<GdalNormalizedDataset, GdalAdapterError> {
    let (ogrinfo, ogr2ogr) = executables(&request.binary_directory)?;
    let environment = managed_process_environment(&request.runtime_environment)?;
    let source = request.source_path.as_os_str().to_os_string();
    let inspection = executor.run(
        &ogrinfo,
        &[
            "-ro".into(),
            "-so".into(),
            "-al".into(),
            "-json".into(),
            source.clone(),
        ],
        &environment,
        MAX_INSPECTION_BYTES,
    )?;
    ensure_success(&ogrinfo, &inspection)?;
    let metadata: Value = serde_json::from_slice(&inspection.stdout)
        .map_err(|error| GdalAdapterError::InvalidMetadata(error.to_string()))?;
    let layers = metadata
        .get("layers")
        .and_then(Value::as_array)
        .ok_or_else(|| GdalAdapterError::InvalidMetadata("layers must be an array".to_string()))?;
    let layer = select_layer(layers, &request.layer_name)?;
    let layer_name = layer
        .get("name")
        .and_then(Value::as_str)
        .filter(|value| !value.trim().is_empty())
        .ok_or_else(|| GdalAdapterError::InvalidMetadata("layer name is missing".to_string()))?
        .to_string();
    let explicit_crs = explicit_crs(&request.source_crs);
    let source_crs = explicit_crs
        .map(str::to_string)
        .or_else(|| detected_crs(layer))
        .ok_or(GdalAdapterError::MissingCrs)?;

    let mut args: Vec<OsString> = vec![
        "-f".into(),
        "GeoJSON".into(),
        "/vsistdout/".into(),
        "-t_srs".into(),
        "EPSG:4326".into(),
        "-dim".into(),
        "XY".into(),
        "-lco".into(),
        "RFC7946=YES".into(),
        "-lco".into(),
        "COORDINATE_PRECISION=8".into(),
    ];
    if let Some(crs) = explicit_crs {
        args.extend(["-s_srs".into(), crs.into()]);
    }
    args.extend([source, layer_name.clone().into()]);
    let converted = executor.run(&ogr2ogr, &args, &environment, MAX_GEOJSON_BYTES)?;
    ensure_success(&ogr2ogr, &converted)?;
    let geojson = String::from_utf8(converted.stdout)
        .map_err(|error| GdalAdapterError::InvalidMetadata(error.to_string()))?;
    Ok(GdalNormalizedDataset {
        geojson,
        source_crs,
        layer_name,
    })
}

pub(crate) fn managed_process_environment(
    configured: &GdalRuntimeEnvironment,
) -> Result<GdalProcessEnvironment, GdalAdapterError> {
    let (Some(gdal_data), Some(proj_data)) = (
        configured.gdal_data_directory.as_ref(),
        configured.proj_data_directory.as_ref(),
    ) else {
        return if configured.gdal_data_directory.is_none()
            && configured.proj_data_directory.is_none()
        {
            Ok(GdalProcessEnvironment::default())
        } else {
            Err(GdalAdapterError::IncompleteRuntimeEnvironment)
        };
    };
    let gdal_data = canonical_runtime_data_directory(gdal_data)?;
    let proj_data = canonical_runtime_data_directory(proj_data)?;
    Ok(GdalProcessEnvironment {
        values: vec![
            (OsString::from("GDAL_DATA"), gdal_data.into_os_string()),
            (OsString::from("PROJ_DATA"), proj_data.into_os_string()),
            (
                OsString::from("GDAL_DRIVER_PATH"),
                OsString::from("disable"),
            ),
            (OsString::from("OGR_DRIVER_PATH"), OsString::from("disable")),
            (OsString::from("PROJ_NETWORK"), OsString::from("OFF")),
            (OsString::from("GDAL_PAM_ENABLED"), OsString::from("NO")),
            (
                OsString::from("GDAL_VRT_ENABLE_PYTHON"),
                OsString::from("NO"),
            ),
            (
                OsString::from("GDAL_VRT_ENABLE_RAWRASTERBAND"),
                OsString::from("NO"),
            ),
        ],
        removed: MANAGED_ENVIRONMENT_REMOVALS
            .iter()
            .map(OsString::from)
            .collect(),
    })
}

fn canonical_runtime_data_directory(directory: &Path) -> Result<PathBuf, GdalAdapterError> {
    if !directory.is_absolute() || !directory.is_dir() {
        return Err(GdalAdapterError::InvalidRuntimeDataDirectory(
            directory.display().to_string(),
        ));
    }
    std::fs::canonicalize(directory)
        .map_err(|_| GdalAdapterError::InvalidRuntimeDataDirectory(directory.display().to_string()))
}

fn select_layer<'a>(layers: &'a [Value], requested: &str) -> Result<&'a Value, GdalAdapterError> {
    let named = layers
        .iter()
        .filter_map(|layer| {
            layer
                .get("name")
                .and_then(Value::as_str)
                .map(|name| (name, layer))
        })
        .collect::<Vec<_>>();
    if named.is_empty() {
        return Err(GdalAdapterError::InvalidMetadata(
            "dataset contains no named layers".to_string(),
        ));
    }
    let requested = requested.trim();
    if !requested.is_empty() {
        if requested.starts_with('-') {
            return Err(GdalAdapterError::LayerNotFound(requested.to_string()));
        }
        return named
            .into_iter()
            .find(|(name, _)| *name == requested)
            .map(|(_, layer)| layer)
            .ok_or_else(|| GdalAdapterError::LayerNotFound(requested.to_string()));
    }
    if named.len() == 1 {
        return Ok(named[0].1);
    }
    Err(GdalAdapterError::LayerRequired(
        named
            .iter()
            .map(|(name, _)| *name)
            .collect::<Vec<_>>()
            .join(", "),
    ))
}

fn detected_crs(layer: &Value) -> Option<String> {
    let coordinate_system = layer.get("coordinateSystem").or_else(|| {
        layer
            .get("geometryFields")
            .and_then(Value::as_array)
            .and_then(|fields| fields.first())
            .and_then(|field| field.get("coordinateSystem"))
    })?;
    if let Some(projjson) = coordinate_system.get("projjson") {
        if let Some(id) = projjson.get("id") {
            let authority = id.get("authority").and_then(Value::as_str);
            let code = id.get("code").and_then(value_text);
            if let (Some(authority), Some(code)) = (authority, code) {
                return Some(format!("{}:{}", authority.to_ascii_uppercase(), code));
            }
        }
    }
    coordinate_system
        .get("wkt")
        .and_then(Value::as_str)
        .filter(|value| !value.trim().is_empty() && value.len() <= 8_192)
        .map(|value| value.to_string())
}

fn value_text(value: &Value) -> Option<String> {
    match value {
        Value::String(value) if !value.trim().is_empty() => Some(value.trim().to_string()),
        Value::Number(value) => Some(value.to_string()),
        _ => None,
    }
}

fn explicit_crs(value: &str) -> Option<&str> {
    let value = value.trim();
    (!value.is_empty() && !value.eq_ignore_ascii_case("AUTO")).then_some(value)
}

fn executables(binary_directory: &str) -> Result<(PathBuf, PathBuf), GdalAdapterError> {
    let directory = binary_directory.trim();
    if directory.is_empty() || directory.starts_with("slot:") {
        return Ok((PathBuf::from("ogrinfo"), PathBuf::from("ogr2ogr")));
    }
    let directory = PathBuf::from(directory);
    if !directory.is_dir() {
        return Err(GdalAdapterError::InvalidBinaryDirectory(
            directory.to_string_lossy().into_owned(),
        ));
    }
    let directory = std::fs::canonicalize(&directory).map_err(|_| {
        GdalAdapterError::InvalidBinaryDirectory(directory.to_string_lossy().into_owned())
    })?;
    #[cfg(windows)]
    return Ok((directory.join("ogrinfo.exe"), directory.join("ogr2ogr.exe")));
    #[cfg(not(windows))]
    Ok((directory.join("ogrinfo"), directory.join("ogr2ogr")))
}

fn ensure_success(executable: &Path, result: &ProcessResult) -> Result<(), GdalAdapterError> {
    if result.success {
        return Ok(());
    }
    Err(GdalAdapterError::Failed {
        executable: executable.to_string_lossy().into_owned(),
        detail: bounded_detail(&result.stderr),
    })
}

fn read_bounded(reader: impl Read, limit: u64) -> Result<Vec<u8>, GdalAdapterError> {
    let mut bytes = Vec::new();
    reader
        .take(limit + 1)
        .read_to_end(&mut bytes)
        .map_err(|error| GdalAdapterError::InvalidMetadata(error.to_string()))?;
    if bytes.len() as u64 > limit {
        return Err(GdalAdapterError::OutputTooLarge {
            executable: "GDAL".to_string(),
        });
    }
    Ok(bytes)
}

fn bounded_detail(bytes: &[u8]) -> String {
    let detail = String::from_utf8_lossy(bytes).trim().to_string();
    if detail.is_empty() {
        "process returned a non-zero exit status".to_string()
    } else {
        detail.chars().take(2_000).collect()
    }
}

#[cfg(test)]
mod tests {
    use super::{
        normalize_with_executor, normalize_with_gdal, GdalAdapterError, GdalExecutor,
        GdalNormalizeRequest, GdalRuntimeEnvironment, ProcessResult,
    };
    use std::ffi::OsString;
    use std::fs;
    use std::path::{Path, PathBuf};
    use std::sync::Mutex;

    struct FakeExecutor {
        calls: Mutex<Vec<GdalCall>>,
        results: Mutex<Vec<ProcessResult>>,
    }

    struct GdalCall {
        executable: String,
        args: Vec<String>,
        environment: Vec<(String, String)>,
        removed_environment: Vec<String>,
    }

    impl GdalExecutor for FakeExecutor {
        fn run(
            &self,
            executable: &Path,
            args: &[OsString],
            environment: &super::GdalProcessEnvironment,
            _limit: u64,
        ) -> Result<ProcessResult, GdalAdapterError> {
            self.calls.lock().unwrap().push(GdalCall {
                executable: executable.to_string_lossy().into_owned(),
                args: args
                    .iter()
                    .map(|value| value.to_string_lossy().into_owned())
                    .collect(),
                environment: environment
                    .values
                    .iter()
                    .map(|(name, value)| {
                        (
                            name.to_string_lossy().into_owned(),
                            value.to_string_lossy().into_owned(),
                        )
                    })
                    .collect(),
                removed_environment: environment
                    .removed
                    .iter()
                    .map(|name| name.to_string_lossy().into_owned())
                    .collect(),
            });
            Ok(self.results.lock().unwrap().remove(0))
        }
    }

    #[test]
    fn detects_one_layer_crs_and_builds_confined_wgs84_conversion() {
        let executor = FakeExecutor {
            calls: Mutex::new(Vec::new()),
            results: Mutex::new(vec![
                success(br#"{"layers":[{"name":"signals","geometryFields":[{"coordinateSystem":{"projjson":{"id":{"authority":"EPSG","code":26917}}}}]}]}"#),
                success(br#"{"type":"FeatureCollection","features":[]}"#),
            ]),
        };
        let result = normalize_with_executor(&request("AUTO", ""), &executor).unwrap();

        assert_eq!(result.source_crs, "EPSG:26917");
        assert_eq!(result.layer_name, "signals");
        let calls = executor.calls.lock().unwrap();
        assert_eq!(calls[0].executable, "ogrinfo");
        assert!(calls[1]
            .args
            .windows(2)
            .any(|pair| pair == ["-t_srs", "EPSG:4326"]));
        assert!(!calls[1].args.contains(&"-s_srs".to_string()));
        assert_eq!(calls[1].args.last().map(String::as_str), Some("signals"));
    }

    #[test]
    fn requires_layer_for_multi_layer_data_and_honors_explicit_crs() {
        let metadata = br#"{"layers":[{"name":"signals"},{"name":"lanes"}]}"#;
        let ambiguous = FakeExecutor {
            calls: Mutex::new(Vec::new()),
            results: Mutex::new(vec![success(metadata)]),
        };
        assert!(matches!(
            normalize_with_executor(&request("AUTO", ""), &ambiguous),
            Err(GdalAdapterError::LayerRequired(_))
        ));

        let selected = FakeExecutor {
            calls: Mutex::new(Vec::new()),
            results: Mutex::new(vec![
                success(metadata),
                success(br#"{"type":"FeatureCollection","features":[]}"#),
            ]),
        };
        normalize_with_executor(&request("EPSG:32188", "lanes"), &selected).unwrap();
        let calls = selected.calls.lock().unwrap();
        assert!(calls[1]
            .args
            .windows(2)
            .any(|pair| pair == ["-s_srs", "EPSG:32188"]));
        assert_eq!(calls[1].args.last().map(String::as_str), Some("lanes"));
    }

    #[test]
    fn confines_managed_gdal_and_proj_data_to_the_ogr_children() {
        let root = std::env::temp_dir().join(format!(
            "roadwatcher-gdal-runtime-data-{}",
            uuid::Uuid::new_v4()
        ));
        let gdal_data = root.join("gdal-data");
        let proj_data = root.join("proj-data");
        fs::create_dir_all(&gdal_data).unwrap();
        fs::create_dir_all(&proj_data).unwrap();
        let executor = FakeExecutor {
            calls: Mutex::new(Vec::new()),
            results: Mutex::new(vec![
                success(br#"{"layers":[{"name":"signals","geometryFields":[{"coordinateSystem":{"projjson":{"id":{"authority":"EPSG","code":26917}}}}]}]}"#),
                success(br#"{"type":"FeatureCollection","features":[]}"#),
            ]),
        };
        let mut request = request("AUTO", "");
        request.runtime_environment = GdalRuntimeEnvironment {
            gdal_data_directory: Some(gdal_data.canonicalize().unwrap()),
            proj_data_directory: Some(proj_data.canonicalize().unwrap()),
        };
        normalize_with_executor(&request, &executor).unwrap();
        let calls = executor.calls.lock().unwrap();
        let gdal_data = gdal_data.canonicalize().unwrap().display().to_string();
        let proj_data = proj_data.canonicalize().unwrap().display().to_string();
        for call in calls.iter() {
            assert!(call
                .environment
                .iter()
                .any(|(name, value)| name == "GDAL_DATA" && value == &gdal_data));
            assert!(call
                .environment
                .iter()
                .any(|(name, value)| name == "PROJ_DATA" && value == &proj_data));
            assert!(call
                .environment
                .iter()
                .any(|(name, value)| { name == "GDAL_DRIVER_PATH" && value == "disable" }));
            assert!(call
                .environment
                .iter()
                .any(|(name, value)| name == "PROJ_NETWORK" && value == "OFF"));
            for removed in [
                "GDAL_CONFIG_FILE",
                "GDAL_PYTHON_DRIVER_PATH",
                "GDAL_SKIP",
                "PROJ_LIB",
                "PROJ_NETWORK_ENDPOINT",
                "PYTHONSO",
            ] {
                assert!(call.removed_environment.iter().any(|name| name == removed));
            }
        }
        fs::remove_dir_all(root).unwrap();
    }

    #[test]
    fn rejects_a_partial_managed_gdal_runtime() {
        let root = std::env::temp_dir().join(format!(
            "roadwatcher-gdal-runtime-data-{}",
            uuid::Uuid::new_v4()
        ));
        fs::create_dir_all(&root).unwrap();
        let executor = FakeExecutor {
            calls: Mutex::new(Vec::new()),
            results: Mutex::new(Vec::new()),
        };
        let mut request = request("AUTO", "");
        request.runtime_environment = GdalRuntimeEnvironment {
            gdal_data_directory: Some(root.clone()),
            proj_data_directory: None,
        };
        assert!(matches!(
            normalize_with_executor(&request, &executor),
            Err(GdalAdapterError::IncompleteRuntimeEnvironment)
        ));
        assert!(executor.calls.lock().unwrap().is_empty());
        fs::remove_dir_all(root).unwrap();
    }

    #[test]
    #[ignore = "requires an installed GDAL/OGR binary directory in ROADWATCHER_GDAL_BIN"]
    fn real_gdal_normalizes_projected_data_to_wgs84() {
        let binary_directory = std::env::var("ROADWATCHER_GDAL_BIN")
            .expect("ROADWATCHER_GDAL_BIN must identify the directory containing ogrinfo/ogr2ogr");
        let root =
            std::env::temp_dir().join(format!("roadwatcher-real-gdal-{}", uuid::Uuid::new_v4()));
        fs::create_dir_all(&root).unwrap();
        let source = root.join("signals.geojson");
        fs::write(
            &source,
            r#"{"type":"FeatureCollection","name":"signals","crs":{"type":"name","properties":{"name":"urn:ogc:def:crs:EPSG::26917"}},"features":[{"type":"Feature","properties":{"kind":"traffic_signal"},"geometry":{"type":"Point","coordinates":[630000,4860000]}}]}"#,
        )
        .unwrap();

        let result = normalize_with_gdal(&GdalNormalizeRequest {
            source_path: source,
            source_crs: "AUTO".to_string(),
            layer_name: String::new(),
            binary_directory,
            runtime_environment: GdalRuntimeEnvironment::default(),
        })
        .unwrap();
        let geojson: serde_json::Value = serde_json::from_str(&result.geojson).unwrap();
        let coordinates = geojson["features"][0]["geometry"]["coordinates"]
            .as_array()
            .unwrap();
        let longitude = coordinates[0].as_f64().unwrap();
        let latitude = coordinates[1].as_f64().unwrap();
        assert_eq!(result.source_crs, "EPSG:26917");
        assert_eq!(result.layer_name, "signals");
        assert!((-80.0..=-78.0).contains(&longitude));
        assert!((43.0..=45.0).contains(&latitude));

        fs::remove_dir_all(root).unwrap();
    }

    fn request(source_crs: &str, layer_name: &str) -> GdalNormalizeRequest {
        GdalNormalizeRequest {
            source_path: PathBuf::from("D:/GIS/roads.gpkg"),
            source_crs: source_crs.to_string(),
            layer_name: layer_name.to_string(),
            binary_directory: String::new(),
            runtime_environment: GdalRuntimeEnvironment::default(),
        }
    }

    fn success(stdout: &[u8]) -> ProcessResult {
        ProcessResult {
            success: true,
            stdout: stdout.to_vec(),
            stderr: Vec::new(),
        }
    }
}
