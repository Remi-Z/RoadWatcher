use serde_json::Value;

pub const ROADWATCHER_TILE_DIRECTORY_TOKEN: &str = "${ROADWATCHER_TILE_DIR}";

const FORBIDDEN_MJOLNIR_PATH_KEYS: &[&str] = &[
    "tile_extract",
    "admin",
    "admins",
    "incident_dir",
    "timezones",
    "timezone",
    "traffic_extract",
    "transit_dir",
];

pub fn validate_portable_config(config: &Value) -> Result<(), String> {
    let mjolnir = config
        .as_object()
        .and_then(|value| value.get("mjolnir"))
        .and_then(Value::as_object)
        .ok_or_else(|| "RoadWatcher Valhalla configuration is missing mjolnir".to_string())?;
    if mjolnir.get("tile_dir").and_then(Value::as_str) != Some(ROADWATCHER_TILE_DIRECTORY_TOKEN) {
        return Err("RoadWatcher Valhalla configuration is not portable".to_string());
    }
    for key in FORBIDDEN_MJOLNIR_PATH_KEYS {
        if mjolnir
            .get(*key)
            .is_some_and(|value| !value.is_null() && value.as_str() != Some(""))
        {
            return Err(format!(
                "RoadWatcher Valhalla configuration cannot select mjolnir.{key}"
            ));
        }
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::{validate_portable_config, FORBIDDEN_MJOLNIR_PATH_KEYS};
    use serde_json::json;

    #[test]
    fn accepts_only_portable_tile_configuration() {
        assert!(validate_portable_config(&json!({
            "mjolnir": {
                "tile_dir": "${ROADWATCHER_TILE_DIR}",
                "tile_extract": "",
                "admin": null
            }
        }))
        .is_ok());
        assert!(validate_portable_config(&json!({
            "mjolnir": {"tile_dir": "C:/untrusted"}
        }))
        .unwrap_err()
        .contains("not portable"));
    }

    #[test]
    fn rejects_every_external_mjolnir_path() {
        for key in FORBIDDEN_MJOLNIR_PATH_KEYS {
            let mut mjolnir = serde_json::Map::new();
            mjolnir.insert("tile_dir".to_string(), json!("${ROADWATCHER_TILE_DIR}"));
            mjolnir.insert((*key).to_string(), json!("C:/untrusted"));
            let error = validate_portable_config(&json!({"mjolnir": mjolnir})).unwrap_err();
            assert!(error.contains(key), "unexpected error for {key}: {error}");
        }
    }
}
