import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const post = vi.hoisted(() => vi.fn());
vi.mock('../client', () => ({ apiClient: { post } }));

import { passthroughService } from './passthroughService';
import { timeshiftService } from './timeshiftService';
import { liveVideoLimits } from '../../utils/liveVideoLimits';

describe('starting a live TV channel', () => {
    beforeEach(() => {
        post.mockReset();
        post.mockResolvedValue({ data: { url: '/x', streamType: 'hls' } });
    });

    afterEach(() => vi.unstubAllGlobals());

    it('tells the server what the player takes, on a timeshift start and a passthrough start alike', async () => {
        vi.stubGlobal('MediaSource', { isTypeSupported: () => true });

        await timeshiftService.startTimeshift('c1', 's1', liveVideoLimits());
        await passthroughService.startPassthrough('c1', 's1', liveVideoLimits());

        expect(post).toHaveBeenNthCalledWith(1, '/iptv/timeshift/start', { channelId: 'c1', videoCodecs: ['h264', 'hevc'] }, { serverId: 's1' });
        expect(post).toHaveBeenNthCalledWith(2, '/iptv/passthrough/start', { channelId: 'c1', videoCodecs: ['h264', 'hevc'] }, { serverId: 's1' });
    });

    it('sends only H.264 from a browser whose Media Source cannot take HEVC, so the server encodes HEVC for it', async () => {
        vi.stubGlobal('MediaSource', { isTypeSupported: (type: string) => !type.includes('hvc1') });

        await passthroughService.startPassthrough('c1', undefined, liveVideoLimits());

        expect(post).toHaveBeenCalledWith('/iptv/passthrough/start', { channelId: 'c1', videoCodecs: ['h264'] }, { serverId: undefined });
    });

    it('never offers MPEG-2, which no browser plays', () => {
        vi.stubGlobal('MediaSource', { isTypeSupported: () => true });

        expect(liveVideoLimits().videoCodecs).not.toContain('mpeg2video');
    });

    it('sends the channel alone for a radio station', async () => {
        await passthroughService.startPassthrough('r1', 's1');
        await timeshiftService.startTimeshift('r1', 's1');

        expect(post).toHaveBeenNthCalledWith(1, '/iptv/passthrough/start', { channelId: 'r1' }, { serverId: 's1' });
        expect(post).toHaveBeenNthCalledWith(2, '/iptv/timeshift/start', { channelId: 'r1' }, { serverId: 's1' });
    });
});
