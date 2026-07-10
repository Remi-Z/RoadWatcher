# Project Identity and Snapshot Boundary Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give every RoadWatcher project a stable opaque identity and make portable snapshot loading versioned, validated, migratable, and recoverable.

**Architecture:** Canonical project-facing types move out of demo fixtures into `src/domain/projectModels.ts`. Snapshot creation accepts an existing project ID, while a dedicated boundary module parses JSON as unknown, migrates version 1 to version 2, validates invariants, and returns structured issues. The browser repository preserves its synchronous adapter for this phase but reports missing, corrupt, unsupported, and unavailable storage distinctly.

**Tech Stack:** TypeScript 5.9, React 19, Vitest 3, browser localStorage, no new runtime dependencies.

## Global Constraints

- Existing version-1 portable snapshots must remain importable.
- New projects receive `local-<uuid>` once; incident edits never change identity.
- Snapshot filenames may remain incident-derived, but filenames never define identity.
- Parsing starts from `unknown`; no unchecked `JSON.parse(...) as ProjectSnapshot` cast remains.
- Invalid numbers include `NaN`, infinities, negative durations, and source ranges outside known media duration.
- Duplicate IDs and dangling clip-to-media references are rejected.
- Browser behavior remains available and no native/final-evidence readiness claim is added.
- Every behavior change follows red-green-refactor.
- Update `README.md`, `docs/project-handoff.md`, and `docs/rewrite-manifest.md` before the module commit.

---

### Task 1: Canonical Project Domain Models

**Files:**
- Create: `src/domain/projectModels.ts`
- Modify: `src/data/demoProject.ts`
- Modify: `src/features/media/mediaImport.ts`
- Modify: `src/features/project/projectState.ts`
- Modify: `src/features/project/reviewReadiness.ts`
- Modify: `src/App.tsx`

**Interfaces:**
- Produces: `ProjectId`, `MediaAsset`, `IncidentDraft`, `ComponentSlotStatus`, and `ComponentSlot` from `src/domain/projectModels.ts`.
- Consumers keep the same property names and unions; this task changes ownership, not runtime behavior.

- [x] **Step 1: Create the canonical type module**

```ts
export declare const projectIdBrand: unique symbol;
export type ProjectId = string & { readonly [projectIdBrand]: true };

export interface MediaAsset {
  id: string;
  fileName: string;
  originalPath: string;
  durationSeconds: number;
  detectedStart: string;
  proxyStatus: "ready" | "running" | "queued" | "blocked";
  hash: string;
  fileSizeBytes: number;
}

export interface IncidentDraft {
  category: string;
  start: string;
  end: string;
  plate: string;
  vehicleNotes: string;
  locationNotes: string;
  narrative: string;
  provenance: string;
}

export type ComponentSlotStatus = "needed" | "optional" | "later" | "configured";

export interface ComponentSlot {
  id: string;
  label: string;
  ownerAction: string;
  status: ComponentSlotStatus;
  reference: string;
  notes: string;
}
```

- [x] **Step 2: Replace imports from `data/demoProject` with imports from `domain/projectModels`**

`demoProject.ts` imports the types and continues exporting only fixture values. Production modules and `App.tsx` import canonical types from the domain file.

- [x] **Step 3: Run type/build verification**

Run: `pnpm build`

Expected: TypeScript and Vite build exit 0 with no missing or duplicate type definitions.

- [ ] **Step 4: Commit the type-boundary refactor**

```powershell
git add src/domain/projectModels.ts src/data/demoProject.ts src/features/media/mediaImport.ts src/features/project/projectState.ts src/features/project/reviewReadiness.ts src/App.tsx
git commit -m "refactor: centralize project domain models"
```

### Task 2: Stable Project Identity

**Files:**
- Modify: `src/domain/projectModels.ts`
- Modify: `src/features/project/projectState.ts`
- Modify: `src/features/project/projectState.test.ts`
- Modify: `src/features/project/browserProjectRepository.test.ts`
- Modify: `src/features/project/downloadArtifacts.test.ts`
- Modify: `src/App.tsx`
- Modify: `src/App.test.tsx`

**Interfaces:**
- Produces: `createProjectId(uuid?: () => string): ProjectId`.
- Changes: `ProjectSnapshotInput.projectId` becomes required.
- Changes: `createProjectSnapshot(input)` preserves `input.projectId`.

- [ ] **Step 1: Write failing identity tests**

Add to `projectState.test.ts`:

```ts
it("keeps project identity stable when incident metadata changes", () => {
  const projectId = createProjectId(() => "11111111-1111-4111-8111-111111111111");
  const first = createProjectSnapshot({ ...snapshotInput(projectId), incident: incidentDraft });
  const second = createProjectSnapshot({
    ...snapshotInput(projectId),
    incident: { ...incidentDraft, plate: "EDITED", start: "00:42:00" }
  });

  expect(first.projectId).toBe(projectId);
  expect(second.projectId).toBe(projectId);
});

it("creates distinct opaque IDs for new projects", () => {
  expect(createProjectId(() => "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"))
    .not.toBe(createProjectId(() => "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"));
});
```

Add an App test that edits the plate, saves twice, and asserts the two repository snapshots have the same `projectId`.

- [ ] **Step 2: Run the focused tests and verify RED**

Run: `pnpm test -- src/features/project/projectState.test.ts src/App.test.tsx`

Expected: FAIL because `createProjectId` is missing and `ProjectSnapshotInput` does not own identity.

- [ ] **Step 3: Implement the project ID factory and required snapshot input**

```ts
export function createProjectId(uuid: () => string = () => globalThis.crypto.randomUUID()): ProjectId {
  return `local-${uuid()}` as ProjectId;
}

export interface ProjectSnapshotInput {
  projectId: ProjectId;
  // existing document fields remain unchanged
}

export function createProjectSnapshot(input: ProjectSnapshotInput): ProjectSnapshot {
  return {
    schemaVersion: PROJECT_SCHEMA_VERSION,
    projectId: input.projectId,
    // clone existing fields exactly as before
  };
}
```

In `App`, initialize identity once:

```ts
const [projectId, setProjectId] = useState<ProjectId>(
  () => restoredSnapshot?.projectId ?? createProjectId()
);
```

Include `projectId` in `currentSnapshotInput`, set it from imported snapshots, and create a new ID when clearing to a new seeded project.

- [ ] **Step 4: Update all snapshot test fixtures/call sites to pass a deterministic project ID**

Use explicit values such as `"local-test-project" as ProjectId`; do not add a default that would allow saves to regenerate identity.

- [ ] **Step 5: Run focused tests and verify GREEN**

Run: `pnpm test -- src/features/project/projectState.test.ts src/features/project/browserProjectRepository.test.ts src/features/project/downloadArtifacts.test.ts src/App.test.tsx`

Expected: all selected files pass; changing incident fields does not change `projectId`.

- [ ] **Step 6: Commit stable identity**

```powershell
git add src/domain/projectModels.ts src/features/project/projectState.ts src/features/project/projectState.test.ts src/features/project/browserProjectRepository.test.ts src/features/project/downloadArtifacts.test.ts src/App.tsx src/App.test.tsx
git commit -m "fix: preserve stable project identity"
```

### Task 3: Versioned Snapshot Parser and Version-1 Migration

**Files:**
- Create: `src/features/project/projectSnapshotSchema.ts`
- Create: `src/features/project/projectSnapshotSchema.test.ts`
- Modify: `src/features/project/projectState.ts`
- Modify: `src/features/project/projectState.test.ts`

**Interfaces:**
- Produces: `SnapshotParseIssue`, `SnapshotParseResult`, `tryParseSnapshot(text)`, and `parseSnapshot(text)`.
- Current schema version becomes literal `2`.
- Version-1 input migrates to version 2 while retaining its project ID and filling optional arrays.

- [ ] **Step 1: Write failing migration and rejection tests**

```ts
it("migrates a version-1 snapshot and retains its identity", () => {
  const legacy = JSON.stringify({
    ...validSnapshotV2(),
    schemaVersion: 1,
    projectId: "local-legacy-review",
    componentSlots: undefined,
    nativeCommandAttempts: undefined,
    route: undefined,
    officialFeatures: undefined
  });

  const result = tryParseSnapshot(legacy);

  expect(result).toMatchObject({
    ok: true,
    snapshot: {
      schemaVersion: 2,
      projectId: "local-legacy-review",
      componentSlots: [],
      nativeCommandAttempts: [],
      route: [],
      officialFeatures: []
    }
  });
});

it.each([
  ["unsupported_version", { ...validSnapshotV2(), schemaVersion: 99 }],
  ["invalid_field", { ...validSnapshotV2(), media: [{ ...mediaAssets[0], durationSeconds: Number.NaN }] }],
  ["duplicate_id", { ...validSnapshotV2(), media: [mediaAssets[0], mediaAssets[0]] }],
  ["dangling_reference", { ...validSnapshotV2(), clips: [{ ...initialClips[0], mediaId: "missing" }] }],
  ["invalid_range", { ...validSnapshotV2(), clips: [{ ...initialClips[0], sourceOutSeconds: 99999 }] }]
])("rejects %s snapshots", (code, value) => {
  expect(tryParseSnapshot(JSON.stringify(value))).toMatchObject({ ok: false, issue: { code } });
});
```

- [ ] **Step 2: Run parser tests and verify RED**

Run: `pnpm test -- src/features/project/projectSnapshotSchema.test.ts`

Expected: FAIL because the boundary module does not exist.

- [ ] **Step 3: Implement structured parsing and migration**

The module exposes these exact shapes:

```ts
export type SnapshotParseIssueCode =
  | "invalid_json"
  | "invalid_root"
  | "unsupported_version"
  | "invalid_field"
  | "duplicate_id"
  | "dangling_reference"
  | "invalid_range";

export interface SnapshotParseIssue {
  code: SnapshotParseIssueCode;
  path: string;
  message: string;
}

export type SnapshotParseResult =
  | { ok: true; snapshot: ProjectSnapshot }
  | { ok: false; issue: SnapshotParseIssue };

export class ProjectSnapshotParseError extends Error {
  constructor(readonly issue: SnapshotParseIssue) {
    super(issue.message);
    this.name = "ProjectSnapshotParseError";
  }
}
```

Use focused assertion helpers rather than unchecked casts:

```ts
function record(value: unknown, path: string): Record<string, unknown> {
  if (!value || typeof value !== "object" || Array.isArray(value)) {
    throw issue("invalid_field", path, `${path} must be an object.`);
  }
  return value as Record<string, unknown>;
}

function finiteNumber(value: unknown, path: string, minimum = Number.NEGATIVE_INFINITY): number {
  if (typeof value !== "number" || !Number.isFinite(value) || value < minimum) {
    throw issue("invalid_field", path, `${path} must be a finite number at least ${minimum}.`);
  }
  return value;
}

function nonBlankString(value: unknown, path: string): string {
  if (typeof value !== "string" || !value.trim()) {
    throw issue("invalid_field", path, `${path} must be a non-blank string.`);
  }
  return value;
}
```

Parse every persisted field, validate enum unions, validate `savedAtIso` with `Date.parse`, then run aggregate invariants:

```ts
function validateAggregate(snapshot: ProjectSnapshot): void {
  assertUnique(snapshot.media.map((item) => item.id), "media");
  assertUnique(snapshot.clips.map((item) => item.id), "clips");
  assertUnique(snapshot.jobs.map((item) => item.id), "jobs");
  assertUnique(snapshot.officialFeatures.map((item) => item.id), "officialFeatures");

  const mediaById = new Map(snapshot.media.map((item) => [item.id, item]));
  for (const [index, clip] of snapshot.clips.entries()) {
    const media = mediaById.get(clip.mediaId);
    if (!media) throw issue("dangling_reference", `clips[${index}].mediaId`, `Unknown media ${clip.mediaId}.`);
    if (clip.sourceInSeconds < 0 || clip.sourceOutSeconds <= clip.sourceInSeconds) {
      throw issue("invalid_range", `clips[${index}]`, "Clip source range must have positive duration.");
    }
    if (media.durationSeconds > 0 && clip.sourceOutSeconds > media.durationSeconds) {
      throw issue("invalid_range", `clips[${index}].sourceOutSeconds`, "Clip exceeds known media duration.");
    }
  }
}
```

`tryParseSnapshot` catches JSON errors and thrown issues; `parseSnapshot` unwraps the result and throws `ProjectSnapshotParseError` for compatibility.

- [ ] **Step 4: Route `projectState.parseSnapshot` through the new boundary and advance schema version**

Re-export `parseSnapshot` and `tryParseSnapshot` from `projectState.ts` so existing imports remain stable. `createProjectSnapshot` emits `schemaVersion: 2`.

- [ ] **Step 5: Run parser/project tests and verify GREEN**

Run: `pnpm test -- src/features/project/projectSnapshotSchema.test.ts src/features/project/projectState.test.ts`

Expected: all tests pass, including version-1 migration and invalid aggregate rejection.

- [ ] **Step 6: Commit snapshot validation**

```powershell
git add src/features/project/projectSnapshotSchema.ts src/features/project/projectSnapshotSchema.test.ts src/features/project/projectState.ts src/features/project/projectState.test.ts
git commit -m "feat: validate and migrate project snapshots"
```

### Task 4: Structured Browser Repository Load Results

**Files:**
- Modify: `src/features/project/browserProjectRepository.ts`
- Modify: `src/features/project/browserProjectRepository.test.ts`
- Modify: `src/App.tsx`
- Modify: `src/App.test.tsx`

**Interfaces:**
- Changes: `ProjectRepository.load()` returns `ProjectLoadResult`.
- Produces: missing, loaded, corrupt, unsupported, and unavailable outcomes.

- [ ] **Step 1: Write failing repository result tests**

```ts
it("distinguishes missing, corrupt, unsupported, and unavailable project data", () => {
  expect(createBrowserProjectRepository(createMemoryStorage()).load()).toEqual({ status: "missing" });

  const corrupt = createMemoryStorage();
  corrupt.setItem(DEFAULT_PROJECT_STORAGE_KEY, "{not-json");
  expect(createBrowserProjectRepository(corrupt).load()).toMatchObject({ status: "corrupt", issue: { code: "invalid_json" } });

  const unsupported = createMemoryStorage();
  unsupported.setItem(DEFAULT_PROJECT_STORAGE_KEY, JSON.stringify({ schemaVersion: 99 }));
  expect(createBrowserProjectRepository(unsupported).load()).toMatchObject({ status: "unsupported", issue: { code: "unsupported_version" } });

  expect(createBrowserProjectRepository(null).load()).toEqual({ status: "unavailable" });
});
```

- [ ] **Step 2: Run repository tests and verify RED**

Run: `pnpm test -- src/features/project/browserProjectRepository.test.ts`

Expected: FAIL because `load()` currently collapses every outcome to a snapshot or null.

- [ ] **Step 3: Implement repository load outcomes**

```ts
export type ProjectLoadResult =
  | { status: "loaded"; snapshot: ProjectSnapshot }
  | { status: "missing" }
  | { status: "corrupt"; issue: SnapshotParseIssue }
  | { status: "unsupported"; issue: SnapshotParseIssue }
  | { status: "unavailable"; message?: string };
```

`load()` returns `unavailable` when storage is absent or throws, `missing` when the key is empty, `loaded` on valid/migrated data, and maps `unsupported_version` separately from other parse issues.

- [ ] **Step 4: Update App initialization and import feedback**

Resolve the initial result once:

```ts
const [initialLoad] = useState(() => projectRepository.load());
const restoredSnapshot = initialLoad.status === "loaded" ? initialLoad.snapshot : null;
```

Set status text for corrupt, unsupported, and unavailable storage without discarding the seeded fallback. Project JSON imports report their structured snapshot issue and are not retried as GeoJSON when the root declares a `schemaVersion`.

- [ ] **Step 5: Run repository/App tests and verify GREEN**

Run: `pnpm test -- src/features/project/browserProjectRepository.test.ts src/App.test.tsx`

Expected: all selected tests pass and each load failure has distinct UI/repository evidence.

- [ ] **Step 6: Commit repository recovery behavior**

```powershell
git add src/features/project/browserProjectRepository.ts src/features/project/browserProjectRepository.test.ts src/App.tsx src/App.test.tsx
git commit -m "feat: report project recovery outcomes"
```

### Task 5: Handoff Documentation and Module Verification

**Files:**
- Modify: `README.md`
- Modify: `docs/project-handoff.md`
- Modify: `docs/rewrite-manifest.md`
- Modify: `docs/superpowers/plans/2026-07-10-project-identity-snapshot-boundary.md`

**Interfaces:**
- Documents the shipped schema version, version-1 migration, stable identity, validation rules, and structured recovery outcomes.

- [ ] **Step 1: Update the three handoff documents**

Record:

- current schema version is 2;
- version-1 snapshots migrate automatically;
- project identity is generated once and survives incident edits;
- malformed snapshots, duplicate IDs, dangling media, and invalid clip ranges are rejected;
- browser storage distinguishes missing, corrupt, unsupported, and unavailable states;
- the next module is atomic workstation state/import orchestration.

- [ ] **Step 2: Mark completed plan checkboxes accurately**

Only change checkboxes whose commands and commits actually succeeded.

- [ ] **Step 3: Run full verification**

Run: `pnpm test`

Expected: all test files and tests pass.

Run: `pnpm build`

Expected: TypeScript and Vite production build exit 0.

Run: `git diff --check`

Expected: exit 0; line-ending notices are acceptable, whitespace errors are not.

- [ ] **Step 4: Commit module documentation**

```powershell
git add README.md docs/project-handoff.md docs/rewrite-manifest.md docs/superpowers/plans/2026-07-10-project-identity-snapshot-boundary.md
git commit -m "docs: hand off validated project snapshots"
```

- [ ] **Step 5: Confirm a clean module boundary**

Run: `git status --short`

Expected: no unstaged or untracked files from this module.
