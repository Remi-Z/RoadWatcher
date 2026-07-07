import { describe, expect, it } from "vitest";
import {
  clampClipToMedia,
  duplicateClip,
  moveClip,
  removeClip,
  splitClipInTimeline,
  splitClipAt,
  timelineDurationSeconds,
  trimClipSourceRange
} from "./timelineModel";

describe("timeline model", () => {
  it("clamps clip source range to media duration and preserves reel order", () => {
    const clip = clampClipToMedia(
      {
        id: "clip-1",
        mediaId: "media-1",
        sourceInSeconds: -4,
        sourceOutSeconds: 370,
        reelStartSeconds: 12,
        label: "Stop sign approach"
      },
      300
    );

    expect(clip.sourceInSeconds).toBe(0);
    expect(clip.sourceOutSeconds).toBe(300);
    expect(clip.reelStartSeconds).toBe(12);
  });

  it("splits a clip at an internal source time and reseats following reel starts", () => {
    const [left, right] = splitClipAt(
      {
        id: "clip-1",
        mediaId: "media-1",
        sourceInSeconds: 10,
        sourceOutSeconds: 40,
        reelStartSeconds: 0,
        label: "Main incident"
      },
      22,
      "clip-2"
    );

    expect(left.sourceOutSeconds).toBe(22);
    expect(left.reelStartSeconds).toBe(0);
    expect(right.sourceInSeconds).toBe(22);
    expect(right.sourceOutSeconds).toBe(40);
    expect(right.reelStartSeconds).toBe(12);
  });

  it("moves clips by id and recomputes contiguous reel timing", () => {
    const moved = moveClip(
      [
        {
          id: "a",
          mediaId: "m",
          sourceInSeconds: 0,
          sourceOutSeconds: 5,
          reelStartSeconds: 0,
          label: "A"
        },
        {
          id: "b",
          mediaId: "m",
          sourceInSeconds: 20,
          sourceOutSeconds: 32,
          reelStartSeconds: 5,
          label: "B"
        },
        {
          id: "c",
          mediaId: "m",
          sourceInSeconds: 50,
          sourceOutSeconds: 58,
          reelStartSeconds: 17,
          label: "C"
        }
      ],
      "c",
      0
    );

    expect(moved.map((clip) => clip.id)).toEqual(["c", "a", "b"]);
    expect(moved.map((clip) => clip.reelStartSeconds)).toEqual([0, 8, 13]);
    expect(timelineDurationSeconds(moved)).toBe(25);
  });

  it("trims a clip source range and reseats following reel timing", () => {
    const trimmed = trimClipSourceRange(
      [
        {
          id: "a",
          mediaId: "m",
          sourceInSeconds: 10,
          sourceOutSeconds: 40,
          reelStartSeconds: 0,
          label: "A"
        },
        {
          id: "b",
          mediaId: "m",
          sourceInSeconds: 50,
          sourceOutSeconds: 65,
          reelStartSeconds: 30,
          label: "B"
        }
      ],
      "a",
      14,
      28,
      120
    );

    expect(trimmed[0]).toMatchObject({ sourceInSeconds: 14, sourceOutSeconds: 28, reelStartSeconds: 0 });
    expect(trimmed[1].reelStartSeconds).toBe(14);
    expect(timelineDurationSeconds(trimmed)).toBe(29);
  });

  it("splits, duplicates, and removes clips in a timeline while keeping contiguous reel starts", () => {
    const clips = [
      {
        id: "incident",
        mediaId: "m",
        sourceInSeconds: 100,
        sourceOutSeconds: 120,
        reelStartSeconds: 0,
        label: "Incident"
      },
      {
        id: "after",
        mediaId: "m",
        sourceInSeconds: 140,
        sourceOutSeconds: 150,
        reelStartSeconds: 20,
        label: "After"
      }
    ];

    const split = splitClipInTimeline(clips, "incident", 112, "incident-right");
    expect(split.map((clip) => clip.id)).toEqual(["incident", "incident-right", "after"]);
    expect(split.map((clip) => clip.reelStartSeconds)).toEqual([0, 12, 20]);

    const duplicated = duplicateClip(split, "incident-right", "incident-copy");
    expect(duplicated.map((clip) => clip.id)).toEqual(["incident", "incident-right", "incident-copy", "after"]);
    expect(duplicated[2]).toMatchObject({ label: "Incident copy", sourceInSeconds: 112, sourceOutSeconds: 120 });
    expect(duplicated.map((clip) => clip.reelStartSeconds)).toEqual([0, 12, 20, 28]);

    const removed = removeClip(duplicated, "incident-right");
    expect(removed.map((clip) => clip.id)).toEqual(["incident", "incident-copy", "after"]);
    expect(removed.map((clip) => clip.reelStartSeconds)).toEqual([0, 12, 20]);
  });
});
