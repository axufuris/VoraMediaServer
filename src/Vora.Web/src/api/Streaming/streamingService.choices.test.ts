import { beforeEach, describe, expect, it, vi } from 'vitest';
import { streamingService } from './streamingService';
import { apiClient } from '../client';

vi.mock('../client', () => ({
    apiClient: { get: vi.fn(), put: vi.fn() },
}));

const get = vi.mocked(apiClient.get);
const put = vi.mocked(apiClient.put);
const remembered = { remembersVersion: false, remembersAudio: true, subtitle: 'Off' };

beforeEach(() => {
    get.mockReset();
    put.mockReset();
    get.mockResolvedValue({ data: remembered });
    put.mockResolvedValue({ data: remembered });
});

describe('remembered playback choices', () => {
    it('reads what this profile picked for a title on this device', async () => {
        expect(await streamingService.getRememberedChoice('episode', 'server')).toEqual(remembered);
        expect(get).toHaveBeenCalledWith('/streaming/choices/episode', { serverId: 'server' });
    });

    it('saves a pick against the item whose tracks it came from', async () => {
        await streamingService.rememberChoice('episode', { audioTrackId: 'japanese' });
        expect(put).toHaveBeenCalledWith('/streaming/choices/episode', { audioTrackId: 'japanese' }, { serverId: undefined });
    });

    it('hands one kind back to the server', async () => {
        await streamingService.rememberChoice('movie', { forget: ['Subtitles'] }, 'server');
        expect(put).toHaveBeenCalledWith('/streaming/choices/movie', { forget: ['Subtitles'] }, { serverId: 'server' });
    });
});
