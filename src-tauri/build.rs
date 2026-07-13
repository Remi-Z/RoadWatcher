use serde::Deserialize;
use std::collections::{HashMap, HashSet};
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

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct ReleaseManifest {
    schema_version: u32,
    product: String,
    version: String,
    release_channel: String,
    public_release_ready: bool,
    signing: ReleaseSigning,
    updates: ReleaseUpdates,
    clean_machine_validation: CleanMachineValidation,
    runtime_distribution_mode: String,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct ReleaseSigning {
    windows_policy: String,
    artifact_state: String,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct ReleaseUpdates {
    mode: String,
    automatic: bool,
    feed: Option<String>,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct CleanMachineValidation {
    status: String,
    signature_audit: String,
    startup_smoke: String,
    procedure: String,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct DependencyCatalog {
    schema_version: u32,
    platform: String,
    catalog_version: String,
    components: Vec<DependencyComponent>,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct DependencyComponent {
    id: String,
    label: String,
    version: String,
    purpose: String,
    license: DependencyLicense,
    source_url: String,
    availability: String,
    artifact: Option<DependencyArtifact>,
    bootstrap: Option<DependencyBootstrap>,
    install_strategy: Option<DependencyInstallStrategy>,
    dependencies: Vec<String>,
    references: Vec<DependencyReference>,
    project_imports: Vec<DependencyProjectImport>,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct DependencyInstallStrategy {
    kind: String,
    #[serde(default)]
    python_version: String,
    #[serde(default)]
    package: String,
    #[serde(default)]
    package_version: String,
    #[serde(default)]
    wheel_file_name: String,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct DependencyReference {
    id: String,
    path: String,
    kind: String,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct DependencyProjectImport {
    id: String,
    label: String,
    path: String,
    source_crs: String,
    layer_name: String,
    layer_kind: String,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct DependencyLicense {
    id: String,
    label: String,
    url: String,
    digest: String,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct DependencyArtifact {
    url: String,
    sha256: String,
    max_bytes: u64,
    archive: String,
    file_name: Option<String>,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct DependencyBootstrap {
    kind: String,
    version: String,
    source_url: String,
    license: DependencyLicense,
    artifact: Option<DependencyArtifact>,
}

fn main() {
    let distribution_mode = validate_runtime_manifest();
    validate_dependency_catalog();
    validate_release_manifest(&distribution_mode);
    tauri_build::build();
}

fn validate_dependency_catalog() {
    const MANIFEST_PATH: &str = "resources/dependency-catalog.json";
    const DOWNLOAD_HOSTS: &[&str] = &[
        "github.com",
        "objects.githubusercontent.com",
        "release-assets.githubusercontent.com",
        "releases.astral.sh",
        "files.pythonhosted.org",
        "download.osgeo.org",
        "www.gyan.dev",
    ];
    println!("cargo:rerun-if-changed={MANIFEST_PATH}");
    let text = fs::read_to_string(MANIFEST_PATH)
        .unwrap_or_else(|error| panic!("could not read {MANIFEST_PATH}: {error}"));
    let catalog: DependencyCatalog = serde_json::from_str(&text)
        .unwrap_or_else(|error| panic!("invalid {MANIFEST_PATH}: {error}"));
    assert_eq!(
        catalog.schema_version, 1,
        "unsupported dependency catalog schema"
    );
    assert_eq!(catalog.platform, "windows-x86_64");
    assert!(
        !catalog.catalog_version.trim().is_empty(),
        "dependency catalog version is blank"
    );
    assert!(
        !catalog.components.is_empty(),
        "dependency catalog has no components"
    );

    let mut ids = HashSet::new();
    let mut licenses: HashMap<&str, &str> = HashMap::new();
    for component in &catalog.components {
        assert!(
            !component.id.is_empty()
                && component.id.chars().all(|value| value.is_ascii_lowercase()
                    || value.is_ascii_digit()
                    || value == '-'),
            "invalid dependency component id {}",
            component.id
        );
        assert!(
            ids.insert(component.id.as_str()),
            "duplicate dependency component {}",
            component.id
        );
        assert!(
            !component.label.trim().is_empty()
                && !component.version.trim().is_empty()
                && !component.purpose.trim().is_empty(),
            "dependency component {} has blank identity fields",
            component.id
        );
        assert_https(&component.source_url, "source URL", &component.id);
        assert_https(&component.license.url, "license URL", &component.id);
        assert!(
            !component.license.id.trim().is_empty()
                && !component.license.label.trim().is_empty()
                && !component.license.digest.trim().is_empty(),
            "dependency component {} has incomplete license evidence",
            component.id
        );
        if let Some(previous) = licenses.insert(&component.license.id, &component.license.digest) {
            assert_eq!(
                previous, component.license.digest,
                "license {} has inconsistent digests",
                component.license.id
            );
        }
        assert!(
            matches!(
                component.availability.as_str(),
                "available" | "pendingApproval" | "blockedOnUser"
            ),
            "dependency component {} has unsupported availability {}",
            component.id,
            component.availability
        );
        assert!(
            component.availability != "available" || component.artifact.is_some(),
            "available component {} has no artifact",
            component.id
        );
        if let Some(artifact) = &component.artifact {
            assert_https(&artifact.url, "artifact URL", &component.id);
            let host = artifact
                .url
                .trim_start_matches("https://")
                .split('/')
                .next()
                .unwrap_or_default()
                .split(':')
                .next()
                .unwrap_or_default();
            assert!(
                DOWNLOAD_HOSTS.contains(&host),
                "dependency component {} uses non-allowlisted host {host}",
                component.id
            );
            assert!(
                artifact.max_bytes > 0,
                "dependency component {} has no size limit",
                component.id
            );
            assert!(
                artifact.sha256.len() == 64
                    && artifact
                        .sha256
                        .chars()
                        .all(|value| value.is_ascii_hexdigit()),
                "dependency component {} has an invalid SHA-256",
                component.id
            );
            assert!(
                matches!(artifact.archive.as_str(), "file" | "zip"),
                "dependency component {} uses an unsupported archive",
                component.id
            );
            if let Some(file_name) = &artifact.file_name {
                assert_safe_relative(file_name, "artifact file name", &component.id);
            }
        }
        if let Some(bootstrap) = &component.bootstrap {
            assert_eq!(
                component.id, "uv-python",
                "only uv-python may declare a bootstrap"
            );
            assert_eq!(bootstrap.kind, "uv-managed-python");
            assert!(
                exact_version(&bootstrap.version),
                "uv bootstrap version is invalid"
            );
            assert_https(&bootstrap.source_url, "bootstrap source URL", &component.id);
            assert_https(
                &bootstrap.license.url,
                "bootstrap license URL",
                &component.id,
            );
            assert!(
                !bootstrap.license.id.trim().is_empty()
                    && !bootstrap.license.label.trim().is_empty()
                    && !bootstrap.license.digest.trim().is_empty(),
                "uv bootstrap license evidence is incomplete"
            );
            if let Some(previous) =
                licenses.insert(&bootstrap.license.id, &bootstrap.license.digest)
            {
                assert_eq!(
                    previous, bootstrap.license.digest,
                    "license {} has inconsistent digests",
                    bootstrap.license.id
                );
            }
            assert!(
                component.availability != "available" || bootstrap.artifact.is_some(),
                "available uv-python component has no bootstrap artifact"
            );
            if let Some(artifact) = &bootstrap.artifact {
                assert_https(&artifact.url, "bootstrap artifact URL", &component.id);
                let host = artifact
                    .url
                    .trim_start_matches("https://")
                    .split('/')
                    .next()
                    .unwrap_or_default()
                    .split(':')
                    .next()
                    .unwrap_or_default();
                assert!(
                    DOWNLOAD_HOSTS.contains(&host),
                    "uv bootstrap host is not allowlisted"
                );
                assert!(artifact.max_bytes > 0, "uv bootstrap has no size limit");
                assert!(
                    artifact.sha256.len() == 64
                        && artifact
                            .sha256
                            .chars()
                            .all(|value| value.is_ascii_hexdigit()),
                    "uv bootstrap SHA-256 is invalid"
                );
                assert_eq!(
                    artifact.archive, "file",
                    "uv bootstrap must be a verified file"
                );
                let file_name = artifact
                    .file_name
                    .as_deref()
                    .expect("uv bootstrap mirror path is missing");
                assert_safe_relative(file_name, "bootstrap mirror path", &component.id);
                assert!(
                    Path::new(file_name).components().count() >= 2
                        && file_name.to_ascii_lowercase().ends_with(".tar.gz"),
                    "uv bootstrap mirror path is invalid"
                );
            }
        }
        if let Some(strategy) = &component.install_strategy {
            let artifact = component
                .artifact
                .as_ref()
                .expect("install strategy requires an artifact");
            let valid = match strategy.kind.as_str() {
                "uv-wheel-environment" => {
                    component.id == "managed-valhalla"
                        && strategy.python_version == "3.12.13"
                        && strategy.package == "pyvalhalla"
                        && strategy.package_version == "3.7.0"
                        && component.version == strategy.package_version
                        && artifact.archive == "file"
                        && artifact.file_name.as_deref()
                            == Some(strategy.wheel_file_name.as_str())
                        && strategy.wheel_file_name
                            == "pyvalhalla-3.7.0-cp312-abi3-win_amd64.whl"
                        && component.dependencies.as_slice() == ["uv-python"]
                }
                "verified-ffmpeg-archive" => {
                    component.id == "ffmpeg"
                        && component.version == "8.1.1-audited-windows-x64"
                        && strategy.python_version.is_empty()
                        && strategy.package.is_empty()
                        && strategy.package_version.is_empty()
                        && strategy.wheel_file_name.is_empty()
                        && component.license.digest
                            == "c31bd2401e4b09ced92dc957006d20997edbd7211301d9ca06e94802a2e11b50"
                        && artifact.url == "https://github.com/GyanD/codexffmpeg/releases/download/8.1.1/ffmpeg-8.1.1-full_build.zip"
                        && artifact.sha256
                            == "49b28c5f16addd40239a66949973458769b7056fb7752c30ac0d53389d09a552"
                        && artifact.max_bytes == 252_194_496
                        && artifact.archive == "zip"
                        && artifact.file_name.as_deref()
                            == Some("ffmpeg-8.1.1-full_build.zip")
                        && component.dependencies.is_empty()
                }
                _ => false,
            };
            assert!(
                valid,
                "dependency component {} install strategy drifted from the fixed backend contract",
                component.id
            );
        }
        let mut reference_ids = HashSet::new();
        for reference in &component.references {
            assert!(
                !reference.id.is_empty()
                    && reference.id.chars().all(|value| value.is_ascii_lowercase()
                        || value.is_ascii_digit()
                        || value == '-')
                    && reference_ids.insert(reference.id.as_str()),
                "dependency component {} has an invalid or duplicate managed reference id",
                component.id
            );
            assert!(
                matches!(reference.kind.as_str(), "file" | "directory"),
                "dependency component {} has an unsupported managed reference kind",
                component.id
            );
            assert!(
                !reference.path.is_empty()
                    && Path::new(&reference.path)
                        .components()
                        .all(|part| matches!(part, Component::Normal(_))),
                "dependency component {} has an unsafe managed reference path",
                component.id
            );
        }
        if component.bootstrap.is_some() {
            assert!(
                component
                    .references
                    .iter()
                    .any(|reference| reference.id == "executable"
                        && reference.path == "uv.exe"
                        && reference.kind == "file"),
                "uv bootstrap executable reference drifted"
            );
            assert!(
                component
                    .references
                    .iter()
                    .any(|reference| reference.id == "python-installations"
                        && reference.path == "python-installations"
                        && reference.kind == "directory"),
                "uv bootstrap Python reference drifted"
            );
        }
        if let Some(strategy) = &component.install_strategy {
            let valid_reference = component.references.len() == 1
                && match strategy.kind.as_str() {
                    "uv-wheel-environment" => component.references.iter().any(|reference| {
                        reference.id == "service-executable"
                            && reference.path == "Scripts/valhalla_service.exe"
                            && reference.kind == "file"
                    }),
                    "verified-ffmpeg-archive" => component.references.iter().any(|reference| {
                        reference.id == "binary-directory"
                            && reference.path == "ffmpeg-8.1.1-full_build/bin"
                            && reference.kind == "directory"
                    }),
                    _ => false,
                };
            assert!(
                valid_reference,
                "managed dependency reference drifted from the fixed backend contract"
            );
        }
        let mut import_ids = HashSet::new();
        for project_import in &component.project_imports {
            assert!(
                !project_import.id.is_empty()
                    && project_import
                        .id
                        .chars()
                        .all(|value| value.is_ascii_lowercase()
                            || value.is_ascii_digit()
                            || value == '-')
                    && import_ids.insert(project_import.id.as_str())
                    && !project_import.label.trim().is_empty()
                    && !project_import.source_crs.trim().is_empty()
                    && matches!(
                        project_import.layer_kind.as_str(),
                        "mixed"
                            | "traffic_light"
                            | "stop_sign"
                            | "bike_lane"
                            | "crosswalk"
                            | "other"
                    ),
                "dependency component {} has an invalid managed project import",
                component.id
            );
            assert!(
                !project_import.path.is_empty()
                    && Path::new(&project_import.path)
                        .components()
                        .all(|part| matches!(part, Component::Normal(_))),
                "dependency component {} has an unsafe managed project import path",
                component.id
            );
            let _ = &project_import.layer_name;
        }
    }

    let by_id: HashMap<_, _> = catalog
        .components
        .iter()
        .map(|component| (component.id.as_str(), component))
        .collect();
    for component in &catalog.components {
        for dependency in &component.dependencies {
            assert!(
                by_id.contains_key(dependency.as_str()),
                "{} depends on unknown component {dependency}",
                component.id
            );
        }
        visit_dependency(
            &component.id,
            &by_id,
            &mut HashSet::new(),
            &mut HashSet::new(),
        );
    }
}

fn visit_dependency<'a>(
    id: &'a str,
    components: &HashMap<&'a str, &'a DependencyComponent>,
    visiting: &mut HashSet<&'a str>,
    visited: &mut HashSet<&'a str>,
) {
    if visited.contains(id) {
        return;
    }
    assert!(visiting.insert(id), "dependency cycle includes {id}");
    let component = components.get(id).expect("dependency identity disappeared");
    for dependency in &component.dependencies {
        visit_dependency(dependency, components, visiting, visited);
    }
    visiting.remove(id);
    visited.insert(id);
}

fn exact_version(value: &str) -> bool {
    let parts = value.split('.').collect::<Vec<_>>();
    parts.len() == 3
        && parts
            .iter()
            .all(|part| !part.is_empty() && part.chars().all(|value| value.is_ascii_digit()))
}

fn assert_safe_relative(value: &str, field: &str, component_id: &str) {
    assert!(
        !value.is_empty()
            && Path::new(value)
                .components()
                .all(|part| matches!(part, Component::Normal(_))),
        "dependency component {component_id} has an unsafe {field}"
    );
}

fn assert_https(url: &str, field: &str, component_id: &str) {
    assert!(
        url.starts_with("https://") && !url.contains(['\r', '\n']),
        "dependency component {component_id} has an unsafe {field}"
    );
}

fn validate_runtime_manifest() -> String {
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
    manifest.distribution_mode
}

fn validate_release_manifest(runtime_distribution_mode: &str) {
    const MANIFEST_PATH: &str = "resources/release-manifest.json";
    println!("cargo:rerun-if-changed={MANIFEST_PATH}");
    println!("cargo:rerun-if-changed=../scripts/windows-installed-startup-smoke.ps1");
    println!("cargo:rerun-if-changed=../docs/windows-release-validation.md");
    let text = fs::read_to_string(MANIFEST_PATH)
        .unwrap_or_else(|error| panic!("could not read {MANIFEST_PATH}: {error}"));
    let release: ReleaseManifest = serde_json::from_str(&text)
        .unwrap_or_else(|error| panic!("invalid {MANIFEST_PATH}: {error}"));
    assert_eq!(
        release.schema_version, 1,
        "unsupported release manifest schema"
    );
    assert_eq!(release.product, "RoadWatcher");
    assert_eq!(release.version, env!("CARGO_PKG_VERSION"));
    assert_eq!(release.runtime_distribution_mode, runtime_distribution_mode);
    assert!(matches!(
        release.release_channel.as_str(),
        "development" | "candidate" | "stable"
    ));
    assert_eq!(
        release.signing.windows_policy,
        "required-for-public-release"
    );
    assert!(matches!(
        release.signing.artifact_state.as_str(),
        "unsigned-development-only" | "signed"
    ));
    assert_eq!(release.updates.mode, "manual-download");
    assert!(!release.updates.automatic && release.updates.feed.is_none());
    assert!(matches!(
        release.clean_machine_validation.status.as_str(),
        "required-before-public-release" | "passed"
    ));
    assert_eq!(
        release.clean_machine_validation.signature_audit,
        "scripts/windows-release-signature-audit.ps1"
    );
    assert_eq!(
        release.clean_machine_validation.startup_smoke,
        "scripts/windows-installed-startup-smoke.ps1"
    );
    assert_eq!(
        release.clean_machine_validation.procedure,
        "docs/windows-release-validation.md"
    );
    let public_ready = release.release_channel == "stable"
        && release.signing.artifact_state == "signed"
        && release.clean_machine_validation.status == "passed";
    assert_eq!(release.public_release_ready, public_ready);
    assert!(
        release.release_channel == "development" || release.signing.artifact_state == "signed",
        "candidate/stable artifacts must be signed"
    );
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
