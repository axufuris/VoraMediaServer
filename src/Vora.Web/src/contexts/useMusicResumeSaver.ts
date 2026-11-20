import { useCallback, useEffect, useRef } from 'react';
import { musicService, type MusicResumeSave } from '../api/Music/musicService';
import { musicResumeKey, musicResumeSnapshot } from '../utils/musicResume';
import type { PlayableMedia } from './usePlayer';

export const RESUME_SAVE_DELAY_MS = 1000;

interface Args {
    queue: PlayableMedia[];
    queueIndex: number;
    isPlaying: boolean;
    isRadio: boolean;
}

interface Pending {
    save: MusicResumeSave;
    serverId?: string;
}

// Saves where a music queue stopped so Continue Listening can pick it up again:
// when the song changes, when playback pauses and when the player is closed,
// not on a timer. A burst of skips is one save a second after the last one,
// and a queue that finishes by itself clears its point instead.
export function useMusicResumeSaver({ queue, queueIndex, isPlaying, isRadio }: Args) {
    const pending = useRef<Pending | null>(null);
    const timer = useRef<number | undefined>(undefined);
    const lastSent = useRef<string | null>(null);
    const clearedSong = useRef<string | null>(null);

    const sendPending = useCallback(() => {
        window.clearTimeout(timer.current);
        timer.current = undefined;
        const next = pending.current;
        pending.current = null;
        if (!next) return;

        const key = `${next.serverId ?? ''}|${musicResumeKey(next.save)}`;
        if (key === lastSent.current) return;
        lastSent.current = key;
        void musicService.saveResume(next.save, next.serverId);
    }, []);

    const current = queue[queueIndex];
    const currentSong = current?.playbackContextType === 'Music' ? current.id : null;
    const serverId = current?.serverId;

    useEffect(() => {
        if (isPlaying) clearedSong.current = null;

        const snapshot = isRadio || (clearedSong.current !== null && clearedSong.current === currentSong)
            ? null
            : musicResumeSnapshot(queue, queueIndex);
        if (!snapshot) {
            sendPending();
            return;
        }

        pending.current = { save: snapshot, serverId };
        window.clearTimeout(timer.current);
        timer.current = window.setTimeout(sendPending, RESUME_SAVE_DELAY_MS);
    }, [queue, queueIndex, isPlaying, isRadio, currentSong, serverId, sendPending]);

    useEffect(() => {
        window.addEventListener('pagehide', sendPending);
        return () => {
            window.removeEventListener('pagehide', sendPending);
            sendPending();
        };
    }, [sendPending]);

    const clear = useCallback((media: PlayableMedia) => {
        window.clearTimeout(timer.current);
        timer.current = undefined;
        pending.current = null;
        lastSent.current = null;
        clearedSong.current = media.id;
        const source = media.musicSource;
        void musicService.saveResume({ source: source?.type ?? 'None', sourceId: source?.id, trackIds: [], index: 0 }, media.serverId);
    }, []);

    return { clear };
}
