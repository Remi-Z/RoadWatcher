use crate::project_store::{
    import_feature_source_at, FeatureImportRequest, FeatureImportResponse,
    NormalizedOfficialFeature, ProjectStoreError,
};
use serde_json::{Map, Value};
use std::fs::File;
use std::io::Read;
use std::path::PathBuf;
use std::time::{SystemTime, UNIX_EPOCH};
use thiserror::Error;
use uuid::Uuid;

const MAX_GIS_BYTES: u64 = 64 * 1024 * 1024;
const MAX_FEATURES: usize = 250_000;
const WEB_MERCATOR_RADIUS: f64 = 6_378_137.0;
const WEB_MERCATOR_LIMIT: f64 = 20_037_508.342_789_244;

#[derive(Debug)]
pub struct GisImportRequest {
    pub sqlite_path: PathBuf,
    pub project_id: String,
    pub source_path: PathBuf,
    pub source_crs: String,
    pub layer_kind: String,
}

#[derive(Debug, Error)]
pub enum GisImportError {
    #[error("Official GIS source must have a .geojson or .json extension.")]
    InvalidExtension,
    #[error("Official GIS source exceeds the 64 MiB import limit.")]
    SourceTooLarge,
    #[error("Official GIS source CRS must be EPSG:4326 or EPSG:3857.")]
    UnsupportedCrs,
    #[error("Official GIS GeoJSON is invalid: {0}")]
    InvalidGeoJson(String),
    #[error("Official GIS import did not contain supported road features.")]
    NoSupportedFeatures,
    #[error("Could not read official GIS source: {0}")]
    Io(#[from] std::io::Error),
    #[error("Could not persist official GIS source: {0}")]
    Store(#[from] ProjectStoreError),
    #[error("System clock is before the Unix epoch.")]
    InvalidSystemClock,
}

pub fn import_gis(request: GisImportRequest) -> Result<FeatureImportResponse, GisImportError> {
    if request
        .source_path
        .extension()
        .and_then(|value| value.to_str())
        .is_none_or(|value| !matches!(value.to_ascii_lowercase().as_str(), "geojson" | "json"))
    {
        return Err(GisImportError::InvalidExtension);
    }
    let mut bytes = Vec::new();
    File::open(&request.source_path)?
        .take(MAX_GIS_BYTES + 1)
        .read_to_end(&mut bytes)?;
    if bytes.len() as u64 > MAX_GIS_BYTES {
        return Err(GisImportError::SourceTooLarge);
    }
    let text = String::from_utf8(bytes)
        .map_err(|error| GisImportError::InvalidGeoJson(error.to_string()))?;
    let source_id = Uuid::new_v4();
    let features = parse_geojson_features(
        &text,
        &source_id.to_string(),
        &request.source_crs,
        &request.layer_kind,
        request
            .source_path
            .file_name()
            .and_then(|value| value.to_str())
            .unwrap_or("official.geojson"),
    )?;
    let imported_at_unix = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map_err(|_| GisImportError::InvalidSystemClock)?
        .as_secs() as i64;
    Ok(import_feature_source_at(
        FeatureImportRequest {
            sqlite_path: request.sqlite_path,
            project_id: request.project_id,
            source_path: request.source_path,
            source_crs: request.source_crs,
            layer_kind: request.layer_kind,
            features,
        },
        source_id,
        Uuid::new_v4(),
        imported_at_unix,
    )?)
}

pub(crate) fn parse_geojson_features(
    text: &str,
    source_id: &str,
    source_crs: &str,
    requested_layer_kind: &str,
    source_file_name: &str,
) -> Result<Vec<NormalizedOfficialFeature>, GisImportError> {
    if !matches!(source_crs, "EPSG:4326" | "EPSG:3857") {
        return Err(GisImportError::UnsupportedCrs);
    }
    let document: Value = serde_json::from_str(text)
        .map_err(|error| GisImportError::InvalidGeoJson(error.to_string()))?;
    if document.get("type").and_then(Value::as_str) != Some("FeatureCollection") {
        return Err(GisImportError::InvalidGeoJson(
            "root must be a FeatureCollection".to_string(),
        ));
    }
    let values = document
        .get("features")
        .and_then(Value::as_array)
        .ok_or_else(|| GisImportError::InvalidGeoJson("features must be an array".to_string()))?;
    if values.len() > MAX_FEATURES {
        return Err(GisImportError::InvalidGeoJson(
            "feature count exceeds 250000".to_string(),
        ));
    }
    let requested_kind = normalize_kind(requested_layer_kind);
    let mut features = Vec::new();
    for (index, value) in values.iter().enumerate() {
        let Some(feature) = normalize_feature(
            value,
            index,
            source_id,
            source_crs,
            requested_kind.as_deref(),
            source_file_name,
        )?
        else {
            continue;
        };
        features.push(feature);
    }
    if features.is_empty() {
        return Err(GisImportError::NoSupportedFeatures);
    }
    Ok(features)
}

fn normalize_feature(
    value: &Value,
    index: usize,
    source_id: &str,
    source_crs: &str,
    requested_kind: Option<&str>,
    source_file_name: &str,
) -> Result<Option<NormalizedOfficialFeature>, GisImportError> {
    if value.get("type").and_then(Value::as_str) != Some("Feature") {
        return Ok(None);
    }
    let empty_properties = Map::new();
    let properties = value
        .get("properties")
        .and_then(Value::as_object)
        .unwrap_or(&empty_properties);
    let feature_kind = ["kind", "type", "feature_type"].iter().find_map(|key| {
        properties
            .get(*key)
            .and_then(Value::as_str)
            .and_then(normalize_kind)
    });
    let kind = match (feature_kind, requested_kind) {
        (Some(kind), Some(requested)) if kind != requested => return Ok(None),
        (Some(kind), _) => kind,
        (None, Some(requested)) => requested.to_string(),
        (None, None) => return Ok(None),
    };
    let geometry = match value.get("geometry") {
        Some(Value::Object(geometry)) => geometry,
        _ => return Ok(None),
    };
    let geometry_type = match geometry.get("type").and_then(Value::as_str) {
        Some(kind @ ("Point" | "LineString")) => kind,
        _ => return Ok(None),
    };
    let coordinate = if geometry_type == "Point" {
        coordinate_pair(geometry.get("coordinates"))
    } else {
        geometry
            .get("coordinates")
            .and_then(Value::as_array)
            .and_then(|coordinates| coordinates.get(coordinates.len() / 2))
            .and_then(|value| coordinate_pair(Some(value)))
    };
    let Some((x, y)) = coordinate else {
        return Ok(None);
    };
    let (longitude, latitude) = normalize_coordinate(x, y, source_crs)?;
    let source_feature_id = value
        .get("id")
        .and_then(value_id)
        .or_else(|| properties.get("id").and_then(value_id))
        .unwrap_or_else(|| index.to_string());
    let source_layer = properties
        .get("sourceLayer")
        .and_then(Value::as_str)
        .or_else(|| properties.get("source").and_then(Value::as_str))
        .filter(|value| !value.trim().is_empty())
        .unwrap_or(source_file_name)
        .to_string();
    let properties_json = serde_json::to_string(properties)
        .map_err(|error| GisImportError::InvalidGeoJson(error.to_string()))?;
    if properties_json.len() > 64 * 1024 {
        return Err(GisImportError::InvalidGeoJson(
            "feature properties exceed 64 KiB".to_string(),
        ));
    }
    Ok(Some(NormalizedOfficialFeature {
        id: format!("{source_id}:{source_feature_id}"),
        source_feature_id,
        kind,
        latitude,
        longitude,
        source_layer,
        geometry_type: geometry_type.to_string(),
        properties_json,
    }))
}

fn coordinate_pair(value: Option<&Value>) -> Option<(f64, f64)> {
    let pair = value?.as_array()?;
    Some((pair.first()?.as_f64()?, pair.get(1)?.as_f64()?))
}

fn normalize_coordinate(x: f64, y: f64, source_crs: &str) -> Result<(f64, f64), GisImportError> {
    if !x.is_finite() || !y.is_finite() {
        return Err(GisImportError::InvalidGeoJson(
            "coordinate must be finite".to_string(),
        ));
    }
    let (longitude, latitude) = if source_crs == "EPSG:3857" {
        if x.abs() > WEB_MERCATOR_LIMIT || y.abs() > WEB_MERCATOR_LIMIT {
            return Err(GisImportError::InvalidGeoJson(
                "Web Mercator coordinate is out of range".to_string(),
            ));
        }
        (
            x / WEB_MERCATOR_RADIUS * 180.0 / std::f64::consts::PI,
            (2.0 * (y / WEB_MERCATOR_RADIUS).exp().atan() - std::f64::consts::FRAC_PI_2) * 180.0
                / std::f64::consts::PI,
        )
    } else {
        (x, y)
    };
    if !(-180.0..=180.0).contains(&longitude) || !(-90.0..=90.0).contains(&latitude) {
        return Err(GisImportError::InvalidGeoJson(
            "normalized WGS84 coordinate is out of range".to_string(),
        ));
    }
    Ok((longitude, latitude))
}

fn normalize_kind(value: &str) -> Option<String> {
    match value.trim().to_ascii_lowercase().as_str() {
        "traffic_light" | "traffic signal" | "signal" | "signals" => {
            Some("traffic_light".to_string())
        }
        "stop_sign" | "stop sign" | "stop" => Some("stop_sign".to_string()),
        "bike_lane" | "bike lane" | "cycleway" | "cycling" | "cycling_network" => {
            Some("bike_lane".to_string())
        }
        "crosswalk" | "pedestrian_crossing" => Some("crosswalk".to_string()),
        _ => None,
    }
}

fn value_id(value: &Value) -> Option<String> {
    match value {
        Value::String(value) if !value.trim().is_empty() => Some(value.trim().to_string()),
        Value::Number(value) => Some(value.to_string()),
        _ => None,
    }
}

#[cfg(test)]
mod tests {
    use super::parse_geojson_features;

    #[test]
    fn parses_wgs84_points_and_line_representatives_with_provenance() {
        let features = parse_geojson_features(
            r#"{
              "type":"FeatureCollection",
              "features":[
                {"type":"Feature","id":"source-signal","properties":{"kind":"signal","sourceLayer":"York signals"},"geometry":{"type":"Point","coordinates":[-79.3,43.8]}},
                {"type":"Feature","properties":{"id":"lane-7","type":"cycleway"},"geometry":{"type":"LineString","coordinates":[[-79.4,43.7],[-79.35,43.75],[-79.3,43.8]]}}
              ]
            }"#,
            "source-1",
            "EPSG:4326",
            "mixed",
            "roads.geojson",
        )
        .unwrap();

        assert_eq!(features.len(), 2);
        assert_eq!(
            (
                features[0].kind.as_str(),
                features[0].source_feature_id.as_str()
            ),
            ("traffic_light", "source-signal")
        );
        assert_eq!(
            (
                features[1].kind.as_str(),
                features[1].geometry_type.as_str()
            ),
            ("bike_lane", "LineString")
        );
        assert_eq!(
            (features[1].longitude, features[1].latitude),
            (-79.35, 43.75)
        );
        assert!(features[0].properties_json.contains("York signals"));
    }

    #[test]
    fn converts_web_mercator_representatives_to_wgs84() {
        let features = parse_geojson_features(
            r#"{"type":"FeatureCollection","features":[
              {"type":"Feature","properties":{"kind":"stop_sign"},"geometry":{"type":"Point","coordinates":[1113194.9079327357,1118889.9748579594]}}
            ]}"#,
            "source-2",
            "EPSG:3857",
            "stop_sign",
            "stops.geojson",
        )
        .unwrap();

        assert!((features[0].longitude - 10.0).abs() < 0.000001);
        assert!((features[0].latitude - 10.0).abs() < 0.000001);
    }

    #[test]
    fn rejects_unsupported_crs_and_sources_without_supported_features() {
        let unsupported = parse_geojson_features(
            r#"{"type":"FeatureCollection","features":[]}"#,
            "source-3",
            "EPSG:26917",
            "mixed",
            "roads.geojson",
        )
        .unwrap_err();
        assert!(unsupported.to_string().contains("EPSG:4326 or EPSG:3857"));

        let empty = parse_geojson_features(
            r#"{"type":"FeatureCollection","features":[
              {"type":"Feature","properties":{"kind":"bench"},"geometry":{"type":"Point","coordinates":[-79.3,43.8]}},
              {"type":"Feature","properties":{"kind":"signal"},"geometry":{"type":"Polygon","coordinates":[]}}
            ]}"#,
            "source-3",
            "EPSG:4326",
            "mixed",
            "roads.geojson",
        )
        .unwrap_err();
        assert!(empty.to_string().contains("supported road features"));
    }
}
