const STORAGE_KEY = 'audio_quality_preference';
const CROSSFADE_KEY = 'audio_crossfade_seconds';
const EQ_KEY = 'audio_eq_preset';

export type AudioQuality = 'Auto' | 'High' | 'Medium' | 'Low' | 'Original';
export type EqPreset = 'Off' | 'BassBoost' | 'TrebleBoost' | 'Vocal' | 'Loudness';

export const audioQualityStore = {
    get: (): AudioQuality => {
        try {
            const v = localStorage.getItem(STORAGE_KEY);
            if (v === 'High' || v === 'Medium' || v === 'Low' || v === 'Original') return v;
            return 'Auto';
        } catch {
            return 'Auto';
        }
    },
    set: (value: AudioQuality): void => {
        try { localStorage.setItem(STORAGE_KEY, value); } catch { /* ignore */ }
    }
};

export const crossfadeStore = {
    get: (): number => {
        try {
            const v = parseInt(localStorage.getItem(CROSSFADE_KEY) ?? '0', 10);
            return Number.isFinite(v) ? Math.max(0, Math.min(12, v)) : 0;
        } catch { return 0; }
    },
    set: (seconds: number): void => {
        try { localStorage.setItem(CROSSFADE_KEY, String(Math.max(0, Math.min(12, seconds)))); } catch { /* ignore */ }
    }
};

export const eqPresetStore = {
    get: (): EqPreset => {
        try {
            const v = localStorage.getItem(EQ_KEY);
            if (v === 'BassBoost' || v === 'TrebleBoost' || v === 'Vocal' || v === 'Loudness') return v;
            return 'Off';
        } catch { return 'Off'; }
    },
    set: (value: EqPreset): void => {
        try { localStorage.setItem(EQ_KEY, value); } catch { /* ignore */ }
    }
};

interface EqBand { freq: number; gain: number; q: number; type: BiquadFilterType }

export const EQ_PRESETS: Record<EqPreset, EqBand[]> = {
    Off: [],
    BassBoost: [
        { freq: 60, gain: 8, q: 0.7, type: 'lowshelf' },
        { freq: 200, gain: 4, q: 0.7, type: 'peaking' }
    ],
    TrebleBoost: [
        { freq: 5000, gain: 4, q: 0.7, type: 'peaking' },
        { freq: 10000, gain: 6, q: 0.7, type: 'highshelf' }
    ],
    Vocal: [
        { freq: 200, gain: -3, q: 1.0, type: 'peaking' },
        { freq: 2500, gain: 5, q: 1.2, type: 'peaking' },
        { freq: 4000, gain: 3, q: 1.0, type: 'peaking' }
    ],
    Loudness: [
        { freq: 60, gain: 6, q: 0.7, type: 'lowshelf' },
        { freq: 8000, gain: 4, q: 0.7, type: 'highshelf' }
    ]
};
