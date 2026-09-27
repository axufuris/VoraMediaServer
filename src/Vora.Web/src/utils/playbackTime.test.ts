import { describe, it, expect } from 'vitest';
import { formatPlaybackTime } from './playbackTime';

describe('playback time', () => {
    it.each([
        [0, '0:00'],
        [5, '0:05'],
        [65, '1:05'],
        [191, '3:11'],
        [599.9, '9:59'],
    ])('formats %s seconds as %s', (seconds, expected) => {
        expect(formatPlaybackTime(seconds)).toBe(expected);
    });

    // A podcast past the hour mark must not read as "65:00".
    it.each([
        [3600, '1:00:00'],
        [3900, '1:05:00'],
        [3661, '1:01:01'],
        [36000, '10:00:00'],
    ])('includes hours for %s seconds', (seconds, expected) => {
        expect(formatPlaybackTime(seconds)).toBe(expected);
    });

    // Before metadata loads the duration is NaN or Infinity.
    it.each([Number.NaN, Number.POSITIVE_INFINITY, -1])('shows zero for %s', seconds => {
        expect(formatPlaybackTime(seconds)).toBe('0:00');
    });
});
