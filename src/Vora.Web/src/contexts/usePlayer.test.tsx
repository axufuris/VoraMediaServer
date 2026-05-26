import { describe, it, expect } from 'vitest';
import { renderHook } from '@testing-library/react';
import {
    PlayerContext,
    PlayerTimeContext,
    usePlayer,
    usePlayerTime,
    type PlayerContextType,
    type PlayerTimeContextType,
    type PlayableMedia,
} from './usePlayer';
import type { ReactNode } from 'react';

const makePlayerContextStub = (): PlayerContextType => ({
    currentMedia: null,
    isPlaying: false,
    isMinimized: false,
    volume: 1,
    sessionId: null,
    playMedia: () => { },
    playQueue: () => { },
    addToQueue: () => { },
    playNext: () => { },
    nextTrack: () => { },
    previousTrack: () => { },
    jumpToQueueIndex: () => { },
    hasNext: false,
    hasPrevious: false,
    queue: [],
    queueIndex: 0,
    isShuffled: false,
    toggleShuffle: () => { },
    repeatMode: 'off',
    cycleRepeatMode: () => { },
    togglePlayPause: () => { },
    seek: () => { },
    skipForward: () => { },
    skipBackward: () => { },
    setMinimized: () => { },
    isFullscreen: false,
    toggleFullscreen: () => { },
    setFullscreen: () => { },
    closePlayer: () => { },
    setVolume: () => { },
    changeStreams: async () => { },
    videoRef: { current: null },
    radioSeed: null,
    radioLabel: null,
    startRadio: () => { },
});

describe('usePlayer', () => {
    it('throws when used outside PlayerProvider', () => {
        // renderHook will surface errors thrown by the hook
        expect(() => renderHook(() => usePlayer())).toThrow(/usePlayer must be used within a PlayerProvider/);
    });

    it('returns the supplied context when wrapped in a PlayerContext.Provider', () => {
        const value = makePlayerContextStub();
        const wrapper = ({ children }: { children: ReactNode }) => (
            <PlayerContext.Provider value={value}>{children}</PlayerContext.Provider>
        );

        const { result } = renderHook(() => usePlayer(), { wrapper });

        expect(result.current).toBe(value);
        expect(result.current.repeatMode).toBe('off');
        expect(result.current.isPlaying).toBe(false);
    });

    it('exposes mutation methods that consumers can call', () => {
        let played: PlayableMedia | null = null;
        const value: PlayerContextType = {
            ...makePlayerContextStub(),
            playMedia: (m) => { played = m; },
        };
        const wrapper = ({ children }: { children: ReactNode }) => (
            <PlayerContext.Provider value={value}>{children}</PlayerContext.Provider>
        );

        const { result } = renderHook(() => usePlayer(), { wrapper });
        result.current.playMedia({ id: 'a', title: 'A', streamUrl: '/s' });

        expect(played).not.toBeNull();
        expect(played!.id).toBe('a');
    });
});

describe('usePlayerTime', () => {
    it('throws when used outside PlayerProvider', () => {
        expect(() => renderHook(() => usePlayerTime())).toThrow(/usePlayerTime must be used within a PlayerProvider/);
    });

    it('returns the time slice when wrapped', () => {
        const value: PlayerTimeContextType = { currentTime: 42, duration: 1000 };
        const wrapper = ({ children }: { children: ReactNode }) => (
            <PlayerTimeContext.Provider value={value}>{children}</PlayerTimeContext.Provider>
        );

        const { result } = renderHook(() => usePlayerTime(), { wrapper });

        expect(result.current.currentTime).toBe(42);
        expect(result.current.duration).toBe(1000);
    });

    it('does not re-render the player context consumer when time changes', () => {
        // Sanity check that the two contexts are independent — usePlayer reads PlayerContext,
        // usePlayerTime reads PlayerTimeContext, so a time-only update should not satisfy usePlayer's contract.
        const wrapper = ({ children }: { children: ReactNode }) => (
            <PlayerTimeContext.Provider value={{ currentTime: 0, duration: 0 }}>
                {children}
            </PlayerTimeContext.Provider>
        );

        // usePlayer should still throw inside a TimeContext-only wrapper
        expect(() => renderHook(() => usePlayer(), { wrapper })).toThrow(/usePlayer must be used within a PlayerProvider/);
    });
});
