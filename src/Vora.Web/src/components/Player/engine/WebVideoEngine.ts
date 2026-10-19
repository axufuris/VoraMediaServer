import type Hls from 'hls.js';
import type { RefObject } from 'react';
import { streamingService } from '../../../api/Streaming/streamingService';
import { loadHls } from '../../../utils/loadHls';
import { safeMediaUrl } from '../../../utils/safeUrl';
import { placeCues } from '../../../utils/subtitleCuePlacement';
import {
    EngineEventHub,
    type EngineEventHandler,
    type EngineReading,
    type EngineSource,
    type PlaybackEngine,
    type TextSubtitleResult,
    type TextSubtitleSource,
} from './PlaybackEngine';

interface WebStream {
    video: HTMLVideoElement;
    hls: Hls | null;
    cancelled: boolean;
}

export class WebVideoEngine implements PlaybackEngine {
    readonly kind = 'web';
    private readonly videoRef: RefObject<HTMLVideoElement | null>;
    private readonly events = new EngineEventHub();
    private stream: WebStream | null = null;
    private hls: Hls | null = null;
    private label = '';
    private sideloadedTrack: HTMLTrackElement | null = null;
    private sideloadListeners: AbortController | null = null;

    constructor(videoRef: RefObject<HTMLVideoElement | null>) {
        this.videoRef = videoRef;
    }

    async load(source: EngineSource): Promise<void> {
        const video = this.videoRef.current;
        const streamUrl = safeMediaUrl(source.url);
        if (!video || !streamUrl) return;

        const stream: WebStream = { video, hls: null, cancelled: false };
        this.stream = stream;

        if (source.container === 'hls' || source.strategy === 'Transcode' || streamUrl.includes('.m3u8')) {
            const HlsClass = await loadHls();
            if (stream.cancelled) return;

            if (HlsClass.isSupported()) {
                const instance = new HlsClass({
                    enableWorker: true,
                    lowLatencyMode: true,
                });
                if (stream.cancelled) { instance.destroy(); return; }

                instance.loadSource(streamUrl);
                instance.attachMedia(video);
                this.hls = instance;
                this.label = 'hls.js';
                instance.on(HlsClass.Events.MANIFEST_PARSED, () => {
                    video.play().catch(e => console.error('Auto-play blocked:', e));
                });

                // 410 is the server saying the segment is past the end of
                // the file: the video ends there, so stop asking for it.
                // Restarting the load on every fatal network error used to
                // ask for that segment forever, frozen on the last frame.
                let recoveredMediaError = false;
                instance.on(HlsClass.Events.ERROR, (_, data) => {
                    if (data.response?.code === 410) {
                        instance.stopLoad();
                        this.events.emit({ type: 'ended' });
                        return;
                    }
                    if (!data.fatal) return;
                    if (data.type === HlsClass.ErrorTypes.NETWORK_ERROR) {
                        if (!source.isFinished()) instance.startLoad();
                    } else if (data.type === HlsClass.ErrorTypes.MEDIA_ERROR && !recoveredMediaError) {
                        recoveredMediaError = true;
                        instance.recoverMediaError();
                    } else {
                        instance.destroy();
                    }
                });

                stream.hls = instance;
            } else if (video.canPlayType('application/vnd.apple.mpegurl')) {
                this.label = 'Native HLS';
                video.src = streamUrl;
                video.addEventListener('loadedmetadata', () => {
                    video.play().catch(e => console.error('Auto-play blocked:', e));
                });
            }
        } else {
            this.label = 'The browser, from the file';
            video.src = streamUrl;
            video.play().catch(e => console.error('Auto-play blocked:', e));
        }
    }

    unload(): void {
        const stream = this.stream;
        this.stream = null;
        this.hls = null;
        this.label = '';
        if (!stream) return;
        stream.cancelled = true;
        if (stream.hls) {
            stream.hls.destroy();
        }
        stream.video.removeAttribute('src');
        stream.video.load();
    }

    selectLocalTracks(): boolean {
        return false;
    }

    canShowTextSubtitles(): boolean {
        return this.videoRef.current !== null;
    }

    async showTextSubtitle(subtitle: TextSubtitleSource, stillWanted: () => boolean): Promise<TextSubtitleResult> {
        const video = this.videoRef.current;
        if (!video) return 'superseded';

        const objectUrl = await streamingService.fetchSubtitleVtt(subtitle.sessionId, subtitle.trackId, subtitle.serverId);
        if (!objectUrl) return 'unavailable';
        // The media may have changed while the VTT was in flight.
        if (this.videoRef.current !== video || !stillWanted()) return 'superseded';

        const track = document.createElement('track');
        track.kind = 'subtitles';
        track.label = 'Subtitles';
        track.default = true;
        track.src = objectUrl;
        video.appendChild(track);
        this.sideloadedTrack = track;

        // The browser parks a freshly attached track at 'disabled' until the
        // cue file loads, so the mode is set again on load rather than only
        // here — setting it once can be silently undone. The cues only exist
        // once loaded, which is also the first moment they can be placed.
        const listeners = new AbortController();
        this.sideloadListeners = listeners;

        track.addEventListener('load', () => {
            track.track.mode = 'showing';
            placeCues(track.track.cues);
        }, { once: true, signal: listeners.signal });
        if (track.track) track.track.mode = 'showing';

        // A media element reset (a source swap inside the same session)
        // parks every text track at 'disabled' again, which would drop the
        // subtitle with nothing on screen to explain it. The <video> outlives
        // every track attached to it, so this listener has to come off with
        // the track rather than piling up one per subtitle change.
        video.addEventListener('loadedmetadata', () => {
            if (this.sideloadedTrack === track) track.track.mode = 'showing';
        }, { signal: listeners.signal });

        return 'shown';
    }

    hideTextSubtitle(): void {
        this.sideloadListeners?.abort();
        this.sideloadListeners = null;

        const track = this.sideloadedTrack;
        this.sideloadedTrack = null;
        if (!track) return;
        if (track.track) track.track.mode = 'disabled';
        track.remove();
    }

    setChromeState(): void { }

    setScrubSprite(): void { }

    read(): EngineReading {
        const video = this.videoRef.current;
        const quality = video && typeof video.getVideoPlaybackQuality === 'function' ? video.getVideoPlaybackQuality() : null;
        let bufferedSeconds: number | null = null;
        if (video) {
            for (let index = 0; index < video.buffered.length; index++) {
                if (video.buffered.start(index) <= video.currentTime && video.currentTime <= video.buffered.end(index)) {
                    bufferedSeconds = Math.max(0, video.buffered.end(index) - video.currentTime);
                }
            }
        }
        const estimate = this.hls?.bandwidthEstimate;
        return {
            engine: this.label,
            droppedFrames: quality ? quality.droppedVideoFrames : null,
            totalFrames: quality ? quality.totalVideoFrames : null,
            bufferedSeconds,
            bandwidthKbps: estimate && Number.isFinite(estimate) ? estimate / 1000 : null,
        };
    }

    on(handler: EngineEventHandler): () => void {
        return this.events.add(handler);
    }
}
