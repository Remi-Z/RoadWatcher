import { serializeSnapshot, type EvidencePacket, type ProjectSnapshot } from "./projectState";

export interface DownloadArtifact {
  fileName: string;
  mimeType: string;
  content: string;
  href: string;
}

export function createPacketArtifacts(packet: EvidencePacket): DownloadArtifact[] {
  return [
    {
      fileName: `${packet.fileBaseName}.json`,
      mimeType: "application/json",
      content: JSON.stringify(packet.summaryJson, null, 2),
      href: toDataHref("application/json", JSON.stringify(packet.summaryJson, null, 2))
    },
    {
      fileName: `${packet.fileBaseName}.md`,
      mimeType: "text/markdown",
      content: packet.summaryMarkdown,
      href: toDataHref("text/markdown", packet.summaryMarkdown)
    }
  ];
}

export function createNativeSetupChecklistArtifact(packet: EvidencePacket): DownloadArtifact {
  const content = buildNativeSetupChecklistMarkdown(packet);
  return {
    fileName: `${packet.fileBaseName}-native-setup.md`,
    mimeType: "text/markdown",
    content,
    href: toDataHref("text/markdown", content)
  };
}

export function createProjectSnapshotArtifact(snapshot: ProjectSnapshot): DownloadArtifact {
  const content = serializeSnapshot(snapshot);
  return {
    fileName: `${snapshot.projectId}-project.json`,
    mimeType: "application/json",
    content,
    href: toDataHref("application/json", content)
  };
}

function buildNativeSetupChecklistMarkdown(packet: EvidencePacket): string {
  const runtime = packet.summaryJson.reviewReadiness.runtime;
  const commandLines = runtime.commandSlots
    .map(
      (slot, index) =>
        `${index + 1}. ${slot.label}\n   - state: ${slot.state}\n   - command: \`${slot.tauriCommand}\`\n   - request: ${slot.requestFields.join(
          ", "
        )}\n   - response: ${slot.responseFields.join(", ")}\n   - fallback: ${slot.fallback}\n   - action: ${slot.ownerAction}`
    )
    .join("\n\n");
  const checklistLines = packet.summaryJson.reviewReadiness.nativeChecklist
    .map((item, index) => {
      const blockedJobs = item.blockingJobs.length > 0 ? `\n   - jobs: ${item.blockingJobs.join(", ")}` : "";
      const notes = item.notes.trim() ? `\n   - notes: ${item.notes.trim()}` : "";
      return `${index + 1}. ${item.label}\n   - state: ${item.state}\n   - reference: ${item.reference.trim() || "(blank)"}\n   - verify: \`${
        item.verifyCommand
      }\`${blockedJobs}${notes}\n   - action: ${item.ownerAction}`;
    })
    .join("\n\n");

  return `# RoadWatcher Native Setup Checklist

- Project: ${packet.summaryJson.projectId}
- Saved: ${packet.summaryJson.savedAtIso}
- Review mode: ${packet.summaryJson.reviewReadiness.mode === "native_ready" ? "Native ready" : "Browser fallback"}
- Runtime mode: ${runtime.label}
- Runtime summary: ${runtime.summary}
- Summary: ${packet.summaryJson.reviewReadiness.summary}

## Native Command Slots
${commandLines || "No native command slots saved."}

## Install And Data Slots
${checklistLines || "No native setup slots saved."}
`;
}

function toDataHref(mimeType: string, content: string): string {
  return `data:${mimeType};charset=utf-8,${encodeURIComponent(content)}`;
}
