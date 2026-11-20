import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { renderHook } from '@testing-library/react';
import type { PlayableMedia } from './usePlayer';

const saveResume = vi.hoisted(() => vi.fn());
vi.mock('../api/Music/musicService', () => ({ musicService: { saveResume } }));

import { RESUME_SAVE_DELAY_MS, useMusicResumeSaver } from './useMusicResumeSaver';

const song = (id: string, extra: Partial<PlayableMedia> = {}): PlayableMedia => ({
    id,
    title: id,
    streamUrl: '',
    serverId: 's1',
    playbackContextType: 'Music',
    musicSource: { type: 'Playlist', id: 'p1' },
    ...extra,
});

const queue = [song('a'), song('b'), song('c')];

interface Props {
    queue: PlayableMedia[];
    queueIndex: number;
    isPlaying: boolean;
    isRadio: boolean;
}

const start = (props: Partial<Props> = {}) => renderHook((current: Props) => useMusicResumeSaver(current), {
    initialProps: { queue, queueIndex: 0, isPlaying: true, isRadio: false, ...props },
});

const settle = () => vi.advanceTimersByTime(RESUME_SAVE_DELAY_MS);

describe('saving where a music queue stopped', () => {
    beforeEach(() => {
        vi.useFakeTimers();
        saveResume.mockReset().mockResolvedValue(undefined);
    });

    afterEach(() => {
        vi.useRealTimers();
    });

    it('saves the queue, the current song and the source a second after the queue starts, not before', () => {
        start();

        vi.advanceTimersByTime(RESUME_SAVE_DELAY_MS - 1);
        expect(saveResume).not.toHaveBeenCalled();

        vi.advanceTimersByTime(1);
        expect(saveResume).toHaveBeenCalledTimes(1);
        expect(saveResume).toHaveBeenCalledWith({ source: 'Playlist', sourceId: 'p1', trackIds: ['a', 'b', 'c'], index: 0 }, 's1');
    });

    it('sends one save for a burst of skips, with where the burst ended', () => {
        const { rerender } = start();
        vi.advanceTimersByTime(RESUME_SAVE_DELAY_MS / 2);
        rerender({ queue, queueIndex: 1, isPlaying: true, isRadio: false });
        vi.advanceTimersByTime(RESUME_SAVE_DELAY_MS / 2);
        rerender({ queue, queueIndex: 2, isPlaying: true, isRadio: false });

        settle();

        expect(saveResume).toHaveBeenCalledTimes(1);
        expect(saveResume).toHaveBeenCalledWith(expect.objectContaining({ index: 2 }), 's1');
    });

    it('saves again for the next song', () => {
        const { rerender } = start();
        settle();
        rerender({ queue, queueIndex: 1, isPlaying: true, isRadio: false });
        settle();

        expect(saveResume).toHaveBeenCalledTimes(2);
        expect(saveResume).toHaveBeenLastCalledWith(expect.objectContaining({ index: 1 }), 's1');
    });

    it('does not send the same state twice, as when playback pauses and resumes on one song', () => {
        const { rerender } = start();
        settle();
        rerender({ queue, queueIndex: 0, isPlaying: false, isRadio: false });
        settle();
        rerender({ queue, queueIndex: 0, isPlaying: true, isRadio: false });
        settle();

        expect(saveResume).toHaveBeenCalledTimes(1);
    });

    it('saves a queue edited while paused', () => {
        const { rerender } = start({ isPlaying: false });
        settle();
        rerender({ queue: [...queue, song('d')], queueIndex: 0, isPlaying: false, isRadio: false });
        settle();

        expect(saveResume).toHaveBeenCalledTimes(2);
        expect(saveResume).toHaveBeenLastCalledWith(expect.objectContaining({ trackIds: ['a', 'b', 'c', 'd'] }), 's1');
    });

    it('keeps what a queue with no source is, and where it is for the server it plays from', () => {
        start({ queue: [song('a', { musicSource: undefined, serverId: 's2' })] });
        settle();

        expect(saveResume).toHaveBeenCalledWith({ source: 'None', sourceId: undefined, trackIds: ['a'], index: 0 }, 's2');
    });

    it('sends what it was about to when the player is closed, without waiting out the second', () => {
        const { rerender } = start();

        rerender({ queue: [], queueIndex: 0, isPlaying: false, isRadio: false });

        expect(saveResume).toHaveBeenCalledTimes(1);
        expect(saveResume).toHaveBeenCalledWith(expect.objectContaining({ trackIds: ['a', 'b', 'c'], index: 0 }), 's1');
        settle();
        expect(saveResume).toHaveBeenCalledTimes(1);
    });

    it('sends what it was about to when the page goes away', () => {
        start();

        window.dispatchEvent(new Event('pagehide'));

        expect(saveResume).toHaveBeenCalledTimes(1);
    });

    it('sends what it was about to when it unmounts', () => {
        const { unmount } = start();

        unmount();

        expect(saveResume).toHaveBeenCalledTimes(1);
    });

    it('leaves radio alone, and anything that is not music', () => {
        start({ isRadio: true });
        start({ queue: [{ id: 'e1', title: 'Pilot', streamUrl: '', playbackContextType: 'Video' }] });
        settle();

        expect(saveResume).not.toHaveBeenCalled();
    });

    describe('when the queue finishes by itself', () => {
        it('clears the point of its source, drops what was about to be saved and does not save it back', () => {
            const { result, rerender } = start();

            result.current.clear(queue[2]);
            rerender({ queue, queueIndex: 2, isPlaying: false, isRadio: false });
            settle();

            expect(saveResume).toHaveBeenCalledTimes(1);
            expect(saveResume).toHaveBeenCalledWith({ source: 'Playlist', sourceId: 'p1', trackIds: [], index: 0 }, 's1');
        });

        it('clears a queue with no source under none', () => {
            const { result } = start({ queue: [song('a', { musicSource: undefined })] });

            result.current.clear(song('a', { musicSource: undefined }));

            expect(saveResume).toHaveBeenCalledWith({ source: 'None', sourceId: undefined, trackIds: [], index: 0 }, 's1');
        });

        it('saves the queue again once someone plays it again', () => {
            const { result, rerender } = start();
            result.current.clear(queue[2]);
            rerender({ queue, queueIndex: 2, isPlaying: false, isRadio: false });
            settle();
            saveResume.mockClear();

            rerender({ queue, queueIndex: 2, isPlaying: true, isRadio: false });
            settle();

            expect(saveResume).toHaveBeenCalledTimes(1);
            expect(saveResume).toHaveBeenCalledWith(expect.objectContaining({ index: 2, trackIds: ['a', 'b', 'c'] }), 's1');
        });
    });
});
