import { describe, it, expect } from 'vitest';
import {
    fileNameOf,
    formatBitrate,
    formatChannels,
    formatCodec,
    formatFileSize,
    formatInfoDuration,
    formatLanguage,
    formatResolution,
    subtitleSource,
} from './mediaInfo';

describe('media info formatting', () => {
    it.each([
        [6638, '1:50:38'],
        [191, '3:11'],
        [59.6, '1:00'],
        [3600, '1:00:00'],
    ])('formats a %s second duration as %s', (seconds, expected) => {
        expect(formatInfoDuration(seconds)).toBe(expected);
    });

    it.each([undefined, null, 0, -5, Number.NaN])('has no duration for %s', seconds => {
        expect(formatInfoDuration(seconds)).toBeNull();
    });

    it.each([
        [9_266_000_000, '8.63 GB'],
        [734_003_200, '700.00 MB'],
        [512, '512 B'],
        [3_298_534_883_328, '3.00 TB'],
    ])('formats %s bytes as %s', (bytes, expected) => {
        expect(formatFileSize(bytes)).toBe(expected);
    });

    it.each([undefined, null, 0])('has no size for %s', bytes => {
        expect(formatFileSize(bytes)).toBeNull();
    });

    it('groups a bitrate with thousands separators', () => {
        expect(formatBitrate(11167)).toBe('11,167 kbps');
    });

    it.each([undefined, null, 0])('has no bitrate for %s', kbps => {
        expect(formatBitrate(kbps)).toBeNull();
    });

    it.each([
        [1, 'Mono'],
        [2, 'Stereo'],
        [6, '5.1'],
        [8, '7.1'],
        [3, '3 channels'],
    ])('names %s channels %s', (channels, expected) => {
        expect(formatChannels(channels)).toBe(expected);
    });

    it('shows 2160p as 4K', () => {
        expect(formatResolution('2160p')).toBe('4K');
        expect(formatResolution('1080p')).toBe('1080p');
    });

    it.each([
        ['eng', 'English (eng)'],
        ['en', 'English (en)'],
        ['und', 'Undetermined'],
    ])('names the language %s', (code, expected) => {
        expect(formatLanguage(code)).toBe(expected);
    });

    it('falls back to the code for a language it cannot name', () => {
        expect(formatLanguage('zzz')).toBe('zzz');
    });

    it('upper-cases a codec', () => {
        expect(formatCodec('hevc')).toBe('HEVC');
        expect(formatCodec('')).toBeNull();
    });

    it.each([
        ['/movies1080/Man of War (2026)/Man of War (2026) [Remux-1080p].mkv', 'Man of War (2026) [Remux-1080p].mkv'],
        ['D:\\Media\\Movies\\Arrival (2016).mkv', 'Arrival (2016).mkv'],
        ['file.mkv', 'file.mkv'],
    ])('takes the file name from %s', (path, expected) => {
        expect(fileNameOf(path)).toBe(expected);
    });

    it.each([
        [{ isExternal: false, isDownloaded: false }, 'Embedded'],
        [{ isExternal: true, isDownloaded: false }, 'Sidecar file'],
        [{ isExternal: true, isDownloaded: true }, 'Downloaded'],
    ])('describes where a subtitle comes from', (track, expected) => {
        expect(subtitleSource(track)).toBe(expected);
    });
});
