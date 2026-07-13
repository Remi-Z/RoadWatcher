# GitHub Actions

RoadWatcher now uses two Windows-based GitHub Actions workflows.

## Continuous integration

`.github/workflows/ci.yml` runs on every push, pull request, and manual dispatch.
It uses Windows because the supported managed-installer target is Windows x64.
The workflow runs:

- `pnpm test`
- `pnpm build`
- `pnpm verify:release`
- `pnpm test:internal-pilot`
- `cargo fmt --check`
- `cargo test --locked --all-targets` with `CARGO_TARGET_DIR` outside the
  repository path, which avoids the known space-in-path Rust issue.

The workflow has read-only repository permissions, uses a pinned pnpm version
from `package.json`, and does not download or install RoadWatcher-managed runtime
components. It checks out the pinned GPStitch submodule required by the Rust
build, without leaving repository credentials available to later build steps.

## Internal package delivery

`.github/workflows/internal-package.yml` is intentionally manual. It requires a
boolean acknowledgement that the result is an unsigned internal package, runs
the release-contract verification, builds the Tauri Windows bundles, and uploads
them as a 14-day workflow artifact. It also checks out the pinned GPStitch
submodule needed by the packaged runtime.

It does **not** create a GitHub Release, publish a managed dependency artifact,
sign an installer, enable automatic application updates, or set
`publicReleaseReady`. Publishing a GitHub Release and the clean-Windows pilot
remain owner-controlled gates in `TODO.md`.

To use it, open **Actions → Internal Windows package → Run workflow**, select the
intended commit or branch, and confirm the internal-only acknowledgement. Record
the run URL, commit, installer filename, and SHA-256 in the internal-pilot
evidence before distributing the artifact.
