import { createContext, useContext } from 'react';
import type { PlaybackTransport } from '../components/Player/engine/PlaybackEngine';

export interface PlaybackTransportHost {
    attachTransport: (transport: PlaybackTransport) => () => void;
}

export const PlaybackTransportContext = createContext<PlaybackTransportHost | undefined>(undefined);

export const usePlaybackTransportHost = (): PlaybackTransportHost => {
    const context = useContext(PlaybackTransportContext);
    if (!context) throw new Error("usePlaybackTransportHost must be used within a PlayerProvider");
    return context;
};
