import { AlertTriangle, CheckCircle2, Download, FolderInput, RotateCcw, Square, Trash2 } from "lucide-react";
import { useMemo, useState } from "react";
import type { DependencyCatalog, DependencyInstallJob, ManagedProjectImport } from "../native/nativeDependencyRepository";
import { StatusPill } from "./WorkstationViews";

export function SetupCenter({
  catalog,
  activeJob,
  message,
  onRefresh,
  onInstall,
  onCancel,
  onRemove,
  onProjectImport
}: {
  catalog: DependencyCatalog | null;
  activeJob: DependencyInstallJob | null;
  message: string;
  onRefresh: () => void;
  onInstall: (componentIds: string[], acceptedLicenseDigests: string[]) => void;
  onCancel: () => void;
  onRemove: (componentId: string) => void;
  onProjectImport: (projectImport: ManagedProjectImport) => void;
}) {
  const [acceptedDigests, setAcceptedDigests] = useState<string[]>([]);
  const recommended = useMemo(
    () => catalog?.components.filter((component) => component.recommended && component.availability === "available" && component.state !== "ready") ?? [],
    [catalog]
  );
  const busy = activeJob ? ["queued", "downloading", "installing"].includes(activeJob.status) : false;

  function hasConsent(digest: string) {
    return acceptedDigests.includes(digest);
  }

  function toggleConsent(digest: string, accepted: boolean) {
    setAcceptedDigests((current) => accepted
      ? current.includes(digest) ? current : [...current, digest]
      : current.filter((item) => item !== digest));
  }

  function canInstall(componentId: string) {
    const component = catalog?.components.find((item) => item.id === componentId);
    return Boolean(component && component.availability === "available" && component.artifact
      && (!component.license.consentRequired || hasConsent(component.license.digest)) && !busy);
  }

  const recommendedReady = recommended.length > 0 && recommended.every((component) => canInstall(component.id));

  return (
    <section className="setup-center" aria-label="Managed dependency setup center">
      <div className="setup-center-heading">
        <div><strong>Setup Center</strong><span>Explicit app-local installs · no administrator or PATH changes</span></div>
        <button type="button" className="button secondary" onClick={onRefresh} disabled={busy}><RotateCcw size={15} />Refresh</button>
      </div>
      <p className="setup-center-message" role="status">{message}</p>
      {activeJob ? (
        <div className="setup-job" aria-label="Managed dependency installation progress">
          <div><StatusPill status={activeJob.status} label={activeJob.status} /><strong>{activeJob.currentComponentId || "Selected dependencies"}</strong><span>{activeJob.progress}%</span></div>
          <div className="progress-line"><span style={{ width: `${activeJob.progress}%` }} /></div>
          <p>{activeJob.detail}</p>
          {busy ? <button type="button" className="button secondary" onClick={onCancel}><Square size={15} />Cancel safely</button> : null}
        </div>
      ) : null}
      <div className="setup-center-actions">
        <button type="button" className="button primary" disabled={!recommendedReady}
          onClick={() => onInstall(recommended.map((component) => component.id), acceptedDigests)}>
          <Download size={15} />Install recommended
        </button>
        {recommended.length === 0 ? <small>No approved recommended artifacts are currently installable.</small> : null}
      </div>
      <div className="setup-component-list">
        {catalog?.components.map((component) => (
          <article className="setup-component" key={component.id}>
            <div className="setup-component-summary">
              <div><StatusPill status={component.state === "ready" ? "ready" : component.availability === "blockedOnUser" ? "blocked" : "queued"} label={component.updateAvailable ? "update available" : component.state} />
                <strong>{component.label}</strong><span>{component.version}</span></div>
              <span>{component.required ? "core" : "optional"}</span>
            </div>
            <p>{component.purpose}</p>
            <div className="setup-component-meta">
              <a href={component.sourceUrl} target="_blank" rel="noreferrer">Source</a>
              <a href={component.license.url} target="_blank" rel="noreferrer">{component.license.label}</a>
              {component.artifact ? <span>{formatBytes(component.artifact.maxBytes)} maximum download</span> : <span>Exact artifact pending approval</span>}
            </div>
            {component.license.consentRequired && component.availability === "available" ? (
              <label className="setup-license-consent">
                <input type="checkbox" checked={hasConsent(component.license.digest)}
                  onChange={(event) => toggleConsent(component.license.digest, event.target.checked)} />
                <span>I reviewed and accept this exact license revision.</span>
              </label>
            ) : null}
            <div className="setup-component-detail">
              {component.state === "ready" ? <CheckCircle2 size={15} /> : <AlertTriangle size={15} />}
              <span>{component.detail}</span>
            </div>
            <div className="setup-component-actions">
              {component.managedProjectImports.map((projectImport) => (
                <button type="button" className="button secondary" disabled={busy} key={projectImport.id}
                  onClick={() => onProjectImport(projectImport)}><FolderInput size={15} />Import {projectImport.label} into project</button>
              ))}
              {component.state === "ready" ? (
                <button type="button" className="button secondary" disabled={busy} onClick={() => onRemove(component.id)}><Trash2 size={15} />Remove managed copy</button>
              ) : (
                <button type="button" className="button secondary" disabled={!canInstall(component.id)}
                  onClick={() => onInstall([component.id], acceptedDigests)}><Download size={15} />Install</button>
              )}
            </div>
          </article>
        )) ?? <p>Open the Tauri desktop application to inspect managed dependencies.</p>}
      </div>
      <p className="setup-disk-guidance">Installations use RoadWatcher's app-local data directory. Keep enough free space for the maximum download plus an unpacked staging copy; failures leave the active version intact.</p>
    </section>
  );
}

function formatBytes(bytes: number): string {
  if (bytes >= 1_000_000_000) return `${(bytes / 1_000_000_000).toFixed(1)} GB`;
  if (bytes >= 1_000_000) return `${Math.ceil(bytes / 1_000_000)} MB`;
  return `${Math.ceil(bytes / 1_000)} KB`;
}
