import { beforeEach, describe, expect, it, vi } from 'vitest';
import {
    CONNECTION_SPEED_TTL_MS,
    FIRST_SAMPLE_KB,
    LARGEST_SAMPLE_KB,
    forgetConnectionSpeeds,
    knownConnectionKbps,
    measureConnection,
    type SampleTiming,
} from './connectionSpeed';

// A link of the given speed: at 50,000 kbps the 512 KB first sample takes 84 ms.
const linkOf = (kbps: number) => vi.fn(async (kilobytes: number): Promise<SampleTiming> => ({
    bytes: kilobytes * 1024,
    milliseconds: (kilobytes * 1024 * 8) / kbps,
}));

beforeEach(() => forgetConnectionSpeeds());

describe('measuring the connection to a server', () => {
    it('follows a quick first sample with one sized to take about a second', async () => {
        const download = linkOf(50_000);

        const kbps = await measureConnection('home', download);

        expect(kbps).toBe(50_000);
        expect(download).toHaveBeenNthCalledWith(1, FIRST_SAMPLE_KB);
        expect(download).toHaveBeenNthCalledWith(2, 6250);
    });

    it('never asks for more than the largest sample, however fast the link', async () => {
        const download = linkOf(2_000_000);

        await measureConnection('home', download);

        expect(download).toHaveBeenLastCalledWith(LARGEST_SAMPLE_KB);
    });

    it('stops after the first sample on a slow link', async () => {
        const download = linkOf(2_000);

        expect(await measureConnection('home', download)).toBe(2_000);
        expect(download).toHaveBeenCalledTimes(1);
    });

    it('remembers the speed per server for an hour, then measures again', async () => {
        let now = 1_000;
        const download = linkOf(50_000);
        await measureConnection('home', download, () => now);

        expect(knownConnectionKbps('home', now + CONNECTION_SPEED_TTL_MS - 1)).toBe(50_000);
        expect(knownConnectionKbps('cabin', now)).toBe(0);
        expect(await measureConnection('home', download, () => now)).toBe(50_000);
        expect(download).toHaveBeenCalledTimes(2);

        now += CONNECTION_SPEED_TTL_MS;
        expect(knownConnectionKbps('home', now)).toBe(0);
        await measureConnection('home', download, () => now);
        expect(download).toHaveBeenCalledTimes(4);
    });

    it('shares one measurement between callers asking at the same time', async () => {
        const download = linkOf(50_000);

        await Promise.all([measureConnection('home', download), measureConnection('home', download)]);

        expect(download).toHaveBeenCalledTimes(2);
    });

    it('knows nothing when the sample could not be downloaded', async () => {
        const download = vi.fn(async (): Promise<SampleTiming> => { throw new Error('offline'); });

        expect(await measureConnection('home', download)).toBe(0);
        expect(knownConnectionKbps('home')).toBe(0);
    });
});
