# Release Governance Design

Date: 2026-07-11

## Goal

Prevent a development installer from being mislabeled as a public release and
make versioning, signing, update, runtime-distribution, and clean-machine gates
machine-verifiable.

## Authoritative Metadata

`src-tauri/resources/release-manifest.json` is bundled beside the runtime
manifest. It records the product/version, release channel, public-ready claim,
Windows signing state, update policy, clean-machine state, and runtime
distribution mode. Version identity must match `package.json`, `Cargo.toml`, the
RoadWatcher package in `Cargo.lock`, and `tauri.conf.json`.

The initial state is intentionally conservative: channel `development`,
unsigned-development-only artifacts, manual downloads with no update feed, and
`publicReleaseReady: false`.

## Enforcement

`npm run verify:release` first retains the runtime/source-license audit, then
runs positive/negative release-verifier tests and the real repository audit. It
rejects version drift, hidden updater configuration/dependencies, unsupported
states, unsigned candidate/stable releases, and public-ready claims inconsistent
with stable + signed + clean-machine-passed gates.

The Rust build script independently parses the bundled release manifest and
checks Cargo version, policy values, runtime distribution mode, and aggregate
public-ready semantics. A normal Cargo/Tauri build therefore cannot bypass the
core metadata gate by omitting the Node release command.

## Installed Evidence

The Windows startup smoke accepts an exact installed executable and expected
version. It validates the PE product version, launches the app, proves the same
process survives a bounded startup period, hashes the executable, writes JSON
evidence, and terminates only that spawned process. It never installs/uninstalls
software or removes user data.

The full clean-VM procedure remains a human-observed release gate because it
must verify signatures, installer UX, external prerequisites, media/GIS/matcher
choices, project persistence, exports, and uninstall data retention. A future
CI signing environment may automate more of that workflow, but may not weaken
the stable + signed + passed aggregate.
