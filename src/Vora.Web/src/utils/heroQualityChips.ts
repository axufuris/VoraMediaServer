interface AudioTrackLike {
    id: string;
    codec?: string;
    channels?: number;
    isDefault?: boolean;
}

// The audio chip on a details page has to name the track that will actually
// play, which is the one selected in Quality & tracks. Naming the "best" track
// on the version instead said TRUEHD 8ch over a file that was about to play
// AAC 2ch.
export function pickChipAudioTrack<T extends AudioTrackLike>(tracks: T[] | undefined, selectedAudioId?: string | null): T | undefined {
    if (!tracks?.length) return undefined;

    if (selectedAudioId) {
        const selected = tracks.find(t => t.id === selectedAudioId);
        if (selected) return selected;
    }

    return tracks.find(t => t.isDefault) ?? tracks[0];
}

export function audioChipLabel(track?: AudioTrackLike): string | null {
    if (!track?.codec) return null;
    return `${track.codec.toUpperCase()}${track.channels ? ` ${track.channels}ch` : ''}`;
}

export function resolutionChipLabel(resolution?: string | null): string | null {
    if (!resolution) return null;
    return resolution === '2160p' ? '4K' : resolution;
}
