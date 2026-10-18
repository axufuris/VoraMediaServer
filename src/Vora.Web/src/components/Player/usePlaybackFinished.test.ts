import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { act, renderHook } from '@testing-library/react';
import { EndStretchSeconds, StuckAtTheEndMs, isStuckAtTheEnd, usePlaybackFinished } from './usePlaybackFinished';

interface FakeVideo {
    element: HTMLVideoElement;
    set: (state: { currentTime?: number; duration?: number; paused?: boolean; seeking?: boolean }) => void;
}

function fakeVideo(duration: number): FakeVideo {
    const element = document.createElement('video');
    const state = { currentTime: 0, duration, paused: false, seeking: false };
    Object.defineProperty(element, 'currentTime', { get: () => state.currentTime, configurable: true });
    Object.defineProperty(element, 'duration', { get: () => state.duration, configurable: true });
    Object.defineProperty(element, 'paused', { get: () => state.paused, configurable: true });
    Object.defineProperty(element, 'seeking', { get: () => state.seeking, configurable: true });
    return { element, set: next => Object.assign(state, next) };
}

function renderFinished(video: FakeVideo, playbackKey: string | undefined = 'session-1') {
    const ref = { current: video.element };
    return renderHook(({ key }: { key: string | undefined }) => usePlaybackFinished(ref, key), { initialProps: { key: playbackKey } });
}

describe('isStuckAtTheEnd', () => {
    it('is stuck only right at the end, and only after a few seconds without moving', () => {
        expect(isStuckAtTheEnd(2700 - EndStretchSeconds, 2700, StuckAtTheEndMs)).toBe(true);
        expect(isStuckAtTheEnd(2700 - EndStretchSeconds - 1, 2700, StuckAtTheEndMs)).toBe(false);
        expect(isStuckAtTheEnd(2699, 2700, StuckAtTheEndMs - 1)).toBe(false);
        expect(isStuckAtTheEnd(0, Number.NaN, StuckAtTheEndMs)).toBe(false);
    });
});

describe('usePlaybackFinished', () => {
    beforeEach(() => vi.useFakeTimers());
    afterEach(() => vi.useRealTimers());

    it('finishes when the video ends', () => {
        const video = fakeVideo(100);
        const { result } = renderFinished(video);

        act(() => { video.element.dispatchEvent(new Event('ended')); });

        expect(result.current.finished).toBe(true);
    });

    it('finishes when playback stalls on the last frame because the stream never delivered its end', () => {
        const video = fakeVideo(100);
        video.set({ currentTime: 97.6 });
        const { result } = renderFinished(video);

        act(() => { vi.advanceTimersByTime(StuckAtTheEndMs - 600); });
        expect(result.current.finished).toBe(false);

        act(() => { vi.advanceTimersByTime(1200); });
        expect(result.current.finished).toBe(true);
    });

    it('does not finish a video that is buffering in the middle, or paused near the end', () => {
        const video = fakeVideo(100);
        video.set({ currentTime: 40 });
        const { result } = renderFinished(video);

        act(() => { vi.advanceTimersByTime(StuckAtTheEndMs * 3); });
        expect(result.current.finished).toBe(false);

        video.set({ currentTime: 98, paused: true });
        act(() => { vi.advanceTimersByTime(StuckAtTheEndMs * 3); });
        expect(result.current.finished).toBe(false);
    });

    it('finishes when the server says the rest is past the end', () => {
        const { result } = renderFinished(fakeVideo(100));

        act(() => { result.current.markFinished(); });

        expect(result.current.finished).toBe(true);
    });

    it('starts over when the viewer seeks back out of the last stretch', () => {
        const video = fakeVideo(100);
        const { result } = renderFinished(video);
        act(() => { result.current.markFinished(); });

        video.set({ currentTime: 30 });
        act(() => { video.element.dispatchEvent(new Event('seeking')); });

        expect(result.current.finished).toBe(false);
    });

    it('stays finished when Skip Credits jumps to just before the end', () => {
        const video = fakeVideo(100);
        const { result } = renderFinished(video);
        act(() => { result.current.markFinished(); });

        video.set({ currentTime: 98 });
        act(() => { video.element.dispatchEvent(new Event('seeking')); });

        expect(result.current.finished).toBe(true);
    });

    it('is not finished for the next item', () => {
        const video = fakeVideo(100);
        const { result, rerender } = renderFinished(video);
        act(() => { result.current.markFinished(); });

        rerender({ key: 'session-2' });

        expect(result.current.finished).toBe(false);
    });
});
