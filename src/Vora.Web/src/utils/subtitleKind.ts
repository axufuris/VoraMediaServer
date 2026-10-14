// Where a subtitle can be rendered depends entirely on its codec.
//
//   Image (bitmap) subtitles are pictures. The browser can't draw them over a
//   video, so the server burns them into the frames — which forces a transcode
//   and means selecting one restarts the stream.
//
//   Text subtitles are cues. The server extracts them to WebVTT and the client
//   sideloads that as a <track>, so selecting one changes nothing about the
//   stream that's already playing.
//
// Mirrors BestPathDecisionManager.IsImageSubtitleCodec on the server. The two
// lists have to agree: a codec the server burns in but the client thinks is
// text would show the subtitle twice, and the reverse shows it not at all.
const IMAGE_SUBTITLE_CODECS = new Set([
    'hdmv_pgs_subtitle',
    'pgssub',
    'pgs',
    'dvd_subtitle',
    'vobsub',
    'dvb_subtitle',
    'dvbsub',
    'xsub',
]);

export function isImageSubtitleCodec(codec?: string | null): boolean {
    if (!codec) return false;
    return IMAGE_SUBTITLE_CODECS.has(codec.trim().toLowerCase());
}

// An unanalysed or unrecognised codec on a real track is treated as text. That
// is the recoverable direction: a sideload that finds nothing shows no
// subtitle, whereas wrongly assuming burn-in would restart the stream to
// transcode something that never needed it.
export function isTextSubtitleCodec(codec?: string | null): boolean {
    return !isImageSubtitleCodec(codec);
}

export const NoSubtitle = 'none';

interface SubtitleChoice {
    id: string;
    codec?: string | null;
}

// The selected subtitle's id when the server has to burn it into the picture,
// which is the only time a subtitle changes how the stream is delivered.
export function burnInSubtitleId(tracks: SubtitleChoice[] | undefined, selection?: string | null): string | undefined {
    if (isNoSubtitle(selection)) return undefined;
    const track = tracks?.find(candidate => candidate.id === selection);
    return track && isImageSubtitleCodec(track.codec) ? track.id : undefined;
}

export function isNoSubtitle(selection?: string | null): boolean {
    return !selection || selection === NoSubtitle || selection === '00000000-0000-0000-0000-000000000000';
}
