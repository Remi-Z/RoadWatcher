import { FileVideo, Gauge, MapPinned, Route, Settings, ShieldCheck, Upload } from "lucide-react";
import type { ReactNode } from "react";
import type { RoadFeatureKind } from "../geo/projection";
import type { GpstitchAlignment, TelemetryRender } from "../jobs/jobModel";
import type { RuntimePreflightReport } from "../native/nativeRuntimePreflightRepository";
import type { NativeCommandAttempt } from "../project/projectState";
import type { ReviewReadiness } from "../project/reviewReadiness";
import { StatusPill } from "./WorkstationViews";

export interface ReviewReadinessPanelProps {
  nativeCommandAttempts: NativeCommandAttempt[];
  nativeCvModelPath: string;
  nativeCvLabelsPath: string;
  nativeGisSourceCrs: string;
  nativeGisLayerName: string;
  nativeGisLayerKind: "mixed" | RoadFeatureKind;
  nativeGisSourcePath: string;
  nativeGpxSourcePath: string;
  nativeMediaSourcePath: string;
  nativeProjectRoot: string;
  gpstitchLayout: TelemetryRender["layout"];
  gpstitchAlignment: GpstitchAlignment;
  gpstitchTimeOffsetSeconds: number;
  runtimePreflightReport: RuntimePreflightReport | null;
  onNativeGisImport: () => void;
  onNativeCvModelPathChange: (value: string) => void;
  onNativeCvLabelsPathChange: (value: string) => void;
  onNativeGisSelect: () => void;
  onNativeGisDirectorySelect: () => void;
  onNativeGisSourceCrsChange: (value: string) => void;
  onNativeGisLayerNameChange: (value: string) => void;
  onNativeGisLayerKindChange: (value: "mixed" | RoadFeatureKind) => void;
  onNativeGisSourcePathChange: (value: string) => void;
  onNativeGpxImport: () => void;
  onNativeGpxSelect: () => void;
  onNativeGpxSourcePathChange: (value: string) => void;
  onNativeMediaSourcePathChange: (value: string) => void;
  onNativeMediaSelect: () => void;
  onNativeProjectRootChange: (value: string) => void;
  onProbeCvScan: () => void;
  onGpstitchLayoutChange: (value: TelemetryRender["layout"]) => void;
  onGpstitchAlignmentChange: (value: GpstitchAlignment) => void;
  onGpstitchTimeOffsetSecondsChange: (value: number) => void;
  onGpstitchRender: () => void;
  onRuntimePreflight: () => void;
  onRuntimePrepare: () => void;
  onProbeFfmpegProxy: () => void;
  onProbeGisProjection: () => void;
  onProbeGpxMatch: () => void;
  onProbeMediaImport: () => void;
  onProbeNativeProjectStore: () => void;
  setupCenter: ReactNode;
  readiness: ReviewReadiness;
}

export function ReviewReadinessPanel(props: ReviewReadinessPanelProps) {
  const {
    nativeCommandAttempts, nativeCvModelPath, nativeCvLabelsPath, nativeGisSourceCrs,
    nativeGisLayerName, nativeGisLayerKind, nativeGisSourcePath, nativeGpxSourcePath,
    nativeMediaSourcePath, nativeProjectRoot, gpstitchLayout, gpstitchAlignment,
    gpstitchTimeOffsetSeconds, runtimePreflightReport, onNativeGisImport,
    onNativeCvModelPathChange, onNativeCvLabelsPathChange, onNativeGisSelect,
    onNativeGisDirectorySelect, onNativeGisSourceCrsChange, onNativeGisLayerNameChange,
    onNativeGisLayerKindChange, onNativeGisSourcePathChange, onNativeGpxImport,
    onNativeGpxSelect, onNativeGpxSourcePathChange, onNativeMediaSourcePathChange,
    onNativeMediaSelect, onNativeProjectRootChange, onProbeCvScan, onGpstitchLayoutChange,
    onGpstitchAlignmentChange, onGpstitchTimeOffsetSecondsChange, onGpstitchRender,
    onRuntimePreflight, onRuntimePrepare, onProbeFfmpegProxy, onProbeGisProjection,
    onProbeGpxMatch, onProbeMediaImport, onProbeNativeProjectStore, setupCenter, readiness
  } = props;

  return (
    <div className="readiness-panel">
      <p>{readiness.summary}</p>
      <div className="readiness-metrics">
        <span><StatusPill status={readiness.packet.status === "ready" ? "ready" : "blocked"} label={readiness.packet.status} />Packet readiness {readiness.packet.status}</span>
        <span><StatusPill status={readiness.native.status === "ready" ? "ready" : readiness.native.status === "unverified" ? "queued" : "blocked"} label={readiness.native.status} />Native workflow {readiness.native.status}</span>
        <span><StatusPill status={readiness.openComponentSlots.length === 0 ? "ready" : "blocked"} label={readiness.openComponentSlots.length === 0 ? "clear" : "open"} />
          {readiness.openComponentSlots.length} native {readiness.openComponentSlots.length === 1 ? "slot needs" : "slots need"} attention</span>
      </div>
      <div className="readiness-blockers">
        <strong>Open blockers</strong>
        <ul>
          {[...readiness.openComponentSlots, ...readiness.blockedJobs].map((label) => <li key={label}>{label}</li>)}
          {readiness.openComponentSlots.length === 0 && readiness.blockedJobs.length === 0 ? <li>None recorded.</li> : null}
        </ul>
      </div>
      {setupCenter}
      <div className="runtime-status">
        <strong>Runtime mode</strong>
        <p>{readiness.runtime.summary}</p>
        <p><StatusPill status={readiness.runtime.bridgeStatus === "ready" ? "ready" : readiness.runtime.bridgeStatus === "bridge_unavailable" ? "blocked" : "queued"} label={readiness.runtime.bridgeStatus} /> {readiness.runtime.bridgeSummary}</p>
        <button type="button" className="button secondary native-probe-button" onClick={onRuntimePreflight}><ShieldCheck size={15} />Check installed runtime</button>
        <button type="button" className="button secondary native-probe-button" onClick={onRuntimePrepare}><Settings size={15} />Prepare Python environments</button>
        {runtimePreflightReport ? (
          <div className="native-attempt-list" aria-label="Installed runtime preflight results">
            <strong>Installed runtime: {runtimePreflightReport.status}</strong>
            {runtimePreflightReport.components.map((component) => (
              <article className="native-attempt-row" key={component.id}>
                <strong>{component.label}</strong>
                <span><StatusPill status={component.status === "ready" ? "ready" : "blocked"} label={component.status} /> {component.required ? "required" : "optional"}</span>
                <span>{component.version || component.detail}</span><span>{component.executable || "not resolved"}</span>
              </article>
            ))}
          </div>
        ) : null}
        <NativeTextField label="Native project root" value={nativeProjectRoot} onChange={onNativeProjectRootChange} />
        <ProbeButton icon={<Settings size={15} />} label="Probe native project store" onClick={onProbeNativeProjectStore} />
        <NativeTextField label="Native media source path" value={nativeMediaSourcePath} onChange={onNativeMediaSourcePathChange} />
        <ProbeButton icon={<Upload size={15} />} label="Choose media file" onClick={onNativeMediaSelect} />
        <ProbeButton icon={<Upload size={15} />} label="Import native media" onClick={onProbeMediaImport} />
        <NativeTextField label="Native GPX source path" value={nativeGpxSourcePath} onChange={onNativeGpxSourcePathChange} />
        <ProbeButton icon={<Upload size={15} />} label="Choose GPX file" onClick={onNativeGpxSelect} />
        <ProbeButton icon={<Upload size={15} />} label="Import native GPX" onClick={onNativeGpxImport} />
        <ProbeButton icon={<Route size={15} />} label="Start GPX matcher" onClick={onProbeGpxMatch} />
        <NativeTextField label="Native GIS source path" value={nativeGisSourcePath} onChange={onNativeGisSourcePathChange} />
        <ProbeButton icon={<Upload size={15} />} label="Choose GIS file" onClick={onNativeGisSelect} />
        <ProbeButton icon={<Upload size={15} />} label="Choose FileGDB directory" onClick={onNativeGisDirectorySelect} />
        <NativeTextField label="Native GIS source CRS" value={nativeGisSourceCrs} placeholder="AUTO or EPSG:26917" onChange={onNativeGisSourceCrsChange} />
        <NativeTextField label="Native GIS layer name" value={nativeGisLayerName} placeholder="blank for a single-layer dataset" onChange={onNativeGisLayerNameChange} />
        <label className="native-root-field">
          <span>Native GIS feature kind</span>
          <select aria-label="Native GIS feature kind" value={nativeGisLayerKind} onChange={(event) => onNativeGisLayerKindChange(event.target.value as "mixed" | RoadFeatureKind)}>
            <option value="mixed">Read kind from each feature</option><option value="traffic_light">Traffic lights</option>
            <option value="stop_sign">Stop signs</option><option value="bike_lane">Bike lanes</option><option value="crosswalk">Crosswalks</option>
          </select>
        </label>
        <ProbeButton icon={<Upload size={15} />} label="Import native GIS" onClick={onNativeGisImport} />
        <ProbeButton icon={<MapPinned size={15} />} label="Start GIS projection" onClick={onProbeGisProjection} />
        <ProbeButton icon={<FileVideo size={15} />} label="Start native proxy" onClick={onProbeFfmpegProxy} />
        <NativeTextField label="Native CV model path" value={nativeCvModelPath} onChange={onNativeCvModelPathChange} />
        <NativeTextField label="Native CV labels path" value={nativeCvLabelsPath} onChange={onNativeCvLabelsPathChange} />
        <ProbeButton icon={<Gauge size={15} />} label="Probe local CV scan" onClick={onProbeCvScan} />
        <label className="native-root-field"><span>GPStitch layout</span><select aria-label="GPStitch layout" value={gpstitchLayout}
          onChange={(event) => onGpstitchLayoutChange(event.target.value as TelemetryRender["layout"])}>
          <option value="speed-awareness">Speed awareness</option><option value="default">Default dashboard</option>
        </select></label>
        <label className="native-root-field"><span>GPStitch alignment</span><select aria-label="GPStitch alignment" value={gpstitchAlignment}
          onChange={(event) => onGpstitchAlignmentChange(event.target.value as GpstitchAlignment)}>
          <option value="auto">Video detected start</option><option value="gpx_timestamps">GPX timestamps</option><option value="manual">Manual offset</option>
        </select></label>
        {gpstitchAlignment === "manual" ? <label className="native-root-field"><span>GPStitch time offset (seconds)</span>
          <input aria-label="GPStitch time offset seconds" type="number" step="1" value={gpstitchTimeOffsetSeconds}
            onChange={(event) => onGpstitchTimeOffsetSecondsChange(Math.trunc(Number(event.target.value) || 0))} /></label> : null}
        <ProbeButton icon={<Gauge size={15} />} label="Render telemetry overlay" onClick={onGpstitchRender} />
        <div className="native-attempt-list"><strong>Native capability evidence</strong>
          {readiness.native.capabilities.map((capability) => <article className="native-attempt-row" key={capability.id}>
            <strong>{`${capability.command} ${capability.evidence}`}</strong><span>{capability.required ? "required" : "optional"}</span>
            <span>{capability.lastAttemptStatus ? `latest attempt: ${capability.lastAttemptStatus} at ${capability.lastAttemptAtIso}` : "no native attempt recorded"}</span>
          </article>)}
        </div>
        <div className="native-attempt-list"><strong>Native command attempts</strong>
          {nativeCommandAttempts.length === 0 ? <small>No native command attempts yet.</small> : nativeCommandAttempts.map((attempt) => (
            <article className="native-attempt-row" key={attempt.id}><strong>{`${attempt.command}: ${attempt.status}`}</strong><span>{attempt.requestSummary}</span><span>{attempt.resultSummary}</span></article>
          ))}
        </div>
        <div className="runtime-command-list">{readiness.runtime.commandSlots.map((slot) => (
          <article className="runtime-command-row" key={slot.id}>
            <div><StatusPill status={slot.state === "implemented_tauri_command" ? "ready" : slot.state === "planned_tauri_command" ? "queued" : "optional"} label={slot.state} /><strong>{slot.label}</strong></div>
            <code>{slot.tauriCommand}</code><span>request: {slot.requestFields.join(", ")}<br />response: {slot.responseFields.join(", ")}<br />fallback: {slot.fallback}</span>
          </article>
        ))}</div>
      </div>
      <div className="native-checklist"><strong>Native setup checklist</strong><div className="native-checklist-list">
        {readiness.nativeChecklist.map((item) => <article className="native-checklist-row" key={item.id}>
          <div><StatusPill status={item.state === "ready" ? "ready" : item.state === "blocked" ? "blocked" : "queued"} label={item.state} /><strong>{item.label}</strong></div>
          <span>{item.reference}</span><code>{item.verifyCommand}</code>{item.blockingJobs.length > 0 ? <small>jobs: {item.blockingJobs.join(", ")}</small> : null}
        </article>)}
      </div></div>
    </div>
  );
}

function NativeTextField({ label, value, placeholder, onChange }: { label: string; value: string; placeholder?: string; onChange: (value: string) => void }) {
  return <label className="native-root-field"><span>{label}</span><input aria-label={label} value={value} placeholder={placeholder} onChange={(event) => onChange(event.target.value)} /></label>;
}

function ProbeButton({ icon, label, onClick }: { icon: ReactNode; label: string; onClick: () => void }) {
  return <button type="button" className="button secondary native-probe-button" onClick={onClick}>{icon}{label}</button>;
}
