export const SYNTH_BANDS = 48;

const MIN_HZ = 40;
const MAX_HZ = 16000;
const FALL_PER_FRAME = 0.045;
const PEAK_FALL_PER_FRAME = 0.012;

export function bandEdges(binCount: number, sampleRate: number, bands = SYNTH_BANDS): number[] {
    const nyquist = sampleRate / 2;
    const top = Math.min(MAX_HZ, nyquist);
    const edges: number[] = [];
    for (let i = 0; i <= bands; i++) {
        const hz = MIN_HZ * Math.pow(top / MIN_HZ, i / bands);
        edges.push(Math.min(binCount - 1, Math.max(0, Math.round((hz / nyquist) * binCount))));
    }
    return edges;
}

export function bandLevels(data: ArrayLike<number>, edges: number[]): number[] {
    const levels: number[] = [];
    for (let b = 0; b < edges.length - 1; b++) {
        const start = edges[b];
        const end = Math.max(start + 1, edges[b + 1]);
        let peak = 0;
        for (let i = start; i < end && i < data.length; i++) peak = Math.max(peak, data[i]);
        levels.push(peak / 255);
    }
    return levels;
}

export function settle(current: number[], target: number[]): number[] {
    return target.map((value, i) => Math.max(value, (current[i] ?? 0) - FALL_PER_FRAME));
}

export function fallPeaks(peaks: number[], levels: number[]): number[] {
    return levels.map((level, i) => Math.max(level, (peaks[i] ?? 0) - PEAK_FALL_PER_FRAME));
}

export function idleLevels(bands: number, seconds: number): number[] {
    return Array.from({ length: bands }, (_, i) => 0.05 + 0.035 * (1 + Math.sin(seconds * 1.4 + i * 0.45)) / 2);
}
