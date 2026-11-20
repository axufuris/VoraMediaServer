import { useCallback, useEffect, useState } from 'react';
import { musicService, type MusicResumeItemVM } from '../../../../api/Music/musicService';
import { useSignalREvent } from '../../../../hooks/useSignalREvent';

export const MUSIC_RESUME_LIMIT = 10;

// The Continue Listening row's items. They load with the page and reload when
// the server says a queue was saved for this profile, here or on another device.
export function useMusicResume(serverId?: string) {
    const [items, setItems] = useState<MusicResumeItemVM[]>([]);

    const reload = useCallback(() => {
        musicService.getResumeItems(MUSIC_RESUME_LIMIT, serverId)
            .then(setItems)
            .catch(err => console.error('Failed to load Continue Listening', err));
    }, [serverId]);

    useEffect(() => { reload(); }, [reload]);
    useSignalREvent<string>('MusicResumeUpdated', reload);

    return { items, reload };
}
