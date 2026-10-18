import { useEffect, useState } from 'react';
import { profileService, type EpisodeLayout } from '../../../api/Users/profileService';

// How the profile wants a season's episodes drawn, from its playback
// preferences. Null until the server answers, so a profile that picked the list
// doesn't see the row first. A server too old to know the setting gets the row.
export function useEpisodeLayout(serverId?: string): EpisodeLayout | null {
    const [layout, setLayout] = useState<{ serverId?: string; value: EpisodeLayout } | null>(null);

    useEffect(() => {
        let cancelled = false;
        profileService.getMyPlaybackPreferences(serverId)
            .then(prefs => { if (!cancelled) setLayout({ serverId, value: prefs.episodeLayout ?? 'Row' }); })
            .catch(() => { if (!cancelled) setLayout({ serverId, value: 'Row' }); });
        return () => { cancelled = true; };
    }, [serverId]);

    return layout && layout.serverId === serverId ? layout.value : null;
}
