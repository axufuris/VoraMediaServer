// Music has one expanded view: NowPlayingFullscreen. The inline player stays a
// bar underneath it. LiveRadioPlayer also carries an older expanded layout for
// radio and podcasts, and letting music reach it gave two different "full"
// players depending on which mini-bar control was pressed — one without lyrics,
// queue or audio settings.
export interface NowPlayingCandidate {
    playbackContextType?: string;
}

export function usesNowPlayingScreen(media: NowPlayingCandidate | null | undefined): boolean {
    return media?.playbackContextType === 'Music';
}
