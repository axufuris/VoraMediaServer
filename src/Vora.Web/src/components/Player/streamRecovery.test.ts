import { describe, expect, it } from 'vitest';
import { recoversStream, STREAM_RECOVERY_INTERVAL_MS } from './streamRecovery';

describe('asking for a stream again', () => {
    it('recovers a stream that has played and has not been recovered before', () => {
        expect(recoversStream(false, Number.POSITIVE_INFINITY)).toBe(true);
    });

    it('never recovers a stream that never played, so its error shows', () => {
        expect(recoversStream(true, Number.POSITIVE_INFINITY)).toBe(false);
    });

    it('recovers at most once a minute', () => {
        expect(STREAM_RECOVERY_INTERVAL_MS).toBe(60_000);
        expect(recoversStream(false, 59_999)).toBe(false);
        expect(recoversStream(false, 60_000)).toBe(true);
    });
});
