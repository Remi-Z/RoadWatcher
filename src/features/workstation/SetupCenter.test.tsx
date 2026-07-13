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
  dependencies: [], state: "notInstalled", installPath: "C:/RoadWatcher/tool/1.2.3", updateAvailable: false,
  detail: "Available for explicit installation."
};

const catalog: DependencyCatalog = {
  schemaVersion: 1, platform: "windows-x86_64", catalogVersion: "test", components: [installable]
};

function renderSetup(overrides: Partial<Parameters<typeof SetupCenter>[0]> = {}) {
  const props: Parameters<typeof SetupCenter>[0] = {
    catalog, activeJob: null, message: "Catalog ready.", onRefresh: vi.fn(), onInstall: vi.fn(),
    onCancel: vi.fn(), onRemove: vi.fn(), ...overrides
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
