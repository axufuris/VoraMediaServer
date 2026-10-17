// How fast this browser can pull from a server, measured by downloading a
// sample (GET /streaming/bandwidth-sample) and kept in memory per server. The
// first sample is small; when it comes back quickly a second one is sized to
// take about a second, so a fast connection is not judged by its slow start.
// Measured when the client opens, never while a stream is starting, so the two
// do not compete.

export const CONNECTION_SPEED_TTL_MS = 60 * 60 * 1000;
export const FIRST_SAMPLE_KB = 512;
export const LARGEST_SAMPLE_KB = 16 * 1024;
const TARGET_MS = 1000;

export interface SampleTiming {
    bytes: number;
    milliseconds: number;
}

export type DownloadSample = (kilobytes: number) => Promise<SampleTiming>;

const measured = new Map<string, { kbps: number; at: number }>();
const running = new Map<string, Promise<number>>();

const keyOf = (serverId?: string) => serverId ?? 'local';

const rateOf = (sample: SampleTiming) =>
    sample.milliseconds > 0 && sample.bytes > 0 ? Math.round((sample.bytes * 8) / sample.milliseconds) : 0;

export function knownConnectionKbps(serverId?: string, now: number = Date.now()): number {
    const entry = measured.get(keyOf(serverId));
    return entry && now - entry.at < CONNECTION_SPEED_TTL_MS ? entry.kbps : 0;
}

export function forgetConnectionSpeeds() {
    measured.clear();
    running.clear();
}

export function measureConnection(serverId: string | undefined, download: DownloadSample, now: () => number = Date.now): Promise<number> {
    const key = keyOf(serverId);
    const known = knownConnectionKbps(serverId, now());
    if (known > 0) return Promise.resolve(known);
    const existing = running.get(key);
    if (existing) return existing;

    const run = (async () => {
        try {
            const first = await download(FIRST_SAMPLE_KB);
            let kbps = rateOf(first);
            if (kbps > 0 && first.milliseconds < TARGET_MS) {
                const kilobytes = Math.min(LARGEST_SAMPLE_KB, Math.max(FIRST_SAMPLE_KB, Math.round((kbps / 8) * (TARGET_MS / 1000))));
                const second = rateOf(await download(kilobytes));
                if (second > 0) kbps = second;
            }
            if (kbps > 0) measured.set(key, { kbps, at: now() });
            return kbps;
        } catch {
            return 0;
        } finally {
            running.delete(key);
        }
    })();
    running.set(key, run);
    return run;
}
