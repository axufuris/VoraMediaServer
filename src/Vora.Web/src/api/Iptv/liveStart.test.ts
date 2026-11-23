import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const post = vi.hoisted(() => vi.fn());
vi.mock('../client', () => ({ apiClient: { post } }));

import { passthroughService } from './passthroughService';
import { timeshiftService } from './timeshiftService';
import { liveVideoLimits, screenVideoHeight } from '../../utils/liveVideoLimits';
import { liveStartRefusal } from '../../utils/liveStartError';

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

describe('the height a screen asks the server to encode to', () => {
    it('is 720 on a phone-sized screen, whatever its pixel density', () => {
        expect(screenVideoHeight({ width: 390, height: 844 }, 3)).toBe(720);
        expect(screenVideoHeight({ width: 844, height: 390 }, 3)).toBe(720);
        expect(screenVideoHeight({ width: 412, height: 915 }, 2.625)).toBe(720);
    });

    it("is the screen height in device pixels on anything bigger, for the server to snap to 480, 720 or 1080", () => {
        expect(screenVideoHeight({ width: 1920, height: 1080 }, 1)).toBe(1080);
        expect(screenVideoHeight({ width: 1366, height: 768 }, 1)).toBe(768);
        expect(screenVideoHeight({ width: 2560, height: 1440 }, 1.5)).toBe(2160);
        expect(screenVideoHeight({ width: 1024, height: 1366 }, 2)).toBe(2048);
    });

    it('is left out when the browser does not say', () => {
        expect(screenVideoHeight({ width: 0, height: 0 }, 1)).toBeUndefined();
        expect(screenVideoHeight({ width: 1920, height: 1080 }, 0)).toBe(1080);
    });

    it('goes to the server with the codecs when the browser reports a screen', async () => {
        vi.stubGlobal('MediaSource', { isTypeSupported: () => true });
        vi.stubGlobal('screen', { width: 390, height: 844 });
        post.mockReset();
        post.mockResolvedValue({ data: { url: '/x' } });

        await timeshiftService.startTimeshift('c1', 's1', liveVideoLimits());

        expect(post).toHaveBeenCalledWith('/iptv/timeshift/start', { channelId: 'c1', videoCodecs: ['h264', 'hevc'], maxHeight: 720 }, { serverId: 's1' });
        vi.unstubAllGlobals();
    });
});

describe('why a live channel would not start', () => {
    it("is the server message when it is at its transcode limit", () => {
        const err = { response: { status: 503, data: { error: "Maximum CPU transcodes reached. This channel has to be transcoded for this device, so it can't start until another stream ends." } } };

        expect(liveStartRefusal(err)).toBe("Maximum CPU transcodes reached. This channel has to be transcoded for this device, so it can't start until another stream ends.");
    });

    it('is nothing for tuners in use, any other failure, or a 503 with no message', () => {
        expect(liveStartRefusal({ response: { status: 409, data: { error: 'All available tuners for this playlist are in use.' } } })).toBeUndefined();
        expect(liveStartRefusal({ response: { status: 500, data: { error: 'boom' } } })).toBeUndefined();
        expect(liveStartRefusal({ response: { status: 503, data: {} } })).toBeUndefined();
        expect(liveStartRefusal({ response: { status: 503, data: { error: '  ' } } })).toBeUndefined();
        expect(liveStartRefusal(new Error('Network Error'))).toBeUndefined();
        expect(liveStartRefusal(null)).toBeUndefined();
    });
});
