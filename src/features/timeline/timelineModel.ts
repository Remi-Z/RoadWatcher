export interface TimelineClip {
  id: string;
  mediaId: string;
  sourceInSeconds: number;
  sourceOutSeconds: number;
  reelStartSeconds: number;
  label: string;
}

const MIN_CLIP_DURATION_SECONDS = 1;

export function clipDurationSeconds(clip: TimelineClip): number {
  return Math.max(0, clip.sourceOutSeconds - clip.sourceInSeconds);
}

export function clampClipToMedia(clip: TimelineClip, mediaDurationSeconds: number): TimelineClip {
  const mediaEnd = Math.max(0, mediaDurationSeconds);
  const sourceInSeconds = clamp(clip.sourceInSeconds, 0, mediaEnd);
  const sourceOutSeconds = clamp(clip.sourceOutSeconds, sourceInSeconds, mediaEnd);

  return {
    ...clip,
    sourceInSeconds,
    sourceOutSeconds
  };
}

export function splitClipAt(clip: TimelineClip, sourceTimeSeconds: number, rightClipId: string): [TimelineClip, TimelineClip] {
  if (sourceTimeSeconds <= clip.sourceInSeconds || sourceTimeSeconds >= clip.sourceOutSeconds) {
    throw new RangeError("Split point must be inside the clip source range.");
  }

  const left: TimelineClip = {
    ...clip,
    sourceOutSeconds: sourceTimeSeconds
  };

  const right: TimelineClip = {
    ...clip,
    id: rightClipId,
    sourceInSeconds: sourceTimeSeconds,
    reelStartSeconds: clip.reelStartSeconds + clipDurationSeconds(left)
  };

  return [left, right];
}

export function moveClip(clips: TimelineClip[], clipId: string, targetIndex: number): TimelineClip[] {
  const currentIndex = clips.findIndex((clip) => clip.id === clipId);
  if (currentIndex === -1) {
    return reseatReelStarts(clips);
  }

  const next = clips.slice();
  const [clip] = next.splice(currentIndex, 1);
  next.splice(clamp(Math.trunc(targetIndex), 0, next.length), 0, clip);
  return reseatReelStarts(next);
}

export function trimClipSourceRange(
  clips: TimelineClip[],
  clipId: string,
  sourceInSeconds: number,
  sourceOutSeconds: number,
  mediaDurationSeconds: number
): TimelineClip[] {
  const mediaEnd = Math.max(MIN_CLIP_DURATION_SECONDS, mediaDurationSeconds);
  const next = clips.map((clip) => {
    if (clip.id !== clipId) {
      return clip;
    }

    const sourceIn = clamp(Math.trunc(sourceInSeconds), 0, mediaEnd - MIN_CLIP_DURATION_SECONDS);
    const sourceOut = clamp(Math.trunc(sourceOutSeconds), sourceIn + MIN_CLIP_DURATION_SECONDS, mediaEnd);
    return {
      ...clip,
      sourceInSeconds: sourceIn,
      sourceOutSeconds: sourceOut
    };
  });

  return reseatReelStarts(next);
}

export function splitClipInTimeline(
  clips: TimelineClip[],
  clipId: string,
  sourceTimeSeconds: number,
  rightClipId: string
): TimelineClip[] {
  const clipIndex = clips.findIndex((clip) => clip.id === clipId);
  if (clipIndex === -1) {
    return reseatReelStarts(clips);
  }

  const [left, right] = splitClipAt(clips[clipIndex], sourceTimeSeconds, rightClipId);
  return reseatReelStarts([...clips.slice(0, clipIndex), left, withSplitLabel(right), ...clips.slice(clipIndex + 1)]);
}

export function duplicateClip(clips: TimelineClip[], clipId: string, duplicateClipId: string): TimelineClip[] {
  const clipIndex = clips.findIndex((clip) => clip.id === clipId);
  if (clipIndex === -1) {
    return reseatReelStarts(clips);
  }

  const source = clips[clipIndex];
  const duplicate: TimelineClip = {
    ...source,
    id: duplicateClipId,
    label: `${baseClipLabel(source.label)} copy`
  };

  return reseatReelStarts([...clips.slice(0, clipIndex + 1), duplicate, ...clips.slice(clipIndex + 1)]);
}

export function removeClip(clips: TimelineClip[], clipId: string): TimelineClip[] {
  if (clips.length <= 1) {
    return reseatReelStarts(clips);
  }

  const next = clips.filter((clip) => clip.id !== clipId);
  return reseatReelStarts(next.length > 0 ? next : clips);
}

export function timelineDurationSeconds(clips: TimelineClip[]): number {
  return clips.reduce((duration, clip) => duration + clipDurationSeconds(clip), 0);
}

export function reseatReelStarts(clips: TimelineClip[]): TimelineClip[] {
  let cursor = 0;
  return clips.map((clip) => {
    const next = { ...clip, reelStartSeconds: cursor };
    cursor += clipDurationSeconds(clip);
    return next;
  });
}

function clamp(value: number, min: number, max: number): number {
  return Math.min(Math.max(value, min), max);
}

function withSplitLabel(clip: TimelineClip): TimelineClip {
  return {
    ...clip,
    label: `${baseClipLabel(clip.label)} tail`
  };
}

function baseClipLabel(label: string): string {
  return label.replace(/\s+(tail|copy)$/i, "");
}
