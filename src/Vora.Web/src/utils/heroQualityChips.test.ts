import { describe, it, expect } from 'vitest';
import { audioChipLabel, pickChipAudioTrack, resolutionChipLabel } from './heroQualityChips';

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
        expect(audioChipLabel(track)).toBe('AAC 2ch');
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
    it('reads codec then channel count', () => {
        expect(audioChipLabel({ id: 'x', codec: 'truehd', channels: 8 })).toBe('TRUEHD 8ch');
    });

    it('drops the channel count when it is unknown', () => {
        expect(audioChipLabel({ id: 'x', codec: 'flac' })).toBe('FLAC');
    });

    it('shows nothing without a codec', () => {
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
