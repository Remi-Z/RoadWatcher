use serde::Deserialize;
use std::fs;
use std::path::{Component, Path, PathBuf};

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct RuntimeManifest {
    schema_version: u32,
    distribution_mode: String,
    components: Vec<RuntimeComponent>,
    external_tools: Vec<ExternalTool>,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct RuntimeComponent {
    id: String,
    version: String,
    license: String,
    source_root: String,
    resource_path: String,
    required_files: Vec<String>,
    version_marker: String,
    license_marker: Option<String>,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct ExternalTool {
    id: String,
    required_for: Vec<String>,
    distributed: bool,
}

fn main() {
    validate_runtime_manifest();
    tauri_build::build();
}

fn validate_runtime_manifest() {
    const MANIFEST_PATH: &str = "resources/runtime-manifest.json";
    println!("cargo:rerun-if-changed={MANIFEST_PATH}");
    println!("cargo:rerun-if-changed=../docs/THIRD_PARTY_NOTICES.md");
    println!("cargo:rerun-if-changed=../LICENSE");
    let manifest_text = fs::read_to_string(MANIFEST_PATH)
        .unwrap_or_else(|error| panic!("could not read {MANIFEST_PATH}: {error}"));
    let manifest: RuntimeManifest = serde_json::from_str(&manifest_text)
        .unwrap_or_else(|error| panic!("invalid {MANIFEST_PATH}: {error}"));
    assert_eq!(
        manifest.schema_version, 1,
        "unsupported runtime manifest schema"
    );
    assert_eq!(
        manifest.distribution_mode,
        "source_bundle_with_external_tools"
    );
    assert!(
        !manifest.components.is_empty(),
        "runtime manifest has no components"
    );
    assert!(
        !manifest.external_tools.is_empty(),
        "runtime manifest has no external tools"
    );
    assert!(
        Path::new("../docs/THIRD_PARTY_NOTICES.md").is_file(),
        "third-party notices are missing"
    );
    let roadwatcher_license =
        fs::read_to_string("../LICENSE").expect("RoadWatcher license notice is missing");
    assert!(
        roadwatcher_license.contains("SPDX-License-Identifier: GPL-3.0-or-later"),
        "RoadWatcher license identifier does not match"
    );

    for component in manifest.components {
        validate_relative(&component.resource_path, "resourcePath");
        assert!(!component.id.trim().is_empty() && !component.version.trim().is_empty());
        assert!(!component.license.trim().is_empty());
        let source_root = PathBuf::from(&component.source_root);
        println!("cargo:rerun-if-changed={}", source_root.display());
        assert!(
            source_root.is_dir(),
            "{} source root is missing",
            component.id
        );
        for required in component.required_files {
            validate_relative(&required, "requiredFiles");
            assert!(
                source_root.join(&required).exists(),
                "{} is missing {}",
                component.id,
                required
            );
        }
        let pyproject = fs::read_to_string(source_root.join("pyproject.toml"))
            .unwrap_or_else(|error| panic!("could not read {} pyproject: {error}", component.id));
        assert!(
            pyproject.contains(&component.version_marker),
            "{} version marker does not match",
            component.id
        );
        if let Some(marker) = component.license_marker {
            let license = fs::read_to_string(source_root.join("LICENSE"))
                .unwrap_or_else(|error| panic!("could not read {} license: {error}", component.id));
            assert!(
                license.contains(&marker),
                "{} license marker does not match",
                component.id
            );
        }
    }
    for tool in manifest.external_tools {
        assert!(!tool.id.trim().is_empty() && !tool.required_for.is_empty());
        assert!(
            !tool.distributed,
            "bundled tool {} requires an explicit license inventory",
            tool.id
        );
    }
}

fn validate_relative(value: &str, field: &str) {
    let path = Path::new(value);
    assert!(
        !path.is_absolute()
            && path
                .components()
                .all(|part| matches!(part, Component::Normal(_))),
        "{field} must be a confined relative path: {value}"
    );
}
