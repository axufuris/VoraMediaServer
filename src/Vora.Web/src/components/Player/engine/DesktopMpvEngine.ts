import { streamingService } from '../../../api/Streaming/streamingService';
import type { OpenRequest, PlayerChromeState, PlayerEvent, ScrubSpriteSpec, VoraDesktopBridge } from '../../../desktop/desktopBridge';
import { safeMediaUrl } from '../../../utils/safeUrl';
import {
    EngineEventHub,
    type EngineEventHandler,
    type EngineReading,
    type EngineSnapshot,
    type EngineSource,
    type LocalTrackSelection,
    type PlaybackEngine,
    type PlaybackTransport,
    type TextSubtitleResult,
    type TextSubtitleSource,
} from './PlaybackEngine';

export const DESKTOP_ENGINE_LABEL = 'Vora player (mpv)';

function streamAddress(raw: string): string | null {
    const url = safeMediaUrl(raw);
    return url && /^https?:/i.test(url) ? url : null;
}

function messageOf(error: unknown): string {
    return error instanceof Error && error.message ? error.message : 'The Vora player did not start.';
}

export class DesktopMpvEngine implements PlaybackEngine, PlaybackTransport {
    readonly kind = 'desktop';
    private readonly bridge: VoraDesktopBridge;
    private readonly events = new EngineEventHub();
    private unsubscribe: (() => void) | null = null;
    private open = false;
    private directPlay = false;
    private loaded = false;
    private failed = false;
    private position = 0;
    private duration = 0;
    private paused = false;

    constructor(bridge: VoraDesktopBridge) {
        this.bridge = bridge;
    }

    async load(source: EngineSource): Promise<void> {
        this.listen();
        this.directPlay = source.strategy === 'DirectPlay';
        this.loaded = false;
        this.failed = false;
        this.position = source.startPosition;
        this.duration = 0;
        this.paused = false;

        const url = streamAddress(source.url);
        if (!url) {
            this.fail('The server sent a stream address the Vora player cannot open.');
            return;
        }

        const request: OpenRequest = {
            url,
            startSeconds: source.startPosition,
            title: source.title,
            audioTrack: this.directPlay ? source.audioTrack : null,
            subtitleTrack: this.directPlay ? source.subtitleTrack : null,
        };

        if (this.open) {
            this.bridge.player.command({ type: 'load-next', request });
            return;
        }

        this.open = true;
        try {
            await this.bridge.player.open(request);
        } catch (error: unknown) {
            this.fail(messageOf(error));
        }
    }

    unload(): void { }

    play(): void {
        if (this.open) this.bridge.player.command({ type: 'play' });
    }

    pause(): void {
        if (this.open) this.bridge.player.command({ type: 'pause' });
    }

    seek(seconds: number): void {
        if (!this.open) return;
        this.position = seconds;
        this.bridge.player.command({ type: 'seek', seconds });
    }

    snapshot(): EngineSnapshot {
        return { position: this.position, duration: this.duration, paused: this.paused };
    }

    async stop(): Promise<void> {
        if (!this.open) return;
        this.open = false;
        try {
            await this.bridge.player.close();
        } catch (error: unknown) {
            console.error('Could not close the Vora player', error);
        }
    }

    selectLocalTracks(selection: LocalTrackSelection): boolean {
        if (!this.open || !this.directPlay) return false;
        if (selection.audioTrack === undefined && selection.subtitleTrack === undefined) return false;
        this.bridge.player.command({ type: 'tracks', ...selection });
        return true;
    }

    canShowTextSubtitles(): boolean {
        return this.open;
    }

    async showTextSubtitle(subtitle: TextSubtitleSource, stillWanted: () => boolean): Promise<TextSubtitleResult> {
        if (this.directPlay && subtitle.track) {
            this.bridge.player.command({ type: 'tracks', subtitleTrack: subtitle.track });
            return 'shown';
        }

        const vtt = await streamingService.fetchSubtitleText(subtitle.sessionId, subtitle.trackId, subtitle.serverId);
        if (vtt === null) return 'unavailable';
        if (!this.open || !stillWanted()) return 'superseded';

        this.bridge.player.command({ type: 'sub-add', subtitle: { title: subtitle.title, vtt } });
        return 'shown';
    }

    hideTextSubtitle(): void {
        if (this.open) this.bridge.player.command({ type: 'tracks', subtitleTrack: null });
    }

    setChromeState(state: PlayerChromeState): void {
        if (this.open) this.bridge.player.setChromeState(state);
    }

    setScrubSprite(spec: ScrubSpriteSpec | null): void {
        this.bridge.player.setScrubSprite(spec)
            .catch((error: unknown) => console.error('Could not hand the scrub thumbnails to the Vora player', error));
    }

    read(): EngineReading {
        return { engine: DESKTOP_ENGINE_LABEL, droppedFrames: null, totalFrames: null, bufferedSeconds: null, bandwidthKbps: null };
    }

    on(handler: EngineEventHandler): () => void {
        return this.events.add(handler);
    }

    dispose(): void {
        this.unsubscribe?.();
        this.unsubscribe = null;
    }

    private listen(): void {
        if (!this.unsubscribe) this.unsubscribe = this.bridge.player.onEvent(event => this.handle(event));
    }

    private fail(message: string): void {
        this.failed = true;
        this.events.emit({ type: 'error', error: { message, beforeFirstFrame: !this.loaded } });
    }

    private handle(event: PlayerEvent): void {
        switch (event.type) {
            case 'loaded':
                this.loaded = true;
                return;
            case 'time':
                this.position = event.position;
                this.duration = event.duration;
                this.events.emit({ type: 'time', position: event.position, duration: event.duration });
                return;
            case 'paused':
                this.paused = event.paused;
                this.events.emit({ type: 'paused', paused: event.paused });
                return;
            case 'buffering':
                this.events.emit({ type: 'buffering', buffering: event.buffering });
                return;
            case 'ended':
                this.events.emit({ type: 'ended' });
                return;
            case 'error':
                this.fail(event.message);
                return;
            case 'intent':
                this.events.emit({ type: 'intent', intent: event.intent });
                return;
            case 'info':
                this.events.emit({ type: 'info', info: event.info });
                return;
            case 'closed':
                if (!this.open) return;
                this.open = false;
                if (!this.failed) this.events.emit({ type: 'intent', intent: { type: 'exit' } });
                return;
        }
    }
}
