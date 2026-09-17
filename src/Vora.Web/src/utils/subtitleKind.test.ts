import { describe, it, expect } from 'vitest';
import { isImageSubtitleCodec, isTextSubtitleCodec, isNoSubtitle, NoSubtitle } from './subtitleKind';

// This classification decides whether picking a subtitle restarts the stream.
// Text subtitles are sideloaded as a <track> and change nothing about playback;
// image subtitles have to be burned into the video, which means a transcode and
// a reload. Getting it wrong either shows the subtitle twice or not at all.
describe('subtitle classification', () => {
    it.each(['hdmv_pgs_subtitle', 'pgssub', 'dvd_subtitle', 'vobsub', 'dvb_subtitle', 'xsub'])(
        'treats %s as an image subtitle', codec => {
            expect(isImageSubtitleCodec(codec)).toBe(true);
            expect(isTextSubtitleCodec(codec)).toBe(false);
        });

    it.each(['subrip', 'srt', 'ass', 'ssa', 'mov_text', 'webvtt'])(
        'treats %s as a text subtitle', codec => {
            expect(isTextSubtitleCodec(codec)).toBe(true);
            expect(isImageSubtitleCodec(codec)).toBe(false);
        });

    // ffprobe's casing varies by container and build, and the server lowercases
    // before comparing — the two lists have to agree on the same track.
    it.each(['PGSSUB', 'Hdmv_Pgs_Subtitle', '  vobsub  '])(
        'recognises %s regardless of casing or padding', codec => {
            expect(isImageSubtitleCodec(codec)).toBe(true);
        });

    // An unanalysed track defaults to text: a sideload that finds nothing just
    // shows no subtitle, where a wrong burn-in guess would restart the stream to
    // transcode something that never needed it.
    it.each([undefined, null, '', '   ', 'something_new'])(
        'treats %s as text', codec => {
            expect(isTextSubtitleCodec(codec)).toBe(true);
        });
});

describe('no-subtitle selection', () => {
    it.each([undefined, null, '', NoSubtitle, '00000000-0000-0000-0000-000000000000'])(
        'reads %s as no subtitle', selection => {
            expect(isNoSubtitle(selection)).toBe(true);
        });

    it('reads a real track id as a selection', () => {
        expect(isNoSubtitle('7f1c0f2e-1111-2222-3333-444455556666')).toBe(false);
    });
});
