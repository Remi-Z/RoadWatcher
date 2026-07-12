import assert from "node:assert/strict";
import { execFileSync, spawnSync } from "node:child_process";
import { cpSync, mkdtempSync, mkdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, resolve } from "node:path";
import test from "node:test";

const root = resolve(import.meta.dirname, "..");
const verifier = resolve(root, "scripts/verify-release.mjs");
const files = [
  "package.json",
  "src-tauri/tauri.conf.json",
  "src-tauri/Cargo.toml",
  "src-tauri/Cargo.lock",
  "src-tauri/resources/runtime-manifest.json",
  "src-tauri/resources/release-manifest.json",
  "scripts/windows-release-signature-audit.ps1",
  "scripts/windows-installed-startup-smoke.ps1",
  "docs/windows-release-validation.md"
];

test("accepts synchronized development release metadata", () => {
  const fixture = createFixture();
  try {
    const output = execFileSync(process.execPath, [verifier, fixture], { encoding: "utf8" });
    assert.match(output, /Release metadata verified/);
  } finally {
    rmSync(fixture, { recursive: true, force: true });
  }
});

test("rejects version drift", () => {
  const fixture = createFixture();
  try {
    const packagePath = resolve(fixture, "package.json");
    const manifest = JSON.parse(readFileSync(packagePath, "utf8"));
    manifest.version = "0.1.1";
    writeFileSync(packagePath, `${JSON.stringify(manifest, null, 2)}\n`);
    assertFailure(fixture, "package.json version does not match release manifest");
  } finally {
    rmSync(fixture, { recursive: true, force: true });
  }
});

test("rejects an unsigned stable release claim", () => {
  const fixture = createFixture();
  try {
    const releasePath = resolve(fixture, "src-tauri/resources/release-manifest.json");
    const manifest = JSON.parse(readFileSync(releasePath, "utf8"));
    manifest.releaseChannel = "stable";
    writeFileSync(releasePath, `${JSON.stringify(manifest, null, 2)}\n`);
    assertFailure(fixture, "candidate/stable artifacts must be signed");
  } finally {
    rmSync(fixture, { recursive: true, force: true });
  }
});

function createFixture() {
  const fixture = mkdtempSync(resolve(tmpdir(), "roadwatcher-release-verifier-"));
  for (const relative of files) {
    const destination = resolve(fixture, relative);
    mkdirSync(dirname(destination), { recursive: true });
    cpSync(resolve(root, relative), destination);
  }
  return fixture;
}

function assertFailure(fixture, expected) {
  const result = spawnSync(process.execPath, [verifier, fixture], { encoding: "utf8" });
  assert.notEqual(result.status, 0);
  assert.match(`${result.stdout}\n${result.stderr}`, new RegExp(expected.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")));
}
