import { describe, expect, it } from 'vitest';
import type { PlaybackAudioStats, PlaybackVideoStats } from '../api/Streaming/streamingService';
import {
    audioOutput,
    audioSourceDetail,
    audioSourceTitle,
    methodLabel,
    pictureClass,
    rateText,
    reasonLines,
    subtitleSummary,
    videoOutput,
    videoSourceDetail,
    videoSourceTitle,
} from './playbackStats';

const hdrSource: PlaybackVideoStats = {
    codec: 'hevc',
    profile: 'Main 10',
    level: '5.1',
    bitDepth: 10,
    hdrType: 'HDR10',
    width: 3840,
    height: 1600,
    frameRate: 23.976,
    bitrateKbps: 14123,
    strategy: 'Transcode',
    outputCodec: 'h264',
    outputWidth: 1920,
    outputHeight: 800,
    outputHdrType: 'SDR',
    outputBitrateKbps: 6800,
    outputPeakKbps: 8000,
    toneMapping: 'hable (GPU, CUDA)',
};

const dts: PlaybackAudioStats = {
    codec: 'dts',
    profile: 'DTS-HD MA',
    channels: 6,
    channelLayout: '5.1(side)',
    sampleRate: 48000,
    language: 'eng',
    isDefault: true,
    bitrateKbps: 3812,
    strategy: 'Transcode',
    outputCodec: 'aac',
    outputChannels: 2,
    outputBitrateKbps: 192,
};

describe('playback stats wording', () => {
    it('describes a 4K HDR film and what it is turned into', () => {
        expect(videoSourceTitle(hdrSource)).toBe('4K HDR10 HEVC');
        expect(videoSourceDetail(hdrSource)).toBe('Main 10 · Level 5.1 · 10-bit · 14.1 Mbps · 23.976 fps');
        expect(videoOutput(hdrSource)).toBe('Transcode to H264 · 1920×800 SDR · 6.8 Mbps (peaks 8.0 Mbps)');
    });

    it('says only Direct for video that is not converted', () => {
        expect(videoOutput({ ...hdrSource, strategy: 'DirectPlay' })).toBe('Direct');
        expect(videoOutput({ ...hdrSource, strategy: 'Copy' })).toBe('Copy');
    });

    it('describes lossless audio converted for the browser', () => {
        expect(audioSourceTitle(dts)).toBe('English · DTS-HD MA 5.1 (Default)');
        expect(audioSourceDetail(dts)).toBe('48 kHz · 5.1(side) · 3.8 Mbps');
        expect(audioOutput(dts)).toBe('Transcode to AAC 2.0 · 192 kbps');
    });

    it('names audio by its codec when the file gives no profile or language', () => {
        expect(audioSourceTitle({ codec: 'ac3', channels: 2, isDefault: false, strategy: 'DirectStream' })).toBe('AC3 2.0');
        expect(audioOutput({ codec: 'ac3', isDefault: false, strategy: 'DirectStream' })).toBe('Direct');
    });

    it('classes a picture by both of its sides', () => {
        expect(pictureClass(3840, 1600)).toBe('4K');
        expect(pictureClass(1920, 800)).toBe('1080p');
        expect(pictureClass(1080, 1920)).toBe('1080p');
        expect(pictureClass(1280, 536)).toBe('720p');
        expect(pictureClass(720, 480)).toBe('480p');
        expect(pictureClass(undefined, 1080)).toBeNull();
    });

    it('writes rates in the unit that reads best', () => {
        expect(rateText(192)).toBe('192 kbps');
        expect(rateText(14123)).toBe('14.1 Mbps');
        expect(rateText(0)).toBeNull();
    });

    it('calls the ways of playing what people know them as', () => {
        expect(methodLabel('DirectPlay')).toBe('Direct Play');
        expect(methodLabel('Remux')).toBe('Direct Stream');
        expect(methodLabel('Transcode')).toBe('Transcode');
    });

    it('describes a burned-in forced subtitle', () => {
        expect(subtitleSummary({ codec: 'hdmv_pgs_subtitle', language: 'eng', isForced: true, isExternal: false, strategy: 'BurnIn' }))
            .toBe('English (Forced) · PGS · Burned in');
    });

    it('splits each reason into its area and what it says, leaving out scoring notes', () => {
        expect(reasonLines([
            'Video Transcode: Bitrate (14.1 Mbps) exceeds Remote limit (8.0 Mbps).',
            'Audio Transcode: \'dts\' unsupported.',
            '[Commentary Track Penalized]',
            'Pick: the chosen version or track is not here as it is or would have to be converted, so the closest one that plays was chosen.',
        ])).toEqual([
            { area: 'Video Transcode', text: 'Bitrate (14.1 Mbps) exceeds Remote limit (8.0 Mbps).' },
            { area: 'Audio Transcode', text: '\'dts\' unsupported.' },
            { area: 'Pick', text: 'the chosen version or track is not here as it is or would have to be converted, so the closest one that plays was chosen.' },
        ]);
    });
});
