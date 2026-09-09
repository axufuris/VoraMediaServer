// Where a sideloaded cue sits on screen.
//
// The browser positions cues against the <video> ELEMENT's box, not against the
// picture inside it. The player's element fills the viewport with
// `object-contain`, so on anything that isn't exactly the viewport's aspect —
// and on the very common case of a scope film hard-matted into a 16:9 frame —
// the default bottom placement lands the text in the black bar under the
// picture, on top of the transport controls.
//
// A negative `line` counts lines up from the bottom of the element, which lifts
// the text clear of the controls and back over the picture.
export const SubtitleCueLine = -3;

// Cues that carry their own position are positioned for a reason — a caption
// placed at the top to avoid burned-in text, say — and must be left alone.
export function shouldReposition(cue: { line: number | 'auto' }): boolean {
    return cue.line === 'auto';
}

export function placeCues(cues: TextTrackCueList | null): void {
    if (!cues) return;

    for (let i = 0; i < cues.length; i++) {
        const cue = cues[i] as VTTCue;
        if (!shouldReposition(cue)) continue;

        cue.snapToLines = true;
        cue.line = SubtitleCueLine;
    }
}
