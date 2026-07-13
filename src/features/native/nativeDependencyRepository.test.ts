import { describe, expect, it, vi } from "vitest";
import type { NativeCommandBridge } from "./nativeCommandBridge";
import { createNativeDependencyRepository } from "./nativeDependencyRepository";

const component = {
  id: "tool", label: "Tool", version: "1", purpose: "Testing", required: true, recommended: true,
  license: { id: "mit", label: "MIT", url: "https://example.com/license", digest: "digest", consentRequired: true },
  sourceUrl: "https://example.com/tool", availability: "available",
  artifact: { url: "https://github.com/example/tool.zip", sha256: "a".repeat(64), maxBytes: 1000, archive: "zip" },
  dependencies: [], state: "notInstalled", installPath: "C:/RoadWatcher/tool/1", updateAvailable: false, detail: "Available for installation."
};

describe("native dependency repository", () => {
  it("strictly parses the catalog and starts only the requested identities", async () => {
    const invoke = vi.fn<NativeCommandBridge["invoke"]>()
      .mockResolvedValueOnce({ ok: true, status: "invoked", command: "dependency_catalog", response: {
        schemaVersion: 1, platform: "windows-x86_64", catalogVersion: "test", components: [component]
      } })
      .mockResolvedValueOnce({ ok: true, status: "invoked", command: "dependency_install_start", response: {
        jobId: "job-1", status: "queued", progress: 0, detail: "Queued.", componentIds: ["tool"], currentComponentId: ""
      } });
    const repository = createNativeDependencyRepository({ invoke });
    await expect(repository.catalog()).resolves.toMatchObject({ status: "loaded", catalog: { components: [{ id: "tool" }] } });
    await expect(repository.install(["tool"], ["digest"])).resolves.toMatchObject({ status: "loaded", job: { jobId: "job-1" } });
    expect(invoke).toHaveBeenLastCalledWith("dependency_install_start", { componentIds: ["tool"], acceptedLicenseDigests: ["digest"] });
  });

  it("rejects catalog identity drift and mismatched job responses", async () => {
    const invoke = vi.fn<NativeCommandBridge["invoke"]>()
      .mockResolvedValueOnce({ ok: true, status: "invoked", command: "dependency_catalog", response: {
        schemaVersion: 1, platform: "windows-x86_64", catalogVersion: "test", components: [{ ...component, dependencies: ["missing"] }]
      } })
      .mockResolvedValueOnce({ ok: true, status: "invoked", command: "dependency_install_status", response: {
        jobId: "wrong", status: "ready", progress: 100, detail: "Ready.", componentIds: ["tool"], currentComponentId: ""
      } });
    const repository = createNativeDependencyRepository({ invoke });
    await expect(repository.catalog()).resolves.toMatchObject({ status: "unavailable", commandStatus: "invalid_response" });
    await expect(repository.status("job-1", ["tool"])).resolves.toMatchObject({ status: "unavailable", commandStatus: "invalid_response" });
  });
});
