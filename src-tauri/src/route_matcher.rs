use crate::bounded_process::run_bounded_process;
use crate::managed_valhalla_config::validate_portable_config;
use crate::project_store::{
    claim_route_match_job, complete_route_match_job, fail_route_match_job, read_route_match_status,
    update_route_match_progress, ProjectStoreError, RouteMatchRequest, RouteMatchStatus,
    RoutePoint,
};
use serde::Serialize;
use serde_json::{json, Value};
use std::fs;
use std::io::{Read, Write};
use std::net::TcpStream;
use std::path::{Path, PathBuf};
use std::process::Command;
use std::sync::{Arc, Mutex};
use std::time::Duration;
use thiserror::Error;
use uuid::Uuid;

const MAX_RESPONSE_BYTES: usize = 16 * 1024 * 1024;
const MAX_MATCHED_POINTS: usize = 1_000_000;
const MAX_CONFIG_BYTES: usize = 4 * 1024 * 1024;

#[derive(Clone, Debug)]
pub struct RouteMatcherRequest {
    pub store: RouteMatchRequest,
    pub matcher: String,
    pub valhalla_endpoint: String,
    pub osrm_endpoint: String,
    pub managed_valhalla: Option<ManagedValhallaRequest>,
}

#[derive(Clone, Debug)]
pub struct ManagedValhallaRequest {
    pub executable: PathBuf,
    pub config: PathBuf,
    pub tile_directory: PathBuf,
    pub work_root: PathBuf,
    pub matcher_version: String,
    pub tile_version: String,
    pub tile_sha256: String,
    pub config_sha256: String,
}

#[derive(Clone, Debug, Eq, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct RouteMatchStartResponse {
    pub job_id: String,
    pub status: String,
}

#[derive(Debug, Error)]
pub enum RouteMatcherError {
    #[error("Route matcher configuration is blocked: {0}")]
    Blocked(String),
    #[error("Route matcher transport failed: {0}")]
    Transport(String),
    #[error("Route matcher response is invalid: {0}")]
    InvalidResponse(String),
    #[error("Unsupported route matcher preference: {0}")]
    InvalidPreference(String),
    #[error("Route match state failed: {0}")]
    Store(#[from] ProjectStoreError),
    #[error("Route matcher synchronization failed.")]
    Synchronization,
    #[error("Route match job {0} is already active.")]
    AlreadyActive(String),
}

pub trait MatcherTransport: Send + Sync + 'static {
    fn post_json(
        &self,
        endpoint: &str,
        path: &str,
        body: &str,
    ) -> Result<String, RouteMatcherError>;
}

pub struct SystemLocalHttpTransport;

impl MatcherTransport for SystemLocalHttpTransport {
    fn post_json(
        &self,
        endpoint: &str,
        path: &str,
        body: &str,
    ) -> Result<String, RouteMatcherError> {
        let target = LocalHttpTarget::parse(endpoint, path)?;
        let mut stream = TcpStream::connect((&*target.host, target.port))
            .map_err(|error| RouteMatcherError::Transport(error.to_string()))?;
        stream
            .set_read_timeout(Some(Duration::from_secs(30)))
            .map_err(|error| RouteMatcherError::Transport(error.to_string()))?;
        stream
            .set_write_timeout(Some(Duration::from_secs(10)))
            .map_err(|error| RouteMatcherError::Transport(error.to_string()))?;
        let request = format!(
            "POST {} HTTP/1.1\r\nHost: {}\r\nContent-Type: application/json\r\nContent-Length: {}\r\nConnection: close\r\n\r\n{}",
            target.path,
            target.authority,
            body.len(),
            body
        );
        stream
            .write_all(request.as_bytes())
            .map_err(|error| RouteMatcherError::Transport(error.to_string()))?;
        let mut response = Vec::new();
        stream
            .take((MAX_RESPONSE_BYTES + 1) as u64)
            .read_to_end(&mut response)
            .map_err(|error| RouteMatcherError::Transport(error.to_string()))?;
        if response.len() > MAX_RESPONSE_BYTES {
            return Err(RouteMatcherError::Transport(
                "response exceeds 16 MiB".to_string(),
            ));
        }
        parse_http_response(&response)
    }
}

struct LocalHttpTarget {
    host: String,
    authority: String,
    port: u16,
    path: String,
}

impl LocalHttpTarget {
    fn parse(endpoint: &str, request_path: &str) -> Result<Self, RouteMatcherError> {
        let remainder = endpoint.trim().strip_prefix("http://").ok_or_else(|| {
            RouteMatcherError::Blocked("matcher endpoint must use local HTTP".to_string())
        })?;
        let (authority, base_path) = remainder.split_once('/').unwrap_or((remainder, ""));
        let (host, port) = authority
            .rsplit_once(':')
            .map(|(host, port)| {
                port.parse::<u16>()
                    .map(|port| (host.to_string(), port))
                    .map_err(|_| {
                        RouteMatcherError::Blocked("matcher endpoint port is invalid".to_string())
                    })
            })
            .unwrap_or_else(|| Ok((authority.to_string(), 80)))?;
        if !matches!(host.as_str(), "localhost" | "127.0.0.1") {
            return Err(RouteMatcherError::Blocked(
                "matcher endpoint must resolve through localhost or 127.0.0.1".to_string(),
            ));
        }
        let base_path = base_path.trim_matches('/');
        let path = if base_path.is_empty() {
            format!("/{}", request_path.trim_start_matches('/'))
        } else {
            format!("/{base_path}/{}", request_path.trim_start_matches('/'))
        };
        Ok(Self {
            host,
            authority: authority.to_string(),
            port,
            path,
        })
    }
}

fn parse_http_response(response: &[u8]) -> Result<String, RouteMatcherError> {
    let separator = response
        .windows(4)
        .position(|window| window == b"\r\n\r\n")
        .ok_or_else(|| {
            RouteMatcherError::Transport("HTTP response has no header boundary".to_string())
        })?;
    let headers = String::from_utf8_lossy(&response[..separator]);
    let status = headers
        .lines()
        .next()
        .and_then(|line| line.split_whitespace().nth(1))
        .and_then(|value| value.parse::<u16>().ok())
        .ok_or_else(|| {
            RouteMatcherError::Transport("HTTP response status is invalid".to_string())
        })?;
    if !(200..300).contains(&status) {
        return Err(RouteMatcherError::Transport(format!(
            "HTTP status {status}"
        )));
    }
    String::from_utf8(response[separator + 4..].to_vec())
        .map_err(|error| RouteMatcherError::InvalidResponse(error.to_string()))
}

pub fn execute_route_match<T: MatcherTransport>(
    transport: &T,
    request: &RouteMatcherRequest,
) -> Result<RouteMatchStatus, RouteMatcherError> {
    let result = (|| {
        let claimed = claim_route_match_job(&request.store)?;
        update_route_match_progress(&request.store, 10.0, "Preparing matcher request.")?;
        let (matcher_used, coordinates, detail) = match request.matcher.as_str() {
            "Valhalla" => {
                let valhalla = if !request.valhalla_endpoint.trim().is_empty() {
                    attempt_valhalla(transport, &request.valhalla_endpoint, &claimed.raw_route).map(
                        |points| {
                            (
                                points,
                                "Configured HTTP Valhalla route match complete.".to_string(),
                            )
                        },
                    )
                } else if let Some(managed) = &request.managed_valhalla {
                    attempt_managed_valhalla(managed, &claimed.raw_route).map(|points| {
                        (points, format!(
                            "Managed Valhalla route match complete; matcherVersion={}; tileVersion={}; tileSha256={}; configSha256={}.",
                            managed.matcher_version, managed.tile_version, managed.tile_sha256, managed.config_sha256
                        ))
                    })
                } else {
                    Err(RouteMatcherError::Blocked(
                        "Valhalla endpoint is not configured and managed Valhalla is not ready"
                            .to_string(),
                    ))
                };
                match valhalla {
                    Ok((points, detail)) => ("Valhalla", points, detail),
                    Err(valhalla_error) if !request.osrm_endpoint.trim().is_empty() => {
                        update_route_match_progress(
                            &request.store,
                            45.0,
                            "Valhalla unavailable; trying OSRM Match.",
                        )?;
                        let points =
                            attempt_osrm(transport, &request.osrm_endpoint, &claimed.raw_route)
                                .map_err(|osrm_error| {
                                    RouteMatcherError::Transport(format!(
                                "Valhalla failed ({valhalla_error}); OSRM failed ({osrm_error})"
                            ))
                                })?;
                        (
                            "OSRM",
                            points,
                            format!("OSRM fallback used after: {valhalla_error}"),
                        )
                    }
                    Err(error) => return Err(error),
                }
            }
            "OSRM" => (
                "OSRM",
                attempt_osrm(transport, &request.osrm_endpoint, &claimed.raw_route)?,
                "OSRM route match complete.".to_string(),
            ),
            other => return Err(RouteMatcherError::InvalidPreference(other.to_string())),
        };
        let matched = interpolate_times(&coordinates, &claimed.raw_route)?;
        update_route_match_progress(&request.store, 90.0, &detail)?;
        complete_route_match_job(&request.store, matcher_used, &matched, &detail)?;
        Ok(read_route_match_status(
            &request.store.sqlite_path,
            &request.store.project_id,
            &request.store.route_id,
            &request.store.job_id,
        )?)
    })();
    if let Err(error) = &result {
        let status = if matches!(error, RouteMatcherError::Blocked(_)) {
            "blocked"
        } else {
            "failed"
        };
        let _ = fail_route_match_job(&request.store, status, &error.to_string());
    }
    result
}

fn attempt_valhalla<T: MatcherTransport>(
    transport: &T,
    endpoint: &str,
    raw: &[RoutePoint],
) -> Result<Vec<(f64, f64)>, RouteMatcherError> {
    if endpoint.trim().is_empty() {
        return Err(RouteMatcherError::Blocked(
            "Valhalla endpoint is not configured".to_string(),
        ));
    }
    let locations: Vec<Value> = raw
        .iter()
        .map(|point| json!({ "lat": point.latitude, "lon": point.longitude, "time": point.time_seconds }))
        .collect();
    let response = transport.post_json(
        endpoint,
        "/trace_attributes",
        &json!({ "shape": locations, "costing": "auto", "shape_match": "map_snap" }).to_string(),
    )?;
    let document: Value = serde_json::from_str(&response)
        .map_err(|error| RouteMatcherError::InvalidResponse(error.to_string()))?;
    coordinates_from_objects(document.get("matched_points"), "lat", "lon")
}

fn attempt_managed_valhalla(
    managed: &ManagedValhallaRequest,
    raw: &[RoutePoint],
) -> Result<Vec<(f64, f64)>, RouteMatcherError> {
    if !managed.executable.is_file()
        || !managed.config.is_file()
        || !managed.tile_directory.is_dir()
    {
        return Err(RouteMatcherError::Blocked(
            "managed Valhalla executable, configuration, or tile directory is missing".to_string(),
        ));
    }
    fs::create_dir_all(&managed.work_root)
        .map_err(|error| RouteMatcherError::Transport(error.to_string()))?;
    let work = managed.work_root.join(format!("match-{}", Uuid::new_v4()));
    fs::create_dir(&work).map_err(|error| RouteMatcherError::Transport(error.to_string()))?;
    let result = (|| {
        let request_path = work.join("trace-attributes-request.json");
        let result_path = work.join("trace-attributes-result.json");
        let runtime_config_path = work.join("valhalla.runtime.json");
        materialize_managed_valhalla_config(
            &managed.config,
            &managed.tile_directory,
            &runtime_config_path,
        )?;
        let locations: Vec<Value> = raw
            .iter()
            .map(|point| json!({ "lat": point.latitude, "lon": point.longitude, "time": point.time_seconds }))
            .collect();
        let request = serde_json::to_vec(
            &json!({ "shape": locations, "costing": "auto", "shape_match": "map_snap" }),
        )
        .map_err(|error| RouteMatcherError::InvalidResponse(error.to_string()))?;
        if request.len() > MAX_RESPONSE_BYTES {
            return Err(RouteMatcherError::InvalidResponse(
                "managed Valhalla request exceeds 16 MiB".to_string(),
            ));
        }
        fs::write(&request_path, request)
            .map_err(|error| RouteMatcherError::Transport(error.to_string()))?;
        let output = run_bounded_process(
            Command::new(&managed.executable)
                .arg(&runtime_config_path)
                .arg("trace_attributes")
                .arg(&request_path),
            Duration::from_secs(90),
            MAX_RESPONSE_BYTES as u64,
        )
        .map_err(|error| RouteMatcherError::Transport(error.to_string()))?;
        if !output.status.success() {
            let detail = String::from_utf8_lossy(&output.stderr);
            return Err(RouteMatcherError::Transport(format!(
                "managed Valhalla exited with {}; {}",
                output.status,
                detail.chars().take(1_000).collect::<String>()
            )));
        }
        fs::write(&result_path, &output.stdout)
            .map_err(|error| RouteMatcherError::Transport(error.to_string()))?;
        let document: Value = serde_json::from_slice(&output.stdout)
            .map_err(|error| RouteMatcherError::InvalidResponse(error.to_string()))?;
        coordinates_from_objects(document.get("matched_points"), "lat", "lon")
    })();
    let _ = fs::remove_dir_all(work);
    result
}

fn materialize_managed_valhalla_config(
    template_path: &Path,
    tile_directory: &Path,
    output_path: &Path,
) -> Result<(), RouteMatcherError> {
    let template =
        fs::read(template_path).map_err(|error| RouteMatcherError::Transport(error.to_string()))?;
    if template.len() > MAX_CONFIG_BYTES {
        return Err(RouteMatcherError::InvalidResponse(
            "managed Valhalla configuration exceeds 4 MiB".to_string(),
        ));
    }
    let mut document: Value = serde_json::from_slice(&template)
        .map_err(|error| RouteMatcherError::InvalidResponse(error.to_string()))?;
    validate_portable_config(&document).map_err(RouteMatcherError::InvalidResponse)?;
    let mjolnir = document
        .get_mut("mjolnir")
        .and_then(Value::as_object_mut)
        .ok_or_else(|| {
            RouteMatcherError::InvalidResponse(
                "managed Valhalla configuration is missing mjolnir".to_string(),
            )
        })?;
    mjolnir.remove("tile_extract");
    let canonical_tiles = tile_directory
        .canonicalize()
        .map_err(|error| RouteMatcherError::Transport(error.to_string()))?;
    if !canonical_tiles.is_dir() {
        return Err(RouteMatcherError::Blocked(
            "managed Valhalla tile directory is missing".to_string(),
        ));
    }
    mjolnir.insert(
        "tile_dir".to_string(),
        Value::String(canonical_tiles.to_string_lossy().into_owned()),
    );
    let materialized = serde_json::to_vec(&document)
        .map_err(|error| RouteMatcherError::InvalidResponse(error.to_string()))?;
    if materialized.len() > MAX_CONFIG_BYTES {
        return Err(RouteMatcherError::InvalidResponse(
            "materialized Valhalla configuration exceeds 4 MiB".to_string(),
        ));
    }
    fs::write(output_path, materialized)
        .map_err(|error| RouteMatcherError::Transport(error.to_string()))
}

fn attempt_osrm<T: MatcherTransport>(
    transport: &T,
    endpoint: &str,
    raw: &[RoutePoint],
) -> Result<Vec<(f64, f64)>, RouteMatcherError> {
    if endpoint.trim().is_empty() {
        return Err(RouteMatcherError::Blocked(
            "OSRM endpoint is not configured".to_string(),
        ));
    }
    let coordinates = raw
        .iter()
        .map(|point| format!("{},{}", point.longitude, point.latitude))
        .collect::<Vec<_>>()
        .join(";");
    let timestamps = raw
        .iter()
        .map(|point| point.time_seconds.round().to_string())
        .collect::<Vec<_>>()
        .join(";");
    let path = format!(
        "/match/v1/driving/{coordinates}?overview=full&geometries=geojson&timestamps={timestamps}"
    );
    let response = transport.post_json(endpoint, &path, "{}")?;
    let document: Value = serde_json::from_str(&response)
        .map_err(|error| RouteMatcherError::InvalidResponse(error.to_string()))?;
    let values = document
        .pointer("/matchings/0/geometry/coordinates")
        .and_then(Value::as_array)
        .ok_or_else(|| {
            RouteMatcherError::InvalidResponse("OSRM geometry is missing".to_string())
        })?;
    bounded_coordinates(values.iter().map(|value| {
        let pair = value.as_array()?;
        Some((pair.get(1)?.as_f64()?, pair.first()?.as_f64()?))
    }))
}

fn coordinates_from_objects(
    value: Option<&Value>,
    latitude_field: &str,
    longitude_field: &str,
) -> Result<Vec<(f64, f64)>, RouteMatcherError> {
    let values = value.and_then(Value::as_array).ok_or_else(|| {
        RouteMatcherError::InvalidResponse("Valhalla matched_points are missing".to_string())
    })?;
    bounded_coordinates(values.iter().map(|value| {
        Some((
            value.get(latitude_field)?.as_f64()?,
            value.get(longitude_field)?.as_f64()?,
        ))
    }))
}

fn bounded_coordinates(
    values: impl Iterator<Item = Option<(f64, f64)>>,
) -> Result<Vec<(f64, f64)>, RouteMatcherError> {
    let mut points = Vec::new();
    for value in values {
        let (latitude, longitude) = value.ok_or_else(|| {
            RouteMatcherError::InvalidResponse("matched coordinate is invalid".to_string())
        })?;
        if !latitude.is_finite()
            || !longitude.is_finite()
            || !(-90.0..=90.0).contains(&latitude)
            || !(-180.0..=180.0).contains(&longitude)
        {
            return Err(RouteMatcherError::InvalidResponse(
                "matched coordinate is out of range".to_string(),
            ));
        }
        if points
            .last()
            .is_none_or(|last| *last != (latitude, longitude))
        {
            points.push((latitude, longitude));
        }
        if points.len() > MAX_MATCHED_POINTS {
            return Err(RouteMatcherError::InvalidResponse(
                "matched route has too many points".to_string(),
            ));
        }
    }
    if points.len() < 2 {
        return Err(RouteMatcherError::InvalidResponse(
            "matched route needs at least two distinct points".to_string(),
        ));
    }
    Ok(points)
}

fn interpolate_times(
    coordinates: &[(f64, f64)],
    raw: &[RoutePoint],
) -> Result<Vec<RoutePoint>, RouteMatcherError> {
    let duration = raw.last().map(|point| point.time_seconds).unwrap_or(0.0);
    if !duration.is_finite() || duration <= 0.0 {
        return Err(RouteMatcherError::InvalidResponse(
            "raw route duration is invalid".to_string(),
        ));
    }
    let mut cumulative = vec![0.0];
    for pair in coordinates.windows(2) {
        cumulative
            .push(cumulative.last().copied().unwrap_or(0.0) + haversine_meters(pair[0], pair[1]));
    }
    let distance = cumulative.last().copied().unwrap_or(0.0);
    if !distance.is_finite() || distance <= 0.0 {
        return Err(RouteMatcherError::InvalidResponse(
            "matched route distance is zero".to_string(),
        ));
    }
    Ok(coordinates
        .iter()
        .zip(cumulative)
        .map(|(&(latitude, longitude), traveled)| RoutePoint {
            latitude,
            longitude,
            time_seconds: traveled / distance * duration,
        })
        .collect())
}

fn haversine_meters(left: (f64, f64), right: (f64, f64)) -> f64 {
    let radius = 6_371_000.0;
    let lat1 = left.0.to_radians();
    let lat2 = right.0.to_radians();
    let delta_lat = (right.0 - left.0).to_radians();
    let delta_lon = (right.1 - left.1).to_radians();
    let a =
        (delta_lat / 2.0).sin().powi(2) + lat1.cos() * lat2.cos() * (delta_lon / 2.0).sin().powi(2);
    radius * 2.0 * a.sqrt().atan2((1.0 - a).sqrt())
}

pub struct RouteMatcherManager<T: MatcherTransport = SystemLocalHttpTransport> {
    transport: Arc<T>,
    execution_lock: Arc<Mutex<()>>,
    active_job: Arc<Mutex<Option<String>>>,
}

impl Default for RouteMatcherManager<SystemLocalHttpTransport> {
    fn default() -> Self {
        Self::new(Arc::new(SystemLocalHttpTransport))
    }
}

impl<T: MatcherTransport> RouteMatcherManager<T> {
    pub fn new(transport: Arc<T>) -> Self {
        Self {
            transport,
            execution_lock: Arc::new(Mutex::new(())),
            active_job: Arc::new(Mutex::new(None)),
        }
    }

    pub fn start(
        &self,
        request: RouteMatcherRequest,
    ) -> Result<RouteMatchStartResponse, RouteMatcherError> {
        let current = read_route_match_status(
            &request.store.sqlite_path,
            &request.store.project_id,
            &request.store.route_id,
            &request.store.job_id,
        )?;
        {
            let mut active = self
                .active_job
                .lock()
                .map_err(|_| RouteMatcherError::Synchronization)?;
            if active.is_some() {
                return Err(RouteMatcherError::AlreadyActive(request.store.job_id));
            }
            *active = Some(request.store.job_id.clone());
        }
        let transport = self.transport.clone();
        let execution_lock = self.execution_lock.clone();
        let active_job = self.active_job.clone();
        std::thread::spawn(move || {
            let result = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
                let _guard = execution_lock
                    .lock()
                    .map_err(|_| RouteMatcherError::Synchronization)?;
                execute_route_match(transport.as_ref(), &request)
            }));
            if result.is_err() {
                let _ = fail_route_match_job(&request.store, "failed", "Route matcher panicked.");
            }
            if let Ok(mut active) = active_job.lock() {
                *active = None;
            }
        });
        Ok(RouteMatchStartResponse {
            job_id: current.job_id,
            status: current.status,
        })
    }
}

#[cfg(test)]
mod tests {
    use super::{
        execute_route_match, materialize_managed_valhalla_config, MatcherTransport,
        RouteMatcherError, RouteMatcherManager, RouteMatcherRequest, SystemLocalHttpTransport,
    };
    use crate::project_store::{
        create_project_at, import_route_at, ProjectCreateRequest, RouteImportRequest,
        RouteMatchRequest, RoutePoint,
    };
    use serde_json::Value;
    use std::collections::VecDeque;
    use std::fs;
    use std::path::PathBuf;
    use std::sync::{Arc, Mutex};
    use std::time::Duration;
    use uuid::Uuid;

    #[test]
    fn completes_valhalla_and_interpolates_monotonic_times() {
        let fixture = Fixture::new();
        let transport = FakeTransport::new([Ok(
            r#"{"matched_points":[{"lat":43.1,"lon":-79.2},{"lat":43.15,"lon":-79.15},{"lat":43.2,"lon":-79.1}]}"#,
        )]);
        let status = execute_route_match(
            &transport,
            &fixture.request("Valhalla", "http://localhost:8002", ""),
        )
        .unwrap();
        assert_eq!(status.status, "complete");
        assert_eq!(status.matcher_used, "Valhalla");
        assert_eq!(status.route.first().unwrap().time_seconds, 0.0);
        assert_eq!(status.route.last().unwrap().time_seconds, 10.0);
    }

    #[test]
    fn completes_osrm_geometry() {
        let fixture = Fixture::new();
        let transport = FakeTransport::new([Ok(
            r#"{"matchings":[{"geometry":{"coordinates":[[-79.2,43.1],[-79.1,43.2]]}}]}"#,
        )]);
        let status = execute_route_match(
            &transport,
            &fixture.request("OSRM", "", "http://localhost:5000"),
        )
        .unwrap();
        assert_eq!(status.matcher_used, "OSRM");
        assert_eq!(status.route.len(), 2);
    }

    #[test]
    #[ignore = "requires a live loopback OSRM service in ROADWATCHER_OSRM_ENDPOINT"]
    fn real_osrm_service_completes_durable_route_match() {
        let endpoint = std::env::var("ROADWATCHER_OSRM_ENDPOINT")
            .expect("ROADWATCHER_OSRM_ENDPOINT must identify a loopback OSRM service");
        let fixture = Fixture::new();
        let status = execute_route_match(
            &SystemLocalHttpTransport,
            &fixture.request("OSRM", "", &endpoint),
        )
        .unwrap();
        assert_eq!(status.status, "complete");
        assert_eq!(status.matcher_used, "OSRM");
        assert_eq!(status.route.first().unwrap().time_seconds, 0.0);
        assert_eq!(status.route.last().unwrap().time_seconds, 10.0);
        assert!(status.route.len() >= 3);
        assert!(status
            .route
            .windows(2)
            .all(|pair| pair[0].time_seconds < pair[1].time_seconds));
    }

    #[test]
    fn falls_back_from_valhalla_to_osrm() {
        let fixture = Fixture::new();
        let transport = FakeTransport::new([
            Err(RouteMatcherError::Transport("Valhalla offline".to_string())),
            Ok(r#"{"matchings":[{"geometry":{"coordinates":[[-79.2,43.1],[-79.1,43.2]]}}]}"#),
        ]);
        let status = execute_route_match(
            &transport,
            &fixture.request("Valhalla", "http://localhost:8002", "http://localhost:5000"),
        )
        .unwrap();
        assert_eq!(status.matcher_used, "OSRM");
    }

    #[test]
    fn falls_back_to_osrm_when_managed_valhalla_is_unavailable() {
        let fixture = Fixture::new();
        let transport = FakeTransport::new([Ok(
            r#"{"matchings":[{"geometry":{"coordinates":[[-79.2,43.1],[-79.1,43.2]]}}]}"#,
        )]);
        let status = execute_route_match(
            &transport,
            &fixture.request("Valhalla", "", "http://localhost:5000"),
        )
        .unwrap();
        assert_eq!(status.matcher_used, "OSRM");
    }

    #[test]
    fn missing_configuration_blocks_and_malformed_response_fails() {
        let blocked = Fixture::new();
        let transport = FakeTransport::new([]);
        assert!(matches!(
            execute_route_match(&transport, &blocked.request("Valhalla", "", "")),
            Err(RouteMatcherError::Blocked(_))
        ));
        let status = blocked.status();
        assert_eq!(status.status, "blocked");

        let malformed = Fixture::new();
        let transport = FakeTransport::new([Ok("{}")]);
        assert!(matches!(
            execute_route_match(
                &transport,
                &malformed.request("Valhalla", "http://localhost:8002", "")
            ),
            Err(RouteMatcherError::InvalidResponse(_))
        ));
        assert_eq!(malformed.status().status, "failed");
    }

    #[test]
    fn materializes_only_the_portable_managed_tile_directory() {
        let root =
            std::env::temp_dir().join(format!("roadwatcher-valhalla-config-{}", Uuid::new_v4()));
        let tiles = root.join("tiles");
        fs::create_dir_all(&tiles).unwrap();
        let template = root.join("valhalla.json");
        let output = root.join("runtime.json");
        fs::write(
            &template,
            br#"{"mjolnir":{"tile_dir":"${ROADWATCHER_TILE_DIR}"},"service_limits":{}}"#,
        )
        .unwrap();
        materialize_managed_valhalla_config(&template, &tiles, &output).unwrap();
        let document: Value = serde_json::from_slice(&fs::read(&output).unwrap()).unwrap();
        let expected_tiles = tiles.canonicalize().unwrap().to_string_lossy().into_owned();
        assert_eq!(
            document
                .pointer("/mjolnir/tile_dir")
                .and_then(Value::as_str),
            Some(expected_tiles.as_str())
        );

        fs::write(&template, br#"{"mjolnir":{"tile_dir":"C:/untrusted"}}"#).unwrap();
        assert!(matches!(
            materialize_managed_valhalla_config(&template, &tiles, &output),
            Err(RouteMatcherError::InvalidResponse(detail)) if detail.contains("portable")
        ));

        fs::write(
            &template,
            br#"{"mjolnir":{"tile_dir":"${ROADWATCHER_TILE_DIR}","tile_extract":"other.tar"}}"#,
        )
        .unwrap();
        assert!(matches!(
            materialize_managed_valhalla_config(&template, &tiles, &output),
            Err(RouteMatcherError::InvalidResponse(detail)) if detail.contains("tile_extract")
        ));

        fs::write(
            &template,
            br#"{"mjolnir":{"tile_dir":"${ROADWATCHER_TILE_DIR}","timezones":"C:/untrusted"}}"#,
        )
        .unwrap();
        assert!(matches!(
            materialize_managed_valhalla_config(&template, &tiles, &output),
            Err(RouteMatcherError::InvalidResponse(detail)) if detail.contains("timezones")
        ));
        fs::remove_dir_all(root).unwrap();
    }

    #[test]
    fn manager_returns_promptly_and_completes_in_background() {
        let fixture = Fixture::new();
        let manager = RouteMatcherManager::new(Arc::new(FakeTransport::new([Ok(
            r#"{"matched_points":[{"lat":43.1,"lon":-79.2},{"lat":43.2,"lon":-79.1}]}"#,
        )])));
        let started = manager
            .start(fixture.request("Valhalla", "http://localhost:8002", ""))
            .unwrap();
        assert_eq!(started.status, "queued");
        let deadline = std::time::Instant::now() + Duration::from_secs(2);
        loop {
            let status = fixture.status();
            if status.status == "complete" {
                break;
            }
            assert!(
                std::time::Instant::now() < deadline,
                "route matcher did not complete"
            );
            std::thread::sleep(Duration::from_millis(10));
        }
    }

    struct FakeTransport {
        responses: Mutex<VecDeque<Result<String, RouteMatcherError>>>,
    }

    impl FakeTransport {
        fn new<const N: usize>(responses: [Result<&str, RouteMatcherError>; N]) -> Self {
            Self {
                responses: Mutex::new(
                    responses
                        .into_iter()
                        .map(|result| result.map(str::to_string))
                        .collect(),
                ),
            }
        }
    }

    impl MatcherTransport for FakeTransport {
        fn post_json(
            &self,
            _endpoint: &str,
            _path: &str,
            _body: &str,
        ) -> Result<String, RouteMatcherError> {
            self.responses.lock().unwrap().pop_front().unwrap()
        }
    }

    struct Fixture {
        root: PathBuf,
        store: RouteMatchRequest,
    }

    impl Fixture {
        fn new() -> Self {
            let root =
                std::env::temp_dir().join(format!("roadwatcher-route-matcher-{}", Uuid::new_v4()));
            fs::create_dir_all(&root).unwrap();
            let project_id = Uuid::new_v4();
            let route_id = Uuid::new_v4();
            let job_id = Uuid::new_v4();
            let created = create_project_at(
                ProjectCreateRequest {
                    project_name: "Matcher".to_string(),
                    root_directory: root.clone(),
                },
                project_id,
                1_788_000_000,
            )
            .unwrap();
            let source = root.join("route.gpx");
            fs::write(&source, b"route").unwrap();
            import_route_at(
                RouteImportRequest {
                    sqlite_path: created.sqlite_path.clone().into(),
                    project_id: project_id.to_string(),
                    source_path: source,
                    points: vec![
                        RoutePoint {
                            latitude: 43.1,
                            longitude: -79.2,
                            time_seconds: 0.0,
                        },
                        RoutePoint {
                            latitude: 43.2,
                            longitude: -79.1,
                            time_seconds: 10.0,
                        },
                    ],
                },
                route_id,
                job_id,
                1_788_000_001,
            )
            .unwrap();
            Self {
                root,
                store: RouteMatchRequest {
                    sqlite_path: created.sqlite_path.into(),
                    project_id: project_id.to_string(),
                    route_id: route_id.to_string(),
                    job_id: job_id.to_string(),
                },
            }
        }

        fn request(&self, matcher: &str, valhalla: &str, osrm: &str) -> RouteMatcherRequest {
            RouteMatcherRequest {
                store: self.store.clone(),
                matcher: matcher.to_string(),
                valhalla_endpoint: valhalla.to_string(),
                osrm_endpoint: osrm.to_string(),
                managed_valhalla: None,
            }
        }

        fn status(&self) -> crate::project_store::RouteMatchStatus {
            crate::project_store::read_route_match_status(
                &self.store.sqlite_path,
                &self.store.project_id,
                &self.store.route_id,
                &self.store.job_id,
            )
            .unwrap()
        }
    }

    impl Drop for Fixture {
        fn drop(&mut self) {
            let _ = fs::remove_dir_all(&self.root);
        }
    }
}
