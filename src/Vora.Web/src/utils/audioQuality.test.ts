import { describe, it, expect, beforeEach } from 'vitest';
import { audioQualityStore, crossfadeStore, eqPresetStore, EQ_PRESETS } from './audioQuality';

describe('audioQualityStore', () => {
    beforeEach(() => localStorage.clear());

    it('defaults to Auto when nothing stored', () => {
        expect(audioQualityStore.get()).toBe('Auto');
    });

    it('returns Auto for unrecognized stored value', () => {
        localStorage.setItem('audio_quality_preference', 'Ludicrous');
        expect(audioQualityStore.get()).toBe('Auto');
    });

    it.each(['High', 'Medium', 'Low', 'Original'] as const)('round-trips %s', (q) => {
        audioQualityStore.set(q);
        expect(audioQualityStore.get()).toBe(q);
    });
});

describe('crossfadeStore', () => {
    beforeEach(() => localStorage.clear());

    it('defaults to 0 when nothing stored', () => {
        expect(crossfadeStore.get()).toBe(0);
    });

    it('clamps stored values to 0-12 range', () => {
        crossfadeStore.set(999);
        expect(crossfadeStore.get()).toBe(12);

        crossfadeStore.set(-5);
        expect(crossfadeStore.get()).toBe(0);
    });

    it('round-trips a valid value', () => {
        crossfadeStore.set(7);
        expect(crossfadeStore.get()).toBe(7);
    });

    it('returns 0 for malformed stored value', () => {
        localStorage.setItem('audio_crossfade_seconds', 'not a number');
        expect(crossfadeStore.get()).toBe(0);
    });
});

describe('eqPresetStore', () => {
    beforeEach(() => localStorage.clear());

    it('defaults to Off when nothing stored', () => {
        expect(eqPresetStore.get()).toBe('Off');
    });

    it.each(['BassBoost', 'TrebleBoost', 'Vocal', 'Loudness'] as const)('round-trips %s', (p) => {
        eqPresetStore.set(p);
        expect(eqPresetStore.get()).toBe(p);
    });

    it('returns Off for unrecognized stored value', () => {
        localStorage.setItem('audio_eq_preset', 'Surround');
        expect(eqPresetStore.get()).toBe('Off');
    });
});

describe('EQ_PRESETS', () => {
    it('Off preset has no bands', () => {
        expect(EQ_PRESETS.Off).toEqual([]);
    });

    it('each non-Off preset has at least one band', () => {
        for (const preset of ['BassBoost', 'TrebleBoost', 'Vocal', 'Loudness'] as const) {
            expect(EQ_PRESETS[preset].length).toBeGreaterThan(0);
        }
    });

    it('every band has freq, gain, q, type', () => {
        for (const preset of Object.values(EQ_PRESETS)) {
            for (const band of preset) {
                expect(band.freq).toBeGreaterThan(0);
                expect(typeof band.gain).toBe('number');
                expect(band.q).toBeGreaterThan(0);
                expect(['lowshelf', 'highshelf', 'peaking']).toContain(band.type);
            }
        }
    });
});
