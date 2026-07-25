import { createContext, useContext } from 'react';
import type { RadioSeed } from '../api/Music/musicService';
import type { MediaMarker } from '../api/Media/mediaService';

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
    // Source resolution / HDR (from media-detail VM, what the file actually is)
    resolution?: string;
    hdrType?: string;
    // Output resolution / HDR (from StartStreamResponse, what's actually
    // delivered to the player after transcoding). Used by the badge bar
    // so it doesn't lie about an HDR→SDR or 4K→1080p transcode.
    outputResolution?: string | null;
    outputHdrType?: string | null;
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
    // True when playing a local movie "extra" (trailer/featurette). Suppresses
    // media-detail track fetches and up-next, and keeps ping from writing
    // watch progress onto the parent movie.
    isExtra?: boolean;
    commercialMarkers?: { start: number, end: number }[];
    skipMarkers?: MediaMarker[];
}

export type RepeatMode = 'off' | 'all' | 'one';

export interface PlayerContextType {
    currentMedia: PlayableMedia | null;
    isPlaying: boolean;
    isMinimized: boolean;
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
    skipForward: (seconds?: number) => void;
    skipBackward: (seconds?: number) => void;
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

export interface PlayerTimeContextType {
    currentTime: number;
    duration: number;
}

export const PlayerContext = createContext<PlayerContextType | undefined>(undefined);
export const PlayerTimeContext = createContext<PlayerTimeContextType | undefined>(undefined);

export const usePlayer = (): PlayerContextType => {
    const context = useContext(PlayerContext);
    if (!context) throw new Error("usePlayer must be used within a PlayerProvider");
    return context;
};

export const usePlayerTime = (): PlayerTimeContextType => {
    const context = useContext(PlayerTimeContext);
    if (!context) throw new Error("usePlayerTime must be used within a PlayerProvider");
    return context;
};
