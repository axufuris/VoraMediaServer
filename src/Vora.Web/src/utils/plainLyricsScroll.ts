// Where a plain (unsynced) lyric panel should sit for a given point in the song.
//
// Unsynced lyrics carry no timings, so there is no line to follow. The next best
// thing is to travel through the text at the song's own pace: the top of the
// lyric at the start, the bottom as it finishes. The viewer never scrolls this
// panel themselves, so the position is purely a function of progress.
export function plainLyricsScrollTop(currentTime: number, duration: number, scrollHeight: number, clientHeight: number): number {
    const overflow = scrollHeight - clientHeight;
    if (!isFinite(overflow) || overflow <= 0) return 0;
    if (!isFinite(duration) || duration <= 0) return 0;

    const progress = Math.min(1, Math.max(0, currentTime / duration));
    return Math.round(overflow * progress);
}
