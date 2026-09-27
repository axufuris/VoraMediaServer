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

interface SubtitleTrackLike {
    id: string;
    language?: string;
    title?: string;
}

// "CC Off" when nothing is picked, as Plex shows it: that there are subtitles
// and none are on is worth knowing. Nothing at all when the file has none.
export function subtitleChipLabel(tracks: SubtitleTrackLike[] | undefined, selectedId?: string | null): string | null {
    if (!tracks?.length) return null;
    const picked = tracks.find(t => t.id === selectedId);
    if (!picked) return 'CC Off';
    return `CC ${picked.language?.trim() || picked.title?.trim() || 'On'}`;
}

// The one facts line under the title: year · runtime · genres. The content
// rating follows it as a chip, so it reads as a certificate, not another fact.
export function heroFactsLine({ year, runtime, genres }: { year?: number | null; runtime?: string | null; genres?: string[] }): string {
    return [
        year ? String(year) : null,
        runtime || null,
        genres && genres.length > 0 ? genres.join(', ') : null,
    ].filter(Boolean).join(' · ');
}
