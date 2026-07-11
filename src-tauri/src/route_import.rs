use crate::project_store::{
    import_route_at, ProjectStoreError, RouteImportRequest, RouteImportResponse, RoutePoint,
};
use chrono::DateTime;
use quick_xml::encoding::Decoder;
use quick_xml::events::{BytesStart, Event};
use quick_xml::{Reader, XmlVersion};
use thiserror::Error;
use uuid::Uuid;

use std::fs::File;
use std::io::Read;
use std::path::PathBuf;
use std::time::{SystemTime, UNIX_EPOCH};

const MAX_ROUTE_POINTS: usize = 1_000_000;
const MAX_GPX_BYTES: u64 = 64 * 1024 * 1024;

#[derive(Debug)]
pub struct GpxImportRequest {
    pub sqlite_path: PathBuf,
    pub project_id: String,
    pub source_path: PathBuf,
}

#[derive(Debug, Error)]
pub enum GpxImportError {
    #[error("GPX XML is invalid: {0}")]
    InvalidXml(String),
    #[error("GPX track-point coordinate is missing, invalid, or out of range.")]
    InvalidCoordinate,
    #[error("Every imported GPX sample must be a timed track point.")]
    MissingTime,
    #[error("GPX track-point time must be a valid RFC3339 timestamp.")]
    InvalidTime,
    #[error("GPX track-point timestamps must be strictly increasing.")]
    NonIncreasingTime,
    #[error("GPX import needs at least two timed track points.")]
    TooFewPoints,
    #[error("GPX import exceeds the maximum supported point count.")]
    TooManyPoints,
    #[error("GPX source must have a .gpx extension.")]
    InvalidExtension,
    #[error("GPX source exceeds the 64 MiB import limit.")]
    SourceTooLarge,
    #[error("Could not read GPX source: {0}")]
    Io(#[from] std::io::Error),
    #[error("Could not persist GPX route: {0}")]
    Store(#[from] ProjectStoreError),
    #[error("System clock is before the Unix epoch.")]
    InvalidSystemClock,
}

struct PendingPoint {
    latitude: f64,
    longitude: f64,
    time: Option<String>,
}

pub fn import_gpx(request: GpxImportRequest) -> Result<RouteImportResponse, GpxImportError> {
    if request
        .source_path
        .extension()
        .and_then(|value| value.to_str())
        .is_none_or(|value| !value.eq_ignore_ascii_case("gpx"))
    {
        return Err(GpxImportError::InvalidExtension);
    }
    let mut file = File::open(&request.source_path)?;
    let mut bytes = Vec::new();
    file.by_ref()
        .take(MAX_GPX_BYTES + 1)
        .read_to_end(&mut bytes)?;
    if bytes.len() as u64 > MAX_GPX_BYTES {
        return Err(GpxImportError::SourceTooLarge);
    }
    let text =
        String::from_utf8(bytes).map_err(|error| GpxImportError::InvalidXml(error.to_string()))?;
    let points = parse_gpx_str(&text)?;
    let imported_at_unix = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map_err(|_| GpxImportError::InvalidSystemClock)?
        .as_secs() as i64;
    Ok(import_route_at(
        RouteImportRequest {
            sqlite_path: request.sqlite_path,
            project_id: request.project_id,
            source_path: request.source_path,
            points,
        },
        Uuid::new_v4(),
        Uuid::new_v4(),
        imported_at_unix,
    )?)
}

pub(crate) fn parse_gpx_str(gpx: &str) -> Result<Vec<RoutePoint>, GpxImportError> {
    let mut reader = Reader::from_str(gpx);
    reader.config_mut().trim_text(true);
    let mut pending: Option<PendingPoint> = None;
    let mut inside_time = false;
    let mut raw_points = Vec::new();

    loop {
        match reader.read_event() {
            Ok(Event::Start(element)) if element.local_name().as_ref() == b"trkpt" => {
                if pending.is_some() {
                    return Err(GpxImportError::InvalidXml(
                        "nested track points are not supported".to_string(),
                    ));
                }
                let (latitude, longitude) = coordinates(&element, reader.decoder())?;
                pending = Some(PendingPoint {
                    latitude,
                    longitude,
                    time: None,
                });
            }
            Ok(Event::Empty(element)) if element.local_name().as_ref() == b"trkpt" => {
                coordinates(&element, reader.decoder())?;
                return Err(GpxImportError::MissingTime);
            }
            Ok(Event::Start(element))
                if pending.is_some() && element.local_name().as_ref() == b"time" =>
            {
                inside_time = true;
            }
            Ok(Event::Text(text)) if inside_time => {
                let value = text
                    .decode()
                    .map_err(|error| GpxImportError::InvalidXml(error.to_string()))?;
                if let Some(point) = pending.as_mut() {
                    point.time = Some(value.into_owned());
                }
            }
            Ok(Event::End(element)) if element.local_name().as_ref() == b"time" => {
                inside_time = false;
            }
            Ok(Event::End(element)) if element.local_name().as_ref() == b"trkpt" => {
                let point = pending.take().ok_or_else(|| {
                    GpxImportError::InvalidXml(
                        "track-point closing tag has no opening tag".to_string(),
                    )
                })?;
                let time = point.time.ok_or(GpxImportError::MissingTime)?;
                let timestamp = DateTime::parse_from_rfc3339(time.trim())
                    .map_err(|_| GpxImportError::InvalidTime)?
                    .timestamp_millis();
                raw_points.push((point.latitude, point.longitude, timestamp));
                if raw_points.len() > MAX_ROUTE_POINTS {
                    return Err(GpxImportError::TooManyPoints);
                }
            }
            Ok(Event::Eof) => break,
            Ok(_) => {}
            Err(error) => return Err(GpxImportError::InvalidXml(error.to_string())),
        }
    }

    if pending.is_some() {
        return Err(GpxImportError::InvalidXml(
            "track point is not closed".to_string(),
        ));
    }
    if raw_points.len() < 2 {
        return Err(GpxImportError::TooFewPoints);
    }
    let first_timestamp = raw_points[0].2;
    let mut previous_timestamp = first_timestamp - 1;
    raw_points
        .into_iter()
        .map(|(latitude, longitude, timestamp)| {
            if timestamp <= previous_timestamp {
                return Err(GpxImportError::NonIncreasingTime);
            }
            previous_timestamp = timestamp;
            Ok(RoutePoint {
                latitude,
                longitude,
                time_seconds: (timestamp - first_timestamp) as f64 / 1_000.0,
            })
        })
        .collect()
}

fn coordinates(element: &BytesStart<'_>, decoder: Decoder) -> Result<(f64, f64), GpxImportError> {
    let mut latitude = None;
    let mut longitude = None;
    for attribute in element.attributes() {
        let attribute = attribute.map_err(|error| GpxImportError::InvalidXml(error.to_string()))?;
        let value = attribute
            .decoded_and_normalized_value(XmlVersion::Implicit1_0, decoder)
            .map_err(|error| GpxImportError::InvalidXml(error.to_string()))?;
        match attribute.key.local_name().as_ref() {
            b"lat" => latitude = value.parse::<f64>().ok(),
            b"lon" => longitude = value.parse::<f64>().ok(),
            _ => {}
        }
    }
    match (latitude, longitude) {
        (Some(latitude), Some(longitude))
            if latitude.is_finite()
                && longitude.is_finite()
                && (-90.0..=90.0).contains(&latitude)
                && (-180.0..=180.0).contains(&longitude) =>
        {
            Ok((latitude, longitude))
        }
        _ => Err(GpxImportError::InvalidCoordinate),
    }
}

#[cfg(test)]
mod tests {
    use super::{import_gpx, parse_gpx_str, GpxImportRequest};
    use crate::project_store::{create_project_at, ProjectCreateRequest};
    use std::fs;
    use uuid::Uuid;

    #[test]
    fn parses_namespaced_timed_track_points_and_normalizes_time() {
        let points = parse_gpx_str(
            r#"<?xml version="1.0"?>
            <gpx xmlns="http://www.topografix.com/GPX/1/1" version="1.1">
              <trk><trkseg>
                <trkpt lat="43.1000" lon="-79.2000"><time>2026-07-10T12:00:00Z</time></trkpt>
                <trkpt lat="43.1005" lon="-79.1995"><time>2026-07-10T12:00:02.500Z</time></trkpt>
                <trkpt lat="43.1010" lon="-79.1990"><time>2026-07-10T12:00:05+00:00</time></trkpt>
              </trkseg></trk>
            </gpx>"#,
        )
        .unwrap();

        assert_eq!(points.len(), 3);
        assert_eq!(points[0].time_seconds, 0.0);
        assert_eq!(points[1].time_seconds, 2.5);
        assert_eq!(points[2].time_seconds, 5.0);
        assert_eq!((points[2].latitude, points[2].longitude), (43.101, -79.199));
    }

    #[test]
    fn rejects_missing_invalid_and_non_increasing_timestamps() {
        let missing = parse_gpx_str(
            r#"<gpx><trk><trkseg>
              <trkpt lat="43.1" lon="-79.2" />
              <trkpt lat="43.2" lon="-79.1"><time>2026-07-10T12:00:01Z</time></trkpt>
            </trkseg></trk></gpx>"#,
        )
        .unwrap_err();
        assert!(missing.to_string().contains("timed track point"));

        let invalid = parse_gpx_str(
            r#"<gpx><trk><trkseg>
              <trkpt lat="43.1" lon="-79.2"><time>not-a-time</time></trkpt>
              <trkpt lat="43.2" lon="-79.1"><time>2026-07-10T12:00:01Z</time></trkpt>
            </trkseg></trk></gpx>"#,
        )
        .unwrap_err();
        assert!(invalid.to_string().contains("RFC3339"));

        let repeated = parse_gpx_str(
            r#"<gpx><trk><trkseg>
              <trkpt lat="43.1" lon="-79.2"><time>2026-07-10T12:00:01Z</time></trkpt>
              <trkpt lat="43.2" lon="-79.1"><time>2026-07-10T12:00:01Z</time></trkpt>
            </trkseg></trk></gpx>"#,
        )
        .unwrap_err();
        assert!(repeated.to_string().contains("strictly increasing"));
    }

    #[test]
    fn rejects_out_of_range_coordinates_and_short_tracks() {
        let coordinates = parse_gpx_str(
            r#"<gpx><trk><trkseg>
              <trkpt lat="91" lon="-79.2"><time>2026-07-10T12:00:00Z</time></trkpt>
              <trkpt lat="43.2" lon="-79.1"><time>2026-07-10T12:00:01Z</time></trkpt>
            </trkseg></trk></gpx>"#,
        )
        .unwrap_err();
        assert!(coordinates.to_string().contains("coordinate"));

        let short = parse_gpx_str(
            r#"<gpx><trk><trkseg>
              <trkpt lat="43.1" lon="-79.2"><time>2026-07-10T12:00:00Z</time></trkpt>
            </trkseg></trk></gpx>"#,
        )
        .unwrap_err();
        assert!(short.to_string().contains("at least two"));
    }

    #[test]
    fn imports_a_real_gpx_file_into_the_native_project() {
        let root = std::env::temp_dir().join(format!("roadwatcher-gpx-import-{}", Uuid::new_v4()));
        fs::create_dir_all(&root).unwrap();
        let project_id = Uuid::new_v4();
        let created = create_project_at(
            ProjectCreateRequest {
                project_name: "GPX import".to_string(),
                root_directory: root.clone(),
            },
            project_id,
            1_788_000_000,
        )
        .unwrap();
        let source_path = root.join("drive.gpx");
        fs::write(
            &source_path,
            r#"<gpx><trk><trkseg>
              <trkpt lat="43.1" lon="-79.2"><time>2026-07-10T12:00:00Z</time></trkpt>
              <trkpt lat="43.2" lon="-79.1"><time>2026-07-10T12:00:04Z</time></trkpt>
            </trkseg></trk></gpx>"#,
        )
        .unwrap();

        let result = import_gpx(GpxImportRequest {
            sqlite_path: created.sqlite_path.into(),
            project_id: project_id.to_string(),
            source_path,
        })
        .unwrap();

        assert_eq!(result.file_name, "drive.gpx");
        assert_eq!(result.route.len(), 2);
        assert_eq!(result.route[1].time_seconds, 4.0);
        assert_eq!(result.match_status, "queued");
        fs::remove_dir_all(root).unwrap();
    }
}
