# Windows Release Validation

Date: 2026-07-11

## Current Policy

RoadWatcher `0.1.0` is a development release. Current installers are unsigned
and must not be represented as public-release-ready. Updates use deliberate
manual download and reinstall; there is no background updater, update feed, or
silent network check. A candidate or stable release requires a trusted Windows
code-signing identity and clean-machine evidence.

The authoritative machine-readable policy is
`src-tauri/resources/release-manifest.json`. `npm run verify:release` rejects
version drift, an undisclosed updater, a candidate/stable unsigned state, or a
public-ready claim that is inconsistent with its gates.

## Build Candidate

From a clean checkout at the intended tag:

```powershell
pnpm install --frozen-lockfile
pnpm test
pnpm build
pnpm verify:release
$env:CARGO_TARGET_DIR = 'C:\rw-target'
pnpm tauri:build
```

Record the commit, tag, Rust/Node/pnpm versions, installer SHA-256, and signing
certificate subject/thumbprint. Do not change `artifactState` to `signed`
unless both the installer and installed executable validate with
`Get-AuthenticodeSignature` and chain to the intended certificate.

## Clean Windows VM

Use a supported Windows VM with no RoadWatcher install, no repository checkout,
and no inherited user PATH customization. Verify the installer signature before
launch for a candidate/stable release, install it, locate the installed
`RoadWatcher.exe`, then copy the two release-audit scripts into the VM. First run
the exact Authenticode gate with the release certificate's 40-character SHA-1
thumbprint:

```powershell
powershell -ExecutionPolicy Bypass -File .\windows-release-signature-audit.ps1 `
  -InstallerPath '.\RoadWatcher_0.1.0_x64-setup.exe' `
  -InstalledExecutable 'C:\path\to\RoadWatcher.exe' `
  -ExpectedVersion '0.1.0' `
  -ExpectedSignerThumbprint '0123456789ABCDEF0123456789ABCDEF01234567' `
  -EvidencePath '.\roadwatcher-signature-evidence.json'
```

The command must pass for both files. Preserve its JSON output, then run the
bounded startup check. Both signatures must use a trusted timestamp service;
the audit rejects otherwise-valid signatures without timestamp certificates.

```powershell
powershell -ExecutionPolicy Bypass -File .\windows-installed-startup-smoke.ps1 `
  -InstalledExecutable 'C:\path\to\RoadWatcher.exe' `
  -ExpectedVersion '0.1.0' `
  -EvidencePath '.\roadwatcher-installed-smoke.json'
```

The script verifies product version, hashes the installed executable, proves it
stays alive through the startup window, stops only the process it launched, and
writes JSON evidence. Preserve that JSON with the installer/build evidence.

## Functional Installed Checks

On the same VM:

1. Confirm the installed-runtime panel initially reports external prerequisites
   and both managed sidecar environments honestly.
2. In Setup Center, consent to and install the approved managed uv/Python
   component, then select **Prepare sidecar environments** and confirm all
   required preparation components pass without elevation or PATH changes. An
   explicit external override remains a separately recorded fallback test.
3. Provide the approved FFmpeg/ffprobe build and confirm installed-runtime
   preflight plus a representative proxy job.
4. Create a project outside the installation directory, import representative
   video and GPX files, save/reopen it, render GPStitch telemetry, and publish a
   native evidence packet.
5. If included in the release scope, provide approved GDAL, CV model/labels, and
   Valhalla/OSRM data and retain their separate smoke evidence.
6. Uninstall RoadWatcher. Confirm user-created projects remain intact. Managed
   app-local environments may remain as user data unless the release explicitly
   documents an opt-in removal step.

Any failed required check keeps `publicReleaseReady` false. Update the release
manifest only in the release commit that carries the corresponding signing and
clean-machine evidence.

## Development-Host Tool Evidence

On 2026-07-11, the startup script passed against the freshly rebuilt debug
`RoadWatcher.exe` version `0.1.0`, proving that the spawned process remained
alive for five seconds and that JSON evidence generation/targeted shutdown work.
The executable SHA-256 was
`F565F30EBE1DCBAE04E612E28E90576EB01F8CE0F48CAD9EAF97D87ED48E84E5`.
This is smoke-tool evidence only. It is deliberately not recorded as a passed
clean-machine gate because the executable ran from the development build host.

The signature audit was separately exercised in both directions. The current
unsigned installer was rejected as `NotSigned` without an evidence file.
Temporary copies were then signed by a disposable CurrentUser test identity and
timestamped through DigiCert; installer and executable both validated as
`Valid`, exact thumbprint matching passed, and JSON evidence was produced. The
test removed its personal/trusted-root certificate entries and signed copies in
`finally`, and an independent follow-up check confirmed no residue. This proves
the audit tool, not possession of a trusted public release identity.
