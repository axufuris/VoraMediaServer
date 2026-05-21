import { createContext, useContext } from 'react';
import type { RadioSeed } from '../api/Music/musicService';

export interface PlayableMedia {
    id: string;
    title: string;
    subtitle?: string;
    posterUrl?: string;
    backgroundUrl?: string;
    streamUrl: string;
    serverId?: string;
    sessionId?: string;
    startPosition?: number;
    resolution?: string;
    hdrType?: string;
    audioChannels?: number;
    videoTrackId?: string;
    audioTrackId?: string;
    subtitleTrackId?: string | null;
    strategy?: string;
    videoStrategy?: string;
    audioStrategy?: string;
    subtitleStrategy?: string | null;
    videoCodec?: string;
    audioCodec?: string;
    targetAudioChannels?: number;
    container?: string;
    bandwidthKbps?: number;
    playbackContextType?: string;
    playbackContextId?: string;
    commercialMarkers?: { start: number, end: number }[];
}

export type RepeatMode = 'off' | 'all' | 'one';

export interface PlayerContextType {
    currentMedia: PlayableMedia | null;
    isPlaying: boolean;
    isMinimized: boolean;
    currentTime: number;
    duration: number;
    volume: number;
    sessionId: string | null;
    playMedia: (media: PlayableMedia) => void;
    playQueue: (items: PlayableMedia[], startIndex?: number) => void;
    addToQueue: (items: PlayableMedia[]) => void;
    playNext: (items: PlayableMedia[]) => void;
    nextTrack: () => void;
    previousTrack: () => void;
    jumpToQueueIndex: (idx: number) => void;
    hasNext: boolean;
    hasPrevious: boolean;
    queue: PlayableMedia[];
    queueIndex: number;
    isShuffled: boolean;
    toggleShuffle: () => void;
    repeatMode: RepeatMode;
    cycleRepeatMode: () => void;
    togglePlayPause: () => void;
    seek: (time: number) => void;
    skipForward: (seconds: number) => void;
    skipBackward: (seconds: number) => void;
    setMinimized: (min: boolean) => void;
    isFullscreen: boolean;
    toggleFullscreen: () => void;
    setFullscreen: (full: boolean) => void;
    closePlayer: () => void;
    setVolume: (vol: number) => void;
    changeStreams: (videoTrackId: string, audioTrackId: string, subtitleTrackId: string) => Promise<void>;
    videoRef: React.RefObject<HTMLVideoElement | null>;
    radioSeed: RadioSeed | null;
    radioLabel: string | null;
    startRadio: (seed: RadioSeed, label: string, items: PlayableMedia[]) => void;
}

export const PlayerContext = createContext<PlayerContextType | undefined>(undefined);

export const usePlayer = (): PlayerContextType => {
    const context = useContext(PlayerContext);
    if (!context) throw new Error("usePlayer must be used within a PlayerProvider");
    return context;
};
