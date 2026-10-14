import { useCallback, useMemo, useRef } from 'react';
import { streamingService } from '../api/Streaming/streamingService';
import { StorageKeys } from '../utils/storageKeys';

export interface PlaybackChoice {
    mediaPartId: string;
    videoTrackId: string;
    audioTrackId: string;
    subtitleTrackId: string | null;
    pickReplaced: boolean;
}

export interface PinnedVersion {
    mediaPartId: string;
    videoTrackId: string;
    subtitleTrackId?: string;
}

// Asks the server which version, audio track and subtitle it would play,
// including anything this profile picked for the title on this device, so the
// page shows what will actually play instead of a local guess. Only the answer to
// the newest question is returned; an older one resolves to null, and so does
// a failed request, which leaves the caller on its local pick.
export function useServerPlaybackChoice(serverId?: string) {
    const newest = useRef(0);

    const forget = useCallback(() => {
        newest.current += 1;
    }, []);

    const ask = useCallback(async (mediaId: string, pinned?: PinnedVersion): Promise<PlaybackChoice | null> => {
        const deviceId = localStorage.getItem(StorageKeys.deviceId);
        if (!deviceId) return null;

        newest.current += 1;
        const question = newest.current;
        try {
            const decision = await streamingService.previewDecision(mediaId, deviceId, {
                serverId,
                mediaPartId: pinned?.mediaPartId,
                videoTrackId: pinned?.videoTrackId,
                subtitleTrackId: pinned?.subtitleTrackId,
            });
            if (question !== newest.current) return null;
            return {
                mediaPartId: decision.mediaPartId, videoTrackId: decision.videoTrackId, audioTrackId: decision.audioTrackId,
                subtitleTrackId: decision.subtitleTrackId, pickReplaced: decision.pickReplaced === true,
            };
        } catch {
            return null;
        }
    }, [serverId]);

    return useMemo(() => ({ ask, forget }), [ask, forget]);
}
