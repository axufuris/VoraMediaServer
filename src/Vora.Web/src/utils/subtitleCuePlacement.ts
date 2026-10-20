import type { PictureLayout, Size } from './pictureLayout';

export const SubtitleCueLine = -3;

export const SubtitleLineShare = 0.0575;

export interface CueArea {
    lift: number;
    size: number;
}

export interface PlaceableCue {
    line: number | 'auto';
    snapToLines: boolean;
    size: number;
}

export interface MovedCue {
    cue: PlaceableCue;
    size: number;
}

export const WholeVideo: CueArea = { lift: 0, size: 100 };

export function shouldReposition(cue: { line: number | 'auto' }): boolean {
    return cue.line === 'auto';
}

export function textCues(cues: TextTrackCueList | null): VTTCue[] {
    if (!cues || typeof VTTCue === 'undefined') return [];
    return Array.from(cues).filter((cue): cue is VTTCue => cue instanceof VTTCue);
}

export function cuesToMove(cues: readonly PlaceableCue[]): MovedCue[] {
    return cues.filter(shouldReposition).map(cue => ({ cue, size: cue.size }));
}

export function cueAreaOf(layout: PictureLayout | null, viewport: Size): CueArea {
    if (!layout || layout.width <= 0 || layout.height <= 0) return WholeVideo;

    const lineHeight = SubtitleLineShare * Math.min(layout.width, layout.height);
    const hiddenBelow = Math.max(0, layout.top + layout.height - viewport.height);
    const centre = layout.width / 2;
    const visibleLeft = Math.max(0, -layout.left);
    const visibleRight = Math.min(layout.width, viewport.width - layout.left);
    const halfWidth = Math.max(0, Math.min(centre - visibleLeft, visibleRight - centre));

    return {
        lift: hiddenBelow / lineHeight,
        size: Math.min(100, (200 * halfWidth) / layout.width),
    };
}

export function placeCues(cues: readonly MovedCue[], area: CueArea = WholeVideo): void {
    for (const { cue, size } of cues) {
        cue.snapToLines = true;
        cue.line = SubtitleCueLine - area.lift;
        cue.size = Math.min(size, area.size);
    }
}
