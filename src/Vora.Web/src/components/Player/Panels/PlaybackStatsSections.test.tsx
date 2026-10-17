import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, render, screen, within } from '@testing-library/react';
import PlaybackStatsSections, { SERVER_REFRESH_MS, type PlayerReading } from './PlaybackStatsSections';
import type { PlaybackStats } from '../../../api/Streaming/streamingService';

const getSessionStats = vi.fn<(sessionId: string, serverId?: string) => Promise<PlaybackStats>>();

vi.mock('../../../api/Streaming/streamingService', () => ({
    streamingService: {
        getSessionStats: (sessionId: string, serverId?: string) => getSessionStats(sessionId, serverId),
    },
}));

const transcoding: PlaybackStats = {
    method: 'Transcode',
    delivery: 'HLS',
    sourceContainer: 'mkv',
    sourceBitrateKbps: 18500,
    streamBitrateKbps: 8000,
    video: {
        codec: 'hevc', profile: 'Main 10', level: '5.1', bitDepth: 10, hdrType: 'HDR10', width: 3840, height: 1600, frameRate: 23.976, bitrateKbps: 14123,
        strategy: 'Transcode', outputCodec: 'h264', outputWidth: 1920, outputHeight: 800, outputHdrType: 'SDR', outputBitrateKbps: 6800, outputPeakKbps: 8000,
        toneMapping: 'hable (GPU, CUDA)',
    },
    audio: {
        codec: 'dts', profile: 'DTS-HD MA', channels: 6, channelLayout: '5.1(side)', sampleRate: 48000, language: 'eng', isDefault: true, bitrateKbps: 3812,
        strategy: 'Transcode', outputCodec: 'aac', outputChannels: 2, outputBitrateKbps: 192,
    },
    subtitle: null,
    transcoder: { encoder: 'h264_nvenc', encodeHardware: 'NVIDIA NVENC', decodeHardware: 'NVIDIA NVDEC', fps: 96.4, speed: 4.01, running: true, retriedOnCpu: false },
};

const reading: PlayerReading = { engine: 'hls.js', droppedFrames: 3, totalFrames: 12345, bufferedSeconds: 31.6, bandwidthKbps: 23400 };

const statValue = (section: HTMLElement, label: string) =>
    within(section).getByText(label, { selector: 'dt' }).nextElementSibling?.textContent;

const section = (title: string) => {
    const found = screen.getByRole('heading', { name: title }).closest('section');
    if (!found) throw new Error(`no ${title} section`);
    return found;
};

describe('Playback info', () => {
    beforeEach(() => {
        vi.useFakeTimers({ shouldAdvanceTime: true });
        getSessionStats.mockResolvedValue(transcoding);
    });

    afterEach(() => {
        vi.useRealTimers();
        getSessionStats.mockReset();
    });

    it('sets what the server is doing beside what the player sees', async () => {
        render(<PlaybackStatsSections sessionId="session-1" serverId="srv1" reasons={['Video Transcode: Bitrate (14.1 Mbps) exceeds Remote limit (8.0 Mbps).']} readPlayer={() => reading} />);

        await screen.findByText('Transcode');
        expect(getSessionStats).toHaveBeenCalledWith('session-1', 'srv1');

        const stream = section('Stream');
        expect(statValue(stream, 'Source')).toBe('MKV · 18.5 Mbps');
        expect(statValue(stream, 'Delivered as')).toBe('HLS, up to 8.0 Mbps');
        expect(statValue(stream, 'Transcoder')).toBe('NVIDIA NVENC · 96 fps · 4.0×');
        expect(statValue(stream, 'Decoding')).toBe('NVIDIA NVDEC');
        expect(statValue(stream, 'Player')).toBe('hls.js');
        expect(statValue(stream, 'Buffered')).toBe('32 s ahead');
        expect(within(stream).getByRole('list', { name: 'Why it plays this way' })).toHaveTextContent('Video Transcode: Bitrate (14.1 Mbps) exceeds Remote limit (8.0 Mbps).');

        const video = section('Video');
        expect(statValue(video, 'Source')).toBe('4K HDR10 HEVC');
        expect(statValue(video, 'Output')).toBe('Transcode to H264 · 1920×800 SDR · 6.8 Mbps (peaks 8.0 Mbps)');
        expect(statValue(video, 'Tone mapping')).toBe('hable (GPU, CUDA)');
        expect(statValue(video, 'Dropped frames')).toBe('3 of 12,345');

        const audio = section('Audio');
        expect(statValue(audio, 'Source')).toBe('English · DTS-HD MA 5.1 (Default)');
        expect(statValue(audio, 'Output')).toBe('Transcode to AAC 2.0 · 192 kbps');
    });

    it('asks again while it is open, so the transcoder speed stays current', async () => {
        render(<PlaybackStatsSections sessionId="session-1" readPlayer={() => reading} />);
        await screen.findByText('Transcode');

        getSessionStats.mockResolvedValue({ ...transcoding, transcoder: { ...transcoding.transcoder, speed: 1.2, fps: 28, running: true, retriedOnCpu: false } });
        await act(async () => { await vi.advanceTimersByTimeAsync(SERVER_REFRESH_MS); });

        expect(statValue(section('Stream'), 'Transcoder')).toBe('NVIDIA NVENC · 28 fps · 1.2×');
    });

    it('says when the transcode has finished the whole file', async () => {
        getSessionStats.mockResolvedValue({ ...transcoding, transcoder: { encodeHardware: 'CPU', running: false, retriedOnCpu: false } });
        render(<PlaybackStatsSections sessionId="session-1" readPlayer={() => reading} />);

        await screen.findByText('Transcode');
        expect(statValue(section('Stream'), 'Transcoder')).toBe('CPU · Finished');
    });

    it('still shows what the player knows when the server cannot answer', async () => {
        getSessionStats.mockRejectedValue(new Error('gone'));
        render(<PlaybackStatsSections sessionId="session-1" readPlayer={() => reading} />);

        expect(await screen.findByText("The server couldn't say how this is playing. What the player knows is below.")).toBeInTheDocument();
        expect(statValue(section('Stream'), 'Player')).toBe('hls.js');
    });

    it('names a subtitle the player draws itself', async () => {
        getSessionStats.mockResolvedValue({ ...transcoding, method: 'DirectPlay', delivery: 'File', transcoder: null });
        render(<PlaybackStatsSections sessionId="session-1" readPlayer={() => reading} sideloadedSubtitle={{ language: 'eng', codec: 'subrip' }} />);

        await screen.findByText('Direct Play');
        expect(statValue(section('Stream'), 'Delivered as')).toBe('The file itself');
        expect(statValue(section('Subtitles'), 'Showing')).toBe('English · SRT · Drawn by the player');
    });
});
