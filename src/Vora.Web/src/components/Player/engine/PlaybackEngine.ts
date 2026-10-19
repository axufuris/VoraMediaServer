import type { ChromeIntent, PlayerChromeState, PlayerOutputInfo, ScrubSpriteSpec, TrackRef } from '../../../desktop/desktopBridge';

export interface EngineSource {
    url: string;
    container?: string;
    strategy?: string;
    startPosition: number;
    title: string;
    audioTrack: TrackRef | null;
    subtitleTrack: TrackRef | null;
    isFinished: () => boolean;
}

export interface LocalTrackSelection {
    audioTrack?: TrackRef;
    subtitleTrack?: TrackRef | null;
}

export interface TextSubtitleSource {
    sessionId: string;
    trackId: string;
    serverId?: string;
    title: string;
    track: TrackRef | null;
}

export type TextSubtitleResult = 'shown' | 'unavailable' | 'superseded';

export interface EngineError {
    message: string;
    beforeFirstFrame: boolean;
}

export interface EngineSnapshot {
    position: number;
    duration: number;
    paused: boolean;
}

export interface EngineReading {
    engine: string;
    droppedFrames: number | null;
    totalFrames: number | null;
    bufferedSeconds: number | null;
    bandwidthKbps: number | null;
}

export type EngineEvent =
    | { type: 'time'; position: number; duration: number }
    | { type: 'paused'; paused: boolean }
    | { type: 'buffering'; buffering: boolean }
    | { type: 'ended' }
    | { type: 'error'; error: EngineError }
    | { type: 'intent'; intent: ChromeIntent }
    | { type: 'info'; info: PlayerOutputInfo };

export type EngineEventHandler = (event: EngineEvent) => void;

export interface PlaybackEngine {
    readonly kind: 'web' | 'desktop';
    load(source: EngineSource): Promise<void>;
    unload(): void;
    selectLocalTracks(selection: LocalTrackSelection): boolean;
    canShowTextSubtitles(): boolean;
    showTextSubtitle(subtitle: TextSubtitleSource, stillWanted: () => boolean): Promise<TextSubtitleResult>;
    hideTextSubtitle(): void;
    setChromeState(state: PlayerChromeState): void;
    setScrubSprite(spec: ScrubSpriteSpec | null): void;
    read(): EngineReading;
    on(handler: EngineEventHandler): () => void;
}

export interface PlaybackTransport {
    play(): void;
    pause(): void;
    seek(seconds: number): void;
    snapshot(): EngineSnapshot;
    stop(): Promise<void>;
    on(handler: EngineEventHandler): () => void;
}

export class EngineEventHub {
    private readonly handlers = new Set<EngineEventHandler>();

    add(handler: EngineEventHandler): () => void {
        this.handlers.add(handler);
        return () => { this.handlers.delete(handler); };
    }

    emit(event: EngineEvent): void {
        for (const handler of this.handlers) handler(event);
    }
}
