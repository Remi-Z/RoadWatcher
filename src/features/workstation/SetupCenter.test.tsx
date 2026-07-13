import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { DependencyCatalog, DependencyComponent, DependencyInstallJob } from "../native/nativeDependencyRepository";
import { SetupCenter } from "./SetupCenter";

const installable: DependencyComponent = {
  id: "tool", label: "Audited tool", version: "1.2.3", purpose: "Exercise setup safely.",
  required: true, recommended: true,
  license: { id: "mit", label: "MIT", url: "https://example.com/license", digest: "license-digest", consentRequired: true },
  sourceUrl: "https://example.com/tool", availability: "available",
  artifact: { url: "https://github.com/example/tool.zip", sha256: "a".repeat(64), maxBytes: 25_000_000, archive: "zip" },
  dependencies: [], references: [], projectImports: [], state: "notInstalled", installPath: "C:/RoadWatcher/tool/1.2.3", updateAvailable: false,
  detail: "Available for explicit installation.", managedReferences: {}, managedProjectImports: []
};

const catalog: DependencyCatalog = {
  schemaVersion: 1, platform: "windows-x86_64", catalogVersion: "test", components: [installable]
};

function renderSetup(overrides: Partial<Parameters<typeof SetupCenter>[0]> = {}) {
  const props: Parameters<typeof SetupCenter>[0] = {
    catalog, activeJob: null, message: "Catalog ready.", onRefresh: vi.fn(), onInstall: vi.fn(),
    onCancel: vi.fn(), onRemove: vi.fn(), onProjectImport: vi.fn(), ...overrides
  };
  render(<SetupCenter {...props} />);
  return props;
}

describe("Setup Center", () => {
  it("requires exact license consent before a recommended install", () => {
    const props = renderSetup();
    const install = screen.getByRole("button", { name: "Install recommended" });
    expect(install).toBeDisabled();
    fireEvent.click(screen.getByRole("checkbox", { name: /reviewed and accept/i }));
    expect(install).toBeEnabled();
    fireEvent.click(install);
    expect(props.onInstall).toHaveBeenCalledWith(["tool"], ["license-digest"]);
  });

  it("includes a managed bootstrap payload in displayed download guidance", () => {
    const withBootstrap = {
      ...installable,
      bootstrap: {
        kind: "uv-managed-python" as const,
        version: "3.12.13",
        sourceUrl: "https://releases.astral.sh/python-build-standalone",
        license: { id: "psf-2-0", label: "PSF-2.0", url: "https://docs.python.org/3.12/license.html", digest: "python-digest", consentRequired: true },
        artifact: { url: "https://releases.astral.sh/python.tar.gz", sha256: "b".repeat(64), maxBytes: 22_000_000, archive: "file" as const, fileName: "20260610/python.tar.gz" }
      }
    };
    renderSetup({ catalog: { ...catalog, components: [withBootstrap] } });
    expect(screen.getByText("47 MB maximum download")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Python source" })).toHaveAttribute("href", withBootstrap.bootstrap.sourceUrl);
    const consents = screen.getAllByRole("checkbox");
    const install = screen.getByRole("button", { name: "Install recommended" });
    expect(consents).toHaveLength(2);
    fireEvent.click(consents[0]);
    expect(install).toBeDisabled();
    fireEvent.click(consents[1]);
    expect(install).toBeEnabled();
  });

  it("requires an explicit per-dataset project import action", () => {
    const projectImport = { id: "signals", label: "Traffic signals", sourcePath: "C:/managed/signals.gpkg", sourceCrs: "EPSG:4326", layerName: "signals", layerKind: "traffic_light" as const };
    const ready = { ...installable, state: "ready" as const, managedProjectImports: [projectImport] };
    const props = renderSetup({ catalog: { ...catalog, components: [ready] } });
    expect(props.onProjectImport).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: "Import Traffic signals into project" }));
    expect(props.onProjectImport).toHaveBeenCalledWith(projectImport);
  });

  it("shows update identity and removes only a ready managed copy", () => {
    const ready = { ...installable, state: "ready" as const, updateAvailable: true };
    const props = renderSetup({ catalog: { ...catalog, components: [ready] } });
    expect(screen.getByText("update available")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Remove managed copy" }));
    expect(props.onRemove).toHaveBeenCalledWith("tool");
  });

  it("renders bounded progress and explicit cancellation", () => {
    const activeJob: DependencyInstallJob = {
      jobId: "job-1", status: "downloading", progress: 42, detail: "Downloading audited tool.",
      componentIds: ["tool"], currentComponentId: "tool"
    };
    const props = renderSetup({ activeJob });
    expect(screen.getByLabelText("Managed dependency installation progress")).toHaveTextContent("42%");
    fireEvent.click(screen.getByRole("button", { name: "Cancel safely" }));
    expect(props.onCancel).toHaveBeenCalledOnce();
  });
});
