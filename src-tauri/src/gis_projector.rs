use crate::project_store::{
    claim_gis_projection_job, complete_gis_projection_job, fail_gis_projection_job,
    read_gis_projection_status, update_gis_projection_progress, GisProjectionRequest,
    GisProjectionStatus, NormalizedOfficialFeature, ProjectStoreError, ProjectedOfficialFeature,
    RoutePoint,
};
use serde::Serialize;
use std::sync::{Arc, Mutex};
use thiserror::Error;

#[derive(Clone, Debug, Eq, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct GisProjectionStartResponse {
    pub job_id: String,
    pub status: String,
}

#[derive(Debug, Error)]
pub enum GisProjectorError {
    #[error("GIS projection is blocked: {0}")]
    Blocked(String),
    #[error("GIS projection failed: {0}")]
    Projection(String),
    #[error("GIS projection state failed: {0}")]
    Store(#[from] ProjectStoreError),
    #[error("GIS projector synchronization failed.")]
    Synchronization,
    #[error("GIS projection job {0} is already active.")]
    AlreadyActive(String),
}

pub fn execute_gis_projection(
    request: &GisProjectionRequest,
) -> Result<GisProjectionStatus, GisProjectorError> {
    let result = (|| {
        let claimed = claim_gis_projection_job(request).map_err(|error| match error {
            ProjectStoreError::GisProjectionJobNotFound
            | ProjectStoreError::InvalidRoutePoints(_) => {
                GisProjectorError::Blocked(error.to_string())
            }
            other => GisProjectorError::Store(other),
        })?;
        update_gis_projection_progress(
            request,
            25.0,
            "Projecting normalized features onto route.",
        )?;
        let projected = project_features(
            &request.feature_source_id,
            &request.route_id,
            &claimed.route,
            &claimed.features,
            request.corridor_meters,
        )?;
        update_gis_projection_progress(request, 90.0, "Publishing projected feature rows.")?;
        complete_gis_projection_job(request, &projected)?;
        Ok(read_gis_projection_status(
            &request.sqlite_path,
            &request.project_id,
            &request.feature_source_id,
            &request.job_id,
        )?)
    })();
    if let Err(error) = &result {
        let status = if matches!(error, GisProjectorError::Blocked(_)) {
            "blocked"
        } else {
            "failed"
        };
        let _ = fail_gis_projection_job(request, status, &error.to_string());
    }
    result
}

pub(crate) fn project_features(
    feature_source_id: &str,
    route_id: &str,
    route: &[RoutePoint],
    features: &[NormalizedOfficialFeature],
    corridor_meters: f64,
) -> Result<Vec<ProjectedOfficialFeature>, GisProjectorError> {
    if route.len() < 2 || !corridor_meters.is_finite() || corridor_meters <= 0.0 {
        return Err(GisProjectorError::Projection(
            "route and corridor are invalid".to_string(),
        ));
    }
    let mut projected = features
        .iter()
        .filter_map(|feature| {
            project_feature(feature_source_id, route_id, route, feature, corridor_meters)
        })
        .collect::<Vec<_>>();
    projected.sort_by(|left, right| left.time_seconds.total_cmp(&right.time_seconds));
    Ok(projected)
}

fn project_feature(
    feature_source_id: &str,
    route_id: &str,
    route: &[RoutePoint],
    feature: &NormalizedOfficialFeature,
    corridor_meters: f64,
) -> Option<ProjectedOfficialFeature> {
    let mut best: Option<ProjectedOfficialFeature> = None;
    for segment in route.windows(2) {
        let projection = project_onto_segment(feature, &segment[0], &segment[1]);
        if projection.0 > corridor_meters {
            continue;
        }
        let candidate = ProjectedOfficialFeature {
            feature_id: feature.id.clone(),
            feature_source_id: feature_source_id.to_string(),
            route_id: route_id.to_string(),
            kind: feature.kind.clone(),
            source_layer: feature.source_layer.clone(),
            time_seconds: projection.1,
            distance_meters: projection.0,
            confidence: (1.0 - projection.0 / corridor_meters).max(0.2),
            review_status: "needs_review".to_string(),
            review_note: String::new(),
        };
        if best
            .as_ref()
            .is_none_or(|current| candidate.distance_meters < current.distance_meters)
        {
            best = Some(candidate);
        }
    }
    best
}

fn project_onto_segment(
    feature: &NormalizedOfficialFeature,
    start: &RoutePoint,
    end: &RoutePoint,
) -> (f64, f64) {
    let origin_latitude = (start.latitude + end.latitude) / 2.0;
    let to_meters = |latitude: f64, longitude: f64| {
        let latitude_scale = 111_320.0;
        let longitude_scale = latitude_scale * origin_latitude.to_radians().cos();
        (longitude * longitude_scale, latitude * latitude_scale)
    };
    let start_point = to_meters(start.latitude, start.longitude);
    let end_point = to_meters(end.latitude, end.longitude);
    let feature_point = to_meters(feature.latitude, feature.longitude);
    let segment = (end_point.0 - start_point.0, end_point.1 - start_point.1);
    let length_squared = segment.0 * segment.0 + segment.1 * segment.1;
    let fraction = if length_squared == 0.0 {
        0.0
    } else {
        (((feature_point.0 - start_point.0) * segment.0
            + (feature_point.1 - start_point.1) * segment.1)
            / length_squared)
            .clamp(0.0, 1.0)
    };
    let projected = (
        start_point.0 + fraction * segment.0,
        start_point.1 + fraction * segment.1,
    );
    let distance =
        ((feature_point.0 - projected.0).powi(2) + (feature_point.1 - projected.1).powi(2)).sqrt();
    (
        distance,
        start.time_seconds + (end.time_seconds - start.time_seconds) * fraction,
    )
}

pub struct GisProjectorManager {
    execution_lock: Arc<Mutex<()>>,
    active_job: Arc<Mutex<Option<String>>>,
}

impl Default for GisProjectorManager {
    fn default() -> Self {
        Self {
            execution_lock: Arc::new(Mutex::new(())),
            active_job: Arc::new(Mutex::new(None)),
        }
    }
}

impl GisProjectorManager {
    pub fn start(
        &self,
        request: GisProjectionRequest,
    ) -> Result<GisProjectionStartResponse, GisProjectorError> {
        let current = read_gis_projection_status(
            &request.sqlite_path,
            &request.project_id,
            &request.feature_source_id,
            &request.job_id,
        )?;
        {
            let mut active = self
                .active_job
                .lock()
                .map_err(|_| GisProjectorError::Synchronization)?;
            if active.is_some() {
                return Err(GisProjectorError::AlreadyActive(request.job_id));
            }
            *active = Some(request.job_id.clone());
        }
        let execution_lock = self.execution_lock.clone();
        let active_job = self.active_job.clone();
        std::thread::spawn(move || {
            let result = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
                let _guard = execution_lock
                    .lock()
                    .map_err(|_| GisProjectorError::Synchronization)?;
                execute_gis_projection(&request)
            }));
            if result.is_err() {
                let _ = fail_gis_projection_job(&request, "failed", "GIS projector panicked.");
            }
            if let Ok(mut active) = active_job.lock() {
                *active = None;
            }
        });
        Ok(GisProjectionStartResponse {
            job_id: current.job_id,
            status: current.status,
        })
    }
}

#[cfg(test)]
mod tests {
    use super::{execute_gis_projection, project_features, GisProjectorError, GisProjectorManager};
    use crate::project_store::{
        create_project_at, import_feature_source_at, import_route_at, read_gis_projection_status,
        FeatureImportRequest, GisProjectionRequest, NormalizedOfficialFeature,
        ProjectCreateRequest, RouteImportRequest, RoutePoint,
    };
    use std::fs;
    use std::path::PathBuf;
    use std::time::Duration;
    use uuid::Uuid;

    #[test]
    fn projects_features_to_route_time_distance_and_confidence() {
        let route = vec![
            RoutePoint {
                latitude: 43.8,
                longitude: -79.3,
                time_seconds: 0.0,
            },
            RoutePoint {
                latitude: 43.8,
                longitude: -79.29,
                time_seconds: 10.0,
            },
        ];
        let features = vec![
            feature("near", 43.8001, -79.295),
            feature("far", 43.81, -79.295),
        ];

        let projected = project_features("source-1", "route-1", &route, &features, 90.0).unwrap();

        assert_eq!(projected.len(), 1);
        assert_eq!(projected[0].feature_id, "near");
        assert!((projected[0].time_seconds - 5.0).abs() < 0.1);
        assert!(projected[0].distance_meters > 10.0 && projected[0].distance_meters < 12.0);
        assert!(projected[0].confidence > 0.85);
        assert_eq!(projected[0].review_status, "needs_review");
    }

    #[test]
    fn executes_and_persists_source_specific_projection() {
        let fixture = Fixture::new();
        let status = execute_gis_projection(&fixture.request).unwrap();
        assert_eq!(status.status, "complete");
        assert_eq!(status.route_id, fixture.request.route_id);
        assert_eq!(status.projected_features.len(), 1);
        assert_eq!(
            status.projected_features[0].feature_source_id,
            fixture.request.feature_source_id
        );
    }

    #[test]
    fn missing_route_blocks_without_deleting_imported_features() {
        let mut fixture = Fixture::new();
        fixture.request.route_id = Uuid::nil().to_string();
        assert!(matches!(
            execute_gis_projection(&fixture.request),
            Err(GisProjectorError::Blocked(_))
        ));
        let status = read_gis_projection_status(
            &fixture.request.sqlite_path,
            &fixture.request.project_id,
            &fixture.request.feature_source_id,
            &fixture.request.job_id,
        )
        .unwrap();
        assert_eq!(status.status, "blocked");
    }

    #[test]
    fn manager_returns_promptly_and_completes_in_background() {
        let fixture = Fixture::new();
        let manager = GisProjectorManager::default();
        let started = manager.start(fixture.request.clone()).unwrap();
        assert_eq!(started.status, "queued");
        let deadline = std::time::Instant::now() + Duration::from_secs(2);
        loop {
            let status = read_gis_projection_status(
                &fixture.request.sqlite_path,
                &fixture.request.project_id,
                &fixture.request.feature_source_id,
                &fixture.request.job_id,
            )
            .unwrap();
            if status.status == "complete" {
                break;
            }
            assert!(
                std::time::Instant::now() < deadline,
                "GIS projector did not complete"
            );
            std::thread::sleep(Duration::from_millis(10));
        }
    }

    fn feature(id: &str, latitude: f64, longitude: f64) -> NormalizedOfficialFeature {
        NormalizedOfficialFeature {
            id: id.to_string(),
            source_feature_id: id.to_string(),
            kind: "traffic_light".to_string(),
            latitude,
            longitude,
            source_layer: "test".to_string(),
            geometry_type: "Point".to_string(),
            properties_json: "{}".to_string(),
        }
    }

    struct Fixture {
        root: PathBuf,
        request: GisProjectionRequest,
    }

    impl Fixture {
        fn new() -> Self {
            let root =
                std::env::temp_dir().join(format!("roadwatcher-gis-projector-{}", Uuid::new_v4()));
            fs::create_dir_all(&root).unwrap();
            let project_id = Uuid::new_v4();
            let route_id = Uuid::new_v4();
            let route_job_id = Uuid::new_v4();
            let source_id = Uuid::new_v4();
            let gis_job_id = Uuid::new_v4();
            let created = create_project_at(
                ProjectCreateRequest {
                    project_name: "GIS projection".to_string(),
                    root_directory: root.clone(),
                },
                project_id,
                1_788_000_000,
            )
            .unwrap();
            let route_source = root.join("route.gpx");
            fs::write(&route_source, b"route").unwrap();
            import_route_at(
                RouteImportRequest {
                    sqlite_path: created.sqlite_path.clone().into(),
                    project_id: project_id.to_string(),
                    source_path: route_source,
                    points: vec![
                        RoutePoint {
                            latitude: 43.8,
                            longitude: -79.3,
                            time_seconds: 0.0,
                        },
                        RoutePoint {
                            latitude: 43.8,
                            longitude: -79.29,
                            time_seconds: 10.0,
                        },
                    ],
                },
                route_id,
                route_job_id,
                1_788_000_001,
            )
            .unwrap();
            let gis_source = root.join("signals.geojson");
            fs::write(&gis_source, b"gis").unwrap();
            import_feature_source_at(
                FeatureImportRequest {
                    sqlite_path: created.sqlite_path.clone().into(),
                    project_id: project_id.to_string(),
                    source_path: gis_source,
                    source_crs: "EPSG:4326".to_string(),
                    layer_kind: "traffic_light".to_string(),
                    features: vec![feature("signal-1", 43.8001, -79.295)],
                },
                source_id,
                gis_job_id,
                1_788_000_002,
            )
            .unwrap();
            Self {
                root,
                request: GisProjectionRequest {
                    sqlite_path: created.sqlite_path.into(),
                    project_id: project_id.to_string(),
                    feature_source_id: source_id.to_string(),
                    job_id: gis_job_id.to_string(),
                    route_id: route_id.to_string(),
                    corridor_meters: 90.0,
                },
            }
        }
    }

    impl Drop for Fixture {
        fn drop(&mut self) {
            let _ = fs::remove_dir_all(&self.root);
        }
    }
}
