import { beforeEach, describe, expect, it, vi } from 'vitest';
import { renderHook } from '@testing-library/react';
import { useServerPlaybackChoice } from './useServerPlaybackChoice';
import { streamingService } from '../api/Streaming/streamingService';
import { StorageKeys } from '../utils/storageKeys';

vi.mock('../api/Streaming/streamingService', () => ({
    streamingService: { previewDecision: vi.fn() },
}));

const preview = vi.mocked(streamingService.previewDecision);

const decision = (mediaPartId: string, audioTrackId: string) => ({
    mediaPartId, videoTrackId: `${mediaPartId}-video`, audioTrackId, subtitleTrackId: null,
    strategy: 'DirectPlay', videoStrategy: 'DirectPlay', audioStrategy: 'DirectPlay', targetAudioChannels: 6,
});

beforeEach(() => {
    localStorage.clear();
    preview.mockReset();
    localStorage.setItem(StorageKeys.deviceId, 'device');
});

describe('asking the server what it would play', () => {
    it('returns the version, audio track and subtitle the server picked, leaving the subtitle to the server', async () => {
        preview.mockResolvedValue({ ...decision('part-1080', 'audio-surround'), subtitleTrackId: 'english-sdh' });
        const { result } = renderHook(() => useServerPlaybackChoice('server'));

        const choice = await result.current.ask('movie');

        expect(choice).toEqual({
            mediaPartId: 'part-1080', videoTrackId: 'part-1080-video', audioTrackId: 'audio-surround', subtitleTrackId: 'english-sdh', pickReplaced: false,
        });
        expect(preview).toHaveBeenCalledWith('movie', 'device', {
            serverId: 'server', mediaPartId: undefined, videoTrackId: undefined, subtitleTrackId: undefined,
        });
    });

    it('says when the server played something other than the pick because the pick would be converted', async () => {
        preview.mockResolvedValue({ ...decision('part-1080', 'audio-ac3'), pickReplaced: true });
        const { result } = renderHook(() => useServerPlaybackChoice());

        expect((await result.current.ask('movie'))?.pickReplaced).toBe(true);
    });

    it('asks about the version the viewer chose, and a subtitle that has to be burned in', async () => {
        preview.mockResolvedValue(decision('part-4k', 'audio-stereo'));
        const { result } = renderHook(() => useServerPlaybackChoice());

        await result.current.ask('movie', { mediaPartId: 'part-4k', videoTrackId: 'video-4k', subtitleTrackId: 'pgs' });

        expect(preview).toHaveBeenCalledWith('movie', 'device', {
            serverId: undefined, mediaPartId: 'part-4k', videoTrackId: 'video-4k', subtitleTrackId: 'pgs',
        });
    });

    it('drops an answer that a newer question has replaced', async () => {
        let answerFirst: (value: ReturnType<typeof decision>) => void = () => undefined;
        preview.mockImplementationOnce(() => new Promise(resolve => { answerFirst = resolve; }));
        preview.mockResolvedValueOnce(decision('part-4k', 'audio-new'));
        const { result } = renderHook(() => useServerPlaybackChoice());

        const first = result.current.ask('movie');
        const second = await result.current.ask('movie', { mediaPartId: 'part-4k', videoTrackId: 'video-4k' });
        answerFirst(decision('part-1080', 'audio-old'));

        expect(second?.audioTrackId).toBe('audio-new');
        expect(await first).toBeNull();
    });

    it('drops an answer once the viewer has picked for themselves', async () => {
        let answer: (value: ReturnType<typeof decision>) => void = () => undefined;
        preview.mockImplementationOnce(() => new Promise(resolve => { answer = resolve; }));
        const { result } = renderHook(() => useServerPlaybackChoice());

        const pending = result.current.ask('movie');
        result.current.forget();
        answer(decision('part-1080', 'audio-surround'));

        expect(await pending).toBeNull();
    });

    it('leaves the local pick in place when the server cannot answer', async () => {
        preview.mockRejectedValue(new Error('offline'));
        const { result } = renderHook(() => useServerPlaybackChoice());

        expect(await result.current.ask('movie')).toBeNull();
    });

    it('does not ask before this device has an id', async () => {
        localStorage.clear();
        const { result } = renderHook(() => useServerPlaybackChoice());

        expect(await result.current.ask('movie')).toBeNull();
        expect(preview).not.toHaveBeenCalled();
    });
});
