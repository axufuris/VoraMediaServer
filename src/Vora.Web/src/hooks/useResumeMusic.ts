import { useCallback } from 'react';
import { musicService, type MusicResumeItemVM, type MusicSource } from '../api/Music/musicService';
import { usePlayer } from '../contexts/usePlayer';
import { artistTrackPlayable } from '../utils/playables';

// Starts a Continue Listening tile: the saved queue, from the saved song,
// tagged with its source so what plays next records it. False when there was
// nothing left to play, so the caller can refresh the row.
export function useResumeMusic(serverId?: string) {
    const { playQueue } = usePlayer();

    return useCallback(async (item: MusicResumeItemVM): Promise<boolean> => {
        const queue = await musicService.getResumeQueue(item.source, item.sourceId ?? undefined, serverId);
        if (!queue || queue.tracks.length === 0) return false;

        const source: MusicSource | undefined = item.source !== 'None' && item.sourceId ? { type: item.source, id: item.sourceId } : undefined;
        playQueue(queue.tracks.map(track => artistTrackPlayable(track, serverId, source)), queue.index);
        return true;
    }, [serverId, playQueue]);
}
