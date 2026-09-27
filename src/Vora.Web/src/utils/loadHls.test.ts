import { describe, it, expect, vi, beforeEach } from 'vitest';

const mocks = vi.hoisted(() => ({
    hlsStub: { name: 'hls-stub-sentinel' }
}));

vi.mock('hls.js', () => ({ default: mocks.hlsStub }));

describe('loadHls', () => {
    beforeEach(() => {
        vi.resetModules();
    });

    it('resolves to the hls.js default export', async () => {
        const { loadHls } = await import('./loadHls');
        const Hls = await loadHls();
        expect(Hls).toBe(mocks.hlsStub);
    });

    it('memoizes the dynamic import so repeated calls return the same promise', async () => {
        const { loadHls } = await import('./loadHls');
        const first = loadHls();
        const second = loadHls();
        expect(first).toBe(second);
        const [a, b] = await Promise.all([first, second]);
        expect(a).toBe(b);
        expect(a).toBe(mocks.hlsStub);
    });

    it('sequential awaited calls also share the resolved value', async () => {
        const { loadHls } = await import('./loadHls');
        const first = await loadHls();
        const second = await loadHls();
        expect(first).toBe(second);
    });
});
