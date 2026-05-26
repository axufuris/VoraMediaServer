import { useCallback, useEffect, useRef, useState } from 'react';
import { youtubeService } from '../api/YouTube/youtubeService';
import { useSignalREvent } from './useSignalREvent';

export interface YouTubeWatchedSet {
    has: (videoId: string) => boolean;
    refresh: () => Promise<void>;
}

export function useYouTubeWatchedSet(serverId?: string): YouTubeWatchedSet {
    const [watched, setWatched] = useState<Set<string>>(() => new Set());

    const refresh = useCallback(async () => {
        try {
            const history = await youtubeService.getHistory(serverId);
            const next = new Set<string>();
            for (const entry of history) {
                next.add(entry.videoId);
            }
            setWatched(next);
        } catch {
            setWatched(new Set());
        }
    }, [serverId]);

    const refreshRef = useRef(refresh);
    useEffect(() => {
        refreshRef.current = refresh;
    });

    useEffect(() => {
        void refreshRef.current();
    }, [serverId]);

    useSignalREvent<string>('YouTubeAccessChanged', useCallback(() => {
        void refreshRef.current();
    }, []));

    return {
        has: (videoId: string) => watched.has(videoId),
        refresh
    };
}
