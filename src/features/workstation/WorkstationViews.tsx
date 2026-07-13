import { useSortable } from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";
import {
  Bike,
  CheckCircle2,
  CircleDot,
  Clock3,
  Copy,
  Route,
  Split,
  Trash2
} from "lucide-react";
import { type ReactNode, type RefObject, useMemo } from "react";
import type { ComponentSlot, ComponentSlotStatus, IncidentDraft } from "../../domain/projectModels";
import type {
  CvFindingReview,
  CvFindingReviewStatus,
  WorkstationJob
} from "../jobs/jobModel";
import type {
  OfficialRoadFeature,
  ProjectedRoadFeature,
  TimedRoutePoint
} from "../geo/projection";
import type { NativeReadinessChecklistItem } from "../project/reviewReadiness";
import type { TimelineClip } from "../timeline/timelineModel";
import { clipDurationSeconds } from "../timeline/timelineModel";

export function PanelHeader({ icon, title, meta }: { icon: ReactNode; title: string; meta: string }) {
  return (
    <div className="panel-header">
      <div>
        <span className="header-icon" aria-hidden="true">{icon}</span>
        <h2>{title}</h2>
      </div>
      <span>{meta}</span>
    </div>
  );
}

export function SortableClip({
  clip,
  totalDuration,
  selected,
  onSelect
}: {
  clip: TimelineClip;
  totalDuration: number;
  selected: boolean;
  onSelect: (id: string) => void;
}) {
  const { attributes, listeners, setNodeRef, transform, transition } = useSortable({ id: clip.id });
  const style = {
    transform: CSS.Transform.toString(transform),
    transition,
    flexBasis: `${Math.max(18, (clipDurationSeconds(clip) / Math.max(totalDuration, 1)) * 100)}%`
  };

  return (
    <button
      ref={setNodeRef}
      style={style}
      type="button"
      aria-label={`${clip.label} ${formatSeconds(clip.sourceInSeconds)} - ${formatSeconds(clip.sourceOutSeconds)}`}
      className={`timeline-clip${selected ? " selected" : ""}`}
      onClick={() => onSelect(clip.id)}
      onFocus={() => onSelect(clip.id)}
      onPointerDown={() => onSelect(clip.id)}
      onKeyDown={(event) => {
        if (event.key === "Enter" || event.key === " ") onSelect(clip.id);
      }}
      {...attributes}
      {...listeners}
    >
      <span>{clip.label}</span>
      <strong>{formatSeconds(clip.sourceInSeconds)} - {formatSeconds(clip.sourceOutSeconds)}</strong>
    </button>
  );
}

export function RouteMap({
  route,
  projectedFeatures,
  matchSummary
}: {
  route: TimedRoutePoint[];
  projectedFeatures: ProjectedRoadFeature[];
  matchSummary: string;
}) {
  const routePath = useMemo(
    () => route.map((point, index) => {
      const x = 12 + index * (76 / Math.max(route.length - 1, 1));
      const y = 72 - index * (38 / Math.max(route.length - 1, 1)) + (index % 2) * 4;
      return `${index === 0 ? "M" : "L"} ${x} ${y}`;
    }).join(" "),
    [route]
  );
  const routeEndpoints = useMemo(() => summarizeRouteEndpoints(route), [route]);

  return (
    <div className="map-canvas">
      <svg viewBox="0 0 100 100" role="img" aria-label="Route with projected official road features">
        <path className="map-grid-line" d="M0 30H100 M0 60H100 M25 0V100 M55 0V100 M82 0V100" />
        {route.length > 0 ? <path className="raw-gpx" d="M12 74 L29 62 L48 58 L67 42 L86 34" /> : null}
        <path className="matched-route" d={routePath} />
        {projectedFeatures.map((feature, index) => (
          <g key={feature.featureId} transform={`translate(${29 + index * 20} ${62 - index * 12})`}>
            <circle className={`feature-dot ${feature.kind}`} r="3.8" />
            <text x="6" y="2.5">{feature.kind === "traffic_light" ? "signal" : feature.kind.replace("_", " ")}</text>
          </g>
        ))}
      </svg>
      <div className="map-legend">
        <span aria-label="Route point count"><Clock3 size={14} />{route.length} timed points</span>
        <span><Route size={14} />{matchSummary}</span>
        <span><CircleDot size={14} />Raw GPX</span>
        <span><Bike size={14} />Official GIS projection</span>
      </div>
      <div className="route-endpoint-summary" aria-label="Route endpoint summary">
        {routeEndpoints.map((endpoint) => (
          <span key={endpoint.label}><strong>{endpoint.label}</strong> {endpoint.value}</span>
        ))}
      </div>
    </div>
  );
}

export function TimelineEditor({
  clip,
  clipCount,
  onTrim,
  onSplit,
  onDuplicate,
  onRemove
}: {
  clip?: TimelineClip;
  clipCount: number;
  onTrim: (field: "sourceInSeconds" | "sourceOutSeconds", value: string) => void;
  onSplit: () => void;
  onDuplicate: () => void;
  onRemove: () => void;
}) {
  if (!clip) return null;

  const canSplit = clipDurationSeconds(clip) >= 2;
  function runPointerCommand(event: React.PointerEvent<HTMLButtonElement>, command: () => void) {
    event.currentTarget.dataset.pointerHandled = "true";
    command();
  }
  function runClickCommand(event: React.MouseEvent<HTMLButtonElement>, command: () => void) {
    if (event.currentTarget.dataset.pointerHandled === "true") {
      delete event.currentTarget.dataset.pointerHandled;
      return;
    }
    command();
  }
  function handleActionMenu(value: string) {
    if (value === "split") onSplit();
    if (value === "duplicate") onDuplicate();
    if (value === "remove") onRemove();
  }

  return (
    <div className="timeline-editor" aria-label="Selected clip editor">
      <div><strong>{clip.label}</strong><span>{Math.round(clipDurationSeconds(clip))}s selected clip</span></div>
      <label>
        <span>Source in</span>
        <input aria-label="Selected clip source in seconds" min={0} step={1} type="number"
          value={Math.round(clip.sourceInSeconds)} onChange={(event) => onTrim("sourceInSeconds", event.target.value)} />
      </label>
      <label>
        <span>Source out</span>
        <input aria-label="Selected clip source out seconds" min={1} step={1} type="number"
          value={Math.round(clip.sourceOutSeconds)} onChange={(event) => onTrim("sourceOutSeconds", event.target.value)} />
      </label>
      <label>
        <span>Action</span>
        <select aria-label="Selected clip action" value="" onChange={(event) => handleActionMenu(event.target.value)}>
          <option value="">Choose</option>
          <option value="split" disabled={!canSplit}>Split</option>
          <option value="duplicate">Duplicate</option>
          <option value="remove" disabled={clipCount <= 1}>Remove</option>
        </select>
      </label>
      <div className="timeline-edit-actions">
        <button type="button" className="button secondary timeline-command-button" aria-label="Split selected clip"
          onPointerDown={(event) => runPointerCommand(event, onSplit)} onClick={(event) => runClickCommand(event, onSplit)} disabled={!canSplit}>
          <Split size={15} />Split
        </button>
        <button type="button" className="button secondary timeline-command-button" aria-label="Duplicate selected clip"
          onPointerDown={(event) => runPointerCommand(event, onDuplicate)} onClick={(event) => runClickCommand(event, onDuplicate)}>
          <Copy size={15} />Duplicate
        </button>
        <button type="button" className="button secondary timeline-command-button" aria-label="Remove selected clip"
          onPointerDown={(event) => runPointerCommand(event, onRemove)} onClick={(event) => runClickCommand(event, onRemove)} disabled={clipCount <= 1}>
          <Trash2 size={15} />Remove
        </button>
      </div>
    </div>
  );
}

export function Inspector({
  draft,
  onDraftChange,
  onSaveDraft
}: {
  draft: IncidentDraft;
  onDraftChange: (field: keyof IncidentDraft, value: string) => void;
  onSaveDraft: () => void;
}) {
  return (
    <form className="inspector-form">
      <InspectorField label="Category" value={draft.category} onChange={(value) => onDraftChange("category", value)} />
      <InspectorField label="Start" value={draft.start} onChange={(value) => onDraftChange("start", value)} />
      <InspectorField label="End" value={draft.end} onChange={(value) => onDraftChange("end", value)} />
      <InspectorField label="Plate" value={draft.plate} placeholder="manual entry needed" onChange={(value) => onDraftChange("plate", value)} />
      <InspectorField label="Vehicle notes" value={draft.vehicleNotes} onChange={(value) => onDraftChange("vehicleNotes", value)} />
      <InspectorField label="Location notes" value={draft.locationNotes} onChange={(value) => onDraftChange("locationNotes", value)} />
      <InspectorField label="Provenance" value={draft.provenance} onChange={(value) => onDraftChange("provenance", value)} />
      <label><span>Narrative</span><textarea value={draft.narrative} onChange={(event) => onDraftChange("narrative", event.target.value)} /></label>
      <button type="button" className="button primary full-width" onClick={onSaveDraft}><CheckCircle2 size={16} />Save draft incident</button>
    </form>
  );
}

function InspectorField({
  label,
  value,
  placeholder,
  onChange
}: {
  label: string;
  value: string;
  placeholder?: string;
  onChange: (value: string) => void;
}) {
  return <label><span>{label}</span><input value={value} placeholder={placeholder} onChange={(event) => onChange(event.target.value)} /></label>;
}

export function JobList({
  jobs,
  nativeChecklist,
  onCancelProxy
}: {
  jobs: WorkstationJob[];
  nativeChecklist: NativeReadinessChecklistItem[];
  onCancelProxy: (job: WorkstationJob) => void;
}) {
  return (
    <div className="job-list">
      {jobs.length === 0 ? <p className="empty-state">No processing jobs. Import media, GPX, or GIS data to queue work.</p> : null}
      {jobs.map((job) => {
        const blockers = nativeChecklist.filter((item) => item.blockingJobs.includes(job.label));
        return (
          <article className="job-row" key={job.id}>
            <div><StatusPill status={job.status} label={job.status} /><strong>{job.label}</strong></div>
            <div className="progress-line" aria-label={`${job.label} progress`}><span style={{ width: `${job.progress}%` }} /></div>
            <p>{job.detail}</p>
            {job.type === "proxy" && job.status === "running" && job.mediaId ? (
              <button type="button" className="button secondary" onClick={() => onCancelProxy(job)}>Cancel {job.label}</button>
            ) : null}
            {blockers.length > 0 ? (
              <div className="job-blocker-list">
                {blockers.map((blocker) => <div className="job-blocker" key={blocker.id}><span>Unblock with {blocker.label}</span><code>{blocker.verifyCommand}</code></div>)}
              </div>
            ) : null}
          </article>
        );
      })}
    </div>
  );
}

export function ProjectedFeatureList({
  projectedFeatures,
  officialFeatures,
  onFeatureReviewChange
}: {
  projectedFeatures: ProjectedRoadFeature[];
  officialFeatures: OfficialRoadFeature[];
  onFeatureReviewChange: (featureId: string, field: keyof Pick<ProjectedRoadFeature, "reviewStatus" | "reviewNote">, value: string) => void;
}) {
  const officialFeaturesById = useMemo(() => new Map(officialFeatures.map((feature) => [feature.id, feature])), [officialFeatures]);
  return (
    <div className="feature-review-list">
      {projectedFeatures.length === 0 ? <p className="empty-state">No projected official features to review.</p> : null}
      {projectedFeatures.map((feature) => {
        const source = officialFeaturesById.get(feature.featureId);
        return (
          <article className="feature-review-row" key={feature.featureId}>
            <div>
              <strong>{formatFeatureKind(feature.kind)}</strong><span>{feature.sourceLayer}</span>
              {source?.sourceCrs ? <small>{`${source.sourceCrs} → ${source.normalizedCrs} · ${source.sourcePath}`}</small> : null}
            </div>
            <div><span>{Math.round(feature.timeSeconds)}s</span><StatusPill status={feature.confidence >= 0.8 ? "ready" : "queued"} label={`${Math.round(feature.confidence * 100)}%`} /></div>
            <div className="feature-review-controls">
              <label><span>Status</span><select aria-label={`${formatFeatureKind(feature.kind)} ${feature.featureId} review status`}
                value={feature.reviewStatus} onChange={(event) => onFeatureReviewChange(feature.featureId, "reviewStatus", event.target.value)}>
                <option value="needs_review">needs review</option><option value="included">included</option><option value="excluded">excluded</option>
              </select></label>
              <label><span>Note</span><textarea aria-label={`${formatFeatureKind(feature.kind)} ${feature.featureId} review note`}
                value={feature.reviewNote} onChange={(event) => onFeatureReviewChange(feature.featureId, "reviewNote", event.target.value)} /></label>
            </div>
          </article>
        );
      })}
    </div>
  );
}

export function CvFindingList({ findings, onReview, onPersist }: {
  findings: CvFindingReview[];
  onReview: (findingId: string, status: CvFindingReviewStatus, note: string) => void;
  onPersist: (findingId: string, status: CvFindingReviewStatus, note: string) => void;
}) {
  if (findings.length === 0) return <p className="empty-state">No local CV findings. Configure a model and scan imported media.</p>;
  return (
    <div className="feature-review-list" aria-label="CV finding reviews">
      {findings.map((finding) => (
        <article className="feature-review-row" key={finding.id}>
          <div>
            <strong>{finding.label}</strong>
            <span>{finding.timeSeconds.toFixed(1)}s · confidence {Math.round(finding.confidence * 100)}%</span>
            <small>{finding.engine} · {finding.modelPath}</small>
            <small>Box {Math.round(finding.x)},{Math.round(finding.y)} {Math.round(finding.width)}×{Math.round(finding.height)} / {finding.frameWidth}×{finding.frameHeight}</small>
          </div>
          <label><span>Decision</span><select aria-label={`${finding.label} ${finding.id} CV review status`} value={finding.reviewStatus}
            onChange={(event) => onPersist(finding.id, event.target.value as CvFindingReviewStatus, finding.reviewNote)}>
            <option value="needs_review">needs review</option><option value="included">included</option><option value="excluded">excluded</option>
          </select></label>
          <label><span>Review note</span><input aria-label={`${finding.label} ${finding.id} CV review note`} value={finding.reviewNote}
            onChange={(event) => onReview(finding.id, finding.reviewStatus, event.target.value)}
            onBlur={(event) => onPersist(finding.id, finding.reviewStatus, event.target.value)} /></label>
        </article>
      ))}
    </div>
  );
}

export function ComponentSlotList({
  checklist,
  firstReferenceInputRef,
  slots,
  onSlotChange
}: {
  checklist: NativeReadinessChecklistItem[];
  firstReferenceInputRef?: RefObject<HTMLInputElement | null>;
  slots: ComponentSlot[];
  onSlotChange: (id: string, field: keyof Pick<ComponentSlot, "status" | "reference" | "notes">, value: string) => void;
}) {
  const checklistBySlotId = useMemo(() => new Map(checklist.map((item) => [item.id, item])), [checklist]);
  return (
    <div className="slot-list">
      {slots.map((slot, index) => {
        const checklistItem = checklistBySlotId.get(slot.id);
        return (
          <article className="slot-row component-slot-row" key={slot.id}>
            <div className="slot-summary"><StatusPill status={componentSlotPillStatus(slot.status)} label={slot.status} /><div><strong>{slot.label}</strong><span>{slot.ownerAction}</span></div></div>
            <div className="slot-fields">
              <label><span>Reference</span><input aria-label={`${slot.label} reference`} ref={index === 0 ? firstReferenceInputRef : undefined}
                value={slot.reference} onChange={(event) => onSlotChange(slot.id, "reference", event.target.value)} /></label>
              <label><span>Status</span><select aria-label={`${slot.label} status`} value={slot.status}
                onChange={(event) => onSlotChange(slot.id, "status", event.target.value)}>
                <option value="needed">needed</option><option value="configured">configured</option><option value="optional">optional</option><option value="later">later</option>
              </select></label>
              <div className="slot-verify-command"><span>Verify</span><code>{checklistItem?.verifyCommand ?? "manual verification required"}</code></div>
              <label className="slot-notes-field"><span>Notes</span><textarea aria-label={`${slot.label} notes`} value={slot.notes}
                onChange={(event) => onSlotChange(slot.id, "notes", event.target.value)} /></label>
            </div>
          </article>
        );
      })}
    </div>
  );
}

export function StatusPill({ status, label }: { status: string; label: string }) {
  return <span className={`status-pill ${status}`}>{label}</span>;
}

function summarizeRouteEndpoints(route: TimedRoutePoint[]): { label: string; value: string }[] {
  if (route.length === 0) return [{ label: "Route", value: "No timed points imported" }];
  return [
    { label: "First point", value: formatRoutePoint(route[0]) },
    { label: "Last point", value: formatRoutePoint(route[route.length - 1]) }
  ];
}

function formatRoutePoint(point: TimedRoutePoint): string {
  return `${point.latitude.toFixed(6)}, ${point.longitude.toFixed(6)} at ${Math.round(point.timeSeconds)}s`;
}

function formatFeatureKind(kind: string): string {
  return kind.replace("_", " ");
}

function componentSlotPillStatus(status: ComponentSlotStatus): string {
  if (status === "configured") return "ready";
  if (status === "needed") return "blocked";
  return "queued";
}

function formatSeconds(seconds: number): string {
  const minutes = Math.floor(seconds / 60);
  const remainder = Math.round(seconds % 60).toString().padStart(2, "0");
  return `${minutes}:${remainder}`;
}
