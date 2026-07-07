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

export function createProjectSnapshotArtifact(snapshot: ProjectSnapshot): DownloadArtifact {
  const content = serializeSnapshot(snapshot);
  return {
    fileName: `${snapshot.projectId}-project.json`,
    mimeType: "application/json",
    content,
    href: toDataHref("application/json", content)
  };
}

function toDataHref(mimeType: string, content: string): string {
  return `data:${mimeType};charset=utf-8,${encodeURIComponent(content)}`;
}
