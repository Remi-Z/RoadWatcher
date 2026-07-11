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

#[derive(Clone, Debug)]
pub struct GdalNormalizeRequest {
    pub source_path: PathBuf,
    pub source_crs: String,
    pub layer_name: String,
    pub binary_directory: String,
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
        max_stdout_bytes: u64,
    ) -> Result<ProcessResult, GdalAdapterError>;
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
        max_stdout_bytes: u64,
    ) -> Result<ProcessResult, GdalAdapterError> {
        let executable_label = executable.to_string_lossy().into_owned();
        let mut child = Command::new(executable)
            .args(args)
            .stdin(Stdio::null())
            .stdout(Stdio::piped())
            .stderr(Stdio::piped())
            .spawn()
            .map_err(|error| GdalAdapterError::Launch {
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
    let converted = executor.run(&ogr2ogr, &args, MAX_GEOJSON_BYTES)?;
    ensure_success(&ogr2ogr, &converted)?;
    let geojson = String::from_utf8(converted.stdout)
        .map_err(|error| GdalAdapterError::InvalidMetadata(error.to_string()))?;
    Ok(GdalNormalizedDataset {
        geojson,
        source_crs,
        layer_name,
    })
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
        normalize_with_executor, GdalAdapterError, GdalExecutor, GdalNormalizeRequest,
        ProcessResult,
    };
    use std::ffi::OsString;
    use std::path::{Path, PathBuf};
    use std::sync::Mutex;

    struct FakeExecutor {
        calls: Mutex<Vec<(String, Vec<String>)>>,
        results: Mutex<Vec<ProcessResult>>,
    }

    impl GdalExecutor for FakeExecutor {
        fn run(
            &self,
            executable: &Path,
            args: &[OsString],
            _limit: u64,
        ) -> Result<ProcessResult, GdalAdapterError> {
            self.calls.lock().unwrap().push((
                executable.to_string_lossy().into_owned(),
                args.iter()
                    .map(|value| value.to_string_lossy().into_owned())
                    .collect(),
            ));
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
        assert_eq!(calls[0].0, "ogrinfo");
        assert!(calls[1]
            .1
            .windows(2)
            .any(|pair| pair == ["-t_srs", "EPSG:4326"]));
        assert!(!calls[1].1.contains(&"-s_srs".to_string()));
        assert_eq!(calls[1].1.last().map(String::as_str), Some("signals"));
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
            .1
            .windows(2)
            .any(|pair| pair == ["-s_srs", "EPSG:32188"]));
        assert_eq!(calls[1].1.last().map(String::as_str), Some("lanes"));
    }

    fn request(source_crs: &str, layer_name: &str) -> GdalNormalizeRequest {
        GdalNormalizeRequest {
            source_path: PathBuf::from("D:/GIS/roads.gpkg"),
            source_crs: source_crs.to_string(),
            layer_name: layer_name.to_string(),
            binary_directory: String::new(),
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
