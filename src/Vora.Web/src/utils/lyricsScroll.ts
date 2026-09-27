// Where the active synced line sits in the lyrics panel, as a fraction of the
// panel's height. A third of the way down keeps the upcoming lines in view.
//
// The scroll position is clamped at the top, so before the first line starts
// the lyrics begin at the top of the panel. The panel used to pad the list by
// 35vh top and bottom and centre the active line, and the auto-scroll only
// started once a line was active — so for a song with a long intro the viewer
// stared at a third of a screen of empty space until the singing began.
export const LyricsAnchor = 1 / 3;

export function lyricsScrollTop(lineTop: number, lineHeight: number, viewportHeight: number, anchor = LyricsAnchor): number {
    return Math.max(0, Math.round(lineTop + lineHeight / 2 - viewportHeight * anchor));
}
