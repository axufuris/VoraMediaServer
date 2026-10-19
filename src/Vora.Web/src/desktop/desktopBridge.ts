export const DESKTOP_BRIDGE_VERSION = 1;

export type DesktopChannel = 'qa' | 'prod';

export type DesktopPlatform = 'windows' | 'linux';

export interface MpvStatus {
    available: boolean;
    version: string | null;
    reason: string | null;
}

export interface DesktopCapabilities {
    videoCodecs: string[];
    audioCodecs: string[];
    containers: string[];
    maxAudioChannels: number;
    detectedAudioChannels: number;
    displayWidth: number;
    displayHeight: number;
}

export type AudioPassthrough = 'off' | 'core' | 'all';

export type HardwareDecoding = 'auto' | 'off';

export interface DesktopSettings {
    useMpvForVideo: boolean;
    audioPassthrough: AudioPassthrough;
    hdrOutput: boolean;
    hardwareDecoding: HardwareDecoding;
}

export interface TrackRef {
    position: number;
    codec: string | null;
    language: string | null;
}

export interface OpenRequest {
    url: string;
    startSeconds: number;
    title: string;
    audioTrack: TrackRef | null;
    subtitleTrack: TrackRef | null;
}

export interface SidecarSubtitle {
    title: string;
    vtt: string;
}

export type PlayerCommand =
    | { type: 'play' }
    | { type: 'pause' }
    | { type: 'seek'; seconds: number }
    | { type: 'tracks'; audioTrack?: TrackRef; subtitleTrack?: TrackRef | null }
    | { type: 'sub-add'; subtitle: SidecarSubtitle }
    | { type: 'load-next'; request: OpenRequest };

export interface ScrubCue {
    start: number;
    end: number;
    x: number;
    y: number;
}

export interface ScrubSpriteSpec {
    image: ArrayBuffer;
    cueWidth: number;
    cueHeight: number;
    cues: ScrubCue[];
}

export type MarkerKind = 'Intro' | 'Recap' | 'Preview' | 'Credits' | 'CreditsScene';

export interface TrackOption {
    id: string;
    label: string;
    detail: string | null;
    selected: boolean;
}

export interface PlayerChromeState {
    title: string;
    subtitle: string | null;
    theme: { accent: string; surface: string; text: string; textMuted: string };
    status: 'loading' | 'playing' | 'error';
    statusText: string | null;
    markers: { kind: MarkerKind; start: number; end: number }[];
    skip: { label: string } | null;
    upNext: { title: string; subtitle: string | null; countdownSeconds: number | null } | null;
    audio: TrackOption[];
    subtitles: TrackOption[];
    quality: { options: { id: string; label: string; selected: boolean }[]; currentLabel: string };
    info: { label: string; value: string }[];
    canPlayNext: boolean;
    canPlayPrevious: boolean;
}

export type ChromeIntent =
    | { type: 'toggle-pause' }
    | { type: 'seek'; to: number }
    | { type: 'skip-marker' }
    | { type: 'play-next' }
    | { type: 'play-previous' }
    | { type: 'cancel-up-next' }
    | { type: 'select-audio'; id: string }
    | { type: 'select-subtitle'; id: string | null }
    | { type: 'select-quality'; id: string }
    | { type: 'exit' };

export interface PlayerOutputInfo {
    videoCodec: string | null;
    width: number | null;
    height: number | null;
    outputTransfer: string | null;
    hardwareDecoder: string | null;
    audioCodec: string | null;
    audioChannels: number | null;
}

export type PlayerEvent =
    | { type: 'loaded' }
    | { type: 'time'; position: number; duration: number }
    | { type: 'paused'; paused: boolean }
    | { type: 'buffering'; buffering: boolean }
    | { type: 'ended' }
    | { type: 'error'; message: string }
    | { type: 'intent'; intent: ChromeIntent }
    | { type: 'info'; info: PlayerOutputInfo }
    | { type: 'closed' };

export interface VoraDesktopBridge {
    bridgeVersion: number;
    shellVersion: string;
    channel: DesktopChannel;
    platform: DesktopPlatform;
    mpv: MpvStatus;
    onMpvChanged(handler: (status: MpvStatus) => void): () => void;
    capabilities(): Promise<DesktopCapabilities>;
    player: {
        open(request: OpenRequest): Promise<void>;
        command(command: PlayerCommand): void;
        setChromeState(state: PlayerChromeState): void;
        setScrubSprite(spec: ScrubSpriteSpec | null): Promise<void>;
        close(): Promise<void>;
        onEvent(handler: (event: PlayerEvent) => void): () => void;
    };
    settings: {
        get(): Promise<DesktopSettings>;
        set(patch: Partial<DesktopSettings>): Promise<DesktopSettings>;
        openAdvancedFolder(): Promise<void>;
    };
    servers: { switchServer(): void };
}
