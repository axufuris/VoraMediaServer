import { describe, it, expect } from 'vitest';
import { audioChipLabel, heroFactsLine, pickChipAudioTrack, resolutionChipLabel, subtitleChipLabel } from './heroQualityChips';

// The tracks on the version that produced the bug: a TrueHD 8.1 track that has
// to be transcoded, and the AAC stereo track the client actually direct-plays.
const tracks = [
    { id: 'truehd', codec: 'truehd', channels: 8 },
    { id: 'ac3', codec: 'ac3', channels: 6, isDefault: true },
    { id: 'aac-2', codec: 'aac', channels: 2 },
    { id: 'aac-1', codec: 'aac', channels: 1 },
];

describe('pickChipAudioTrack', () => {
    it('names the selected track, not the richest one on the file', () => {
        const track = pickChipAudioTrack(tracks, 'aac-2');

        expect(track?.id).toBe('aac-2');
        expect(audioChipLabel(track)).toBe('AAC 2.0');
    });

    it('falls back to the default track before any selection is made', () => {
        expect(pickChipAudioTrack(tracks, null)?.id).toBe('ac3');
        expect(pickChipAudioTrack(tracks, undefined)?.id).toBe('ac3');
    });

    it('falls back to the default when the selection belongs to another version', () => {
        expect(pickChipAudioTrack(tracks, 'a-track-from-the-4k-file')?.id).toBe('ac3');
    });

    it('falls back to the first track when nothing is marked default', () => {
        expect(pickChipAudioTrack([{ id: 'only', codec: 'eac3', channels: 6 }], null)?.id).toBe('only');
    });

    it('has nothing to say about a part with no audio', () => {
        expect(pickChipAudioTrack([], 'aac-2')).toBeUndefined();
        expect(pickChipAudioTrack(undefined, 'aac-2')).toBeUndefined();
    });
});

describe('audioChipLabel', () => {
    it('reads codec then channel layout', () => {
        expect(audioChipLabel({ id: 'x', codec: 'truehd', channels: 8 })).toBe('TRUEHD 7.1');
    });

    // Issue #132: a film with English, Tamil, Telugu and Hindi dubs said only
    // "EAC3 6ch", not which of the four would play.
    it('names the language first, the way the Audio menu does', () => {
        expect(audioChipLabel({ id: 'x', codec: 'eac3', channels: 6, language: 'Tel' })).toBe('Telugu · EAC3 5.1');
        expect(audioChipLabel({ id: 'x', codec: 'eac3', channels: 6, language: 'English' })).toBe('English · EAC3 5.1');
    });

    it('leaves the language out when the file does not set one', () => {
        expect(audioChipLabel({ id: 'x', codec: 'ac3', channels: 6 })).toBe('AC3 5.1');
        expect(audioChipLabel({ id: 'x', codec: 'ac3', channels: 6, language: 'Und' })).toBe('AC3 5.1');
    });

    it('drops the channel layout when it is unknown', () => {
        expect(audioChipLabel({ id: 'x', codec: 'flac' })).toBe('FLAC');
    });

    it('shows the language alone when the codec is unknown', () => {
        expect(audioChipLabel({ id: 'x', channels: 6, language: 'Jpn' })).toBe('Japanese');
    });

    it('shows nothing when there is neither a codec nor a language', () => {
        expect(audioChipLabel({ id: 'x', channels: 6 })).toBeNull();
        expect(audioChipLabel(undefined)).toBeNull();
    });
});

describe('resolutionChipLabel', () => {
    it('calls 2160p what everyone else calls it', () => {
        expect(resolutionChipLabel('2160p')).toBe('4K');
    });

    it('passes other resolutions through', () => {
        expect(resolutionChipLabel('1080p')).toBe('1080p');
        expect(resolutionChipLabel(null)).toBeNull();
        expect(resolutionChipLabel(undefined)).toBeNull();
    });
});

describe('subtitleChipLabel', () => {
    const tracks = [{ id: 's1', language: 'English' }, { id: 's2', title: 'Commentary' }, { id: 's3' }];

    it('says nothing when the file has no subtitles', () => {
        expect(subtitleChipLabel([], 'none')).toBeNull();
        expect(subtitleChipLabel(undefined, null)).toBeNull();
    });

    it('reads Off when none is picked', () => {
        expect(subtitleChipLabel(tracks, 'none')).toBe('CC Off');
    });

    it('names the picked track by language, then title', () => {
        expect(subtitleChipLabel(tracks, 's1')).toBe('CC English');
        expect(subtitleChipLabel(tracks, 's2')).toBe('CC Commentary');
        expect(subtitleChipLabel(tracks, 's3')).toBe('CC On');
    });

    it('names a language stored as a code, as a sidecar file name gives it', () => {
        expect(subtitleChipLabel([{ id: 'srt', language: 'en' }], 'srt')).toBe('CC English');
        expect(subtitleChipLabel([{ id: 'pgs', language: 'Jpn' }], 'pgs')).toBe('CC Japanese');
    });

    it('falls back to the title when the language is undetermined', () => {
        expect(subtitleChipLabel([{ id: 'u', language: 'und', title: 'Signs' }], 'u')).toBe('CC Signs');
    });
});

describe('heroFactsLine', () => {
    it('puts the year first, then runtime, then genres', () => {
        expect(heroFactsLine({ year: 2026, runtime: '1 hr 40 min', genres: ['Comedy', 'Crime'] })).toBe('2026 · 1 hr 40 min · Comedy, Crime');
    });

    it('skips what is missing', () => {
        expect(heroFactsLine({ year: null, runtime: '45 min', genres: [] })).toBe('45 min');
        expect(heroFactsLine({})).toBe('');
    });
});
