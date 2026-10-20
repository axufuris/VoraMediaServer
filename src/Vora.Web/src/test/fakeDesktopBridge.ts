import { vi } from 'vitest';
import type {
    DesktopCapabilities,
    DesktopSettings,
    MpvStatus,
    PlayerEvent,
    VoraDesktopBridge,
} from '../desktop/desktopBridge';

export const desktopCapabilities: DesktopCapabilities = {
    videoCodecs: ['h264', 'hevc', 'av1', 'vp9', 'vp8', 'mpeg2video', 'mpeg4', 'vc1'],
    audioCodecs: ['aac', 'ac3', 'eac3', 'truehd', 'dts', 'flac', 'opus', 'vorbis', 'mp3', 'pcm_s16le', 'pcm_s24le'],
    containers: ['mkv', 'mp4', 'avi', 'm4v', 'mov', 'mts', 'm2ts', '3gp', 'mpg', 'mpeg', 'wmv', 'webm', 'ts'],
    maxAudioChannels: 8,
    detectedAudioChannels: 8,
    displayWidth: 3840,
    displayHeight: 2160,
};

export const desktopSettings: DesktopSettings = {
    useMpvForVideo: true,
    audioPassthrough: 'off',
    hdrOutput: true,
    hardwareDecoding: 'auto',
};

export interface FakeDesktopBridge {
    bridge: VoraDesktopBridge;
    emit: (event: PlayerEvent) => void;
    changeMpv: (status: MpvStatus) => void;
}

export function fakeDesktopBridge(overrides: Partial<Omit<VoraDesktopBridge, 'player' | 'settings' | 'servers'>> = {}, settings: DesktopSettings = desktopSettings): FakeDesktopBridge {
    const playerHandlers = new Set<(event: PlayerEvent) => void>();
    const mpvHandlers = new Set<(status: MpvStatus) => void>();
    let saved = settings;

    const bridge: VoraDesktopBridge = {
        bridgeVersion: 1,
        shellVersion: '1.0.0',
        channel: 'prod',
        platform: 'linux',
        mpv: { available: true, version: '0.39.0', reason: null },
        onMpvChanged: handler => {
            mpvHandlers.add(handler);
            return () => { mpvHandlers.delete(handler); };
        },
        capabilities: () => Promise.resolve(desktopCapabilities),
        player: {
            open: vi.fn(() => Promise.resolve()),
            command: vi.fn(),
            setChromeState: vi.fn(),
            setScrubSprite: vi.fn(() => Promise.resolve()),
            close: vi.fn(() => Promise.resolve()),
            onEvent: handler => {
                playerHandlers.add(handler);
                return () => { playerHandlers.delete(handler); };
            },
        },
        settings: {
            get: () => Promise.resolve(saved),
            set: vi.fn((patch: Partial<DesktopSettings>) => {
                saved = { ...saved, ...patch };
                return Promise.resolve(saved);
            }),
            openAdvancedFolder: vi.fn(() => Promise.resolve()),
        },
        servers: { switchServer: vi.fn() },
        ...overrides,
    };

    return {
        bridge,
        emit: event => { for (const handler of playerHandlers) handler(event); },
        changeMpv: status => { for (const handler of mpvHandlers) handler(status); },
    };
}
