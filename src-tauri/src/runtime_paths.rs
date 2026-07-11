use std::path::{Component, Path, PathBuf};
use thiserror::Error;

#[derive(Debug, Error, PartialEq)]
pub enum RuntimePathError {
    #[error("runtime component path is invalid: {0}")]
    Invalid(String),
    #[error("runtime component is unavailable in development and packaged resources: {0}")]
    Missing(String),
}

pub fn resolve_runtime_directory(
    configured: &Path,
    resource_directory: &Path,
    expected_relative: &Path,
) -> Result<PathBuf, RuntimePathError> {
    validate_expected_relative(expected_relative)?;
    if configured.is_absolute() {
        return existing_directory(configured);
    }
    if configured != expected_relative {
        return Err(RuntimePathError::Invalid(configured.display().to_string()));
    }
    if configured.is_dir() {
        return configured
            .canonicalize()
            .map_err(|_| RuntimePathError::Missing(configured.display().to_string()));
    }
    let packaged = resource_directory.join(expected_relative);
    existing_directory(&packaged)
}

fn existing_directory(path: &Path) -> Result<PathBuf, RuntimePathError> {
    if !path.is_dir() {
        return Err(RuntimePathError::Missing(path.display().to_string()));
    }
    path.canonicalize()
        .map_err(|_| RuntimePathError::Missing(path.display().to_string()))
}

fn validate_expected_relative(path: &Path) -> Result<(), RuntimePathError> {
    if path.is_absolute()
        || path
            .components()
            .any(|component| !matches!(component, Component::Normal(_)))
    {
        return Err(RuntimePathError::Invalid(path.display().to_string()));
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::{resolve_runtime_directory, RuntimePathError};
    use std::fs;
    use std::path::{Path, PathBuf};
    use std::time::{SystemTime, UNIX_EPOCH};

    struct TestRoot(PathBuf);
    impl TestRoot {
        fn new() -> Self {
            let suffix = SystemTime::now()
                .duration_since(UNIX_EPOCH)
                .unwrap()
                .as_nanos();
            let path = std::env::temp_dir().join(format!("roadwatcher-runtime-path-{suffix}"));
            fs::create_dir_all(&path).unwrap();
            Self(path)
        }
    }
    impl Drop for TestRoot {
        fn drop(&mut self) {
            let _ = fs::remove_dir_all(&self.0);
        }
    }

    #[test]
    fn resolves_packaged_resource_when_repository_relative_path_is_absent() {
        let root = TestRoot::new();
        let packaged = root.0.join("sidecars/gpstitch");
        fs::create_dir_all(&packaged).unwrap();
        let resolved = resolve_runtime_directory(
            Path::new("sidecars/gpstitch"),
            &root.0,
            Path::new("sidecars/gpstitch"),
        )
        .unwrap();
        assert_eq!(resolved, packaged.canonicalize().unwrap());
    }

    #[test]
    fn accepts_explicit_absolute_directories_and_rejects_other_relative_paths() {
        let root = TestRoot::new();
        assert_eq!(
            resolve_runtime_directory(&root.0, Path::new("missing"), Path::new("sidecars/cv"))
                .unwrap(),
            root.0.canonicalize().unwrap()
        );
        assert!(matches!(
            resolve_runtime_directory(Path::new("../escape"), &root.0, Path::new("sidecars/cv")),
            Err(RuntimePathError::Invalid(_))
        ));
    }
}
