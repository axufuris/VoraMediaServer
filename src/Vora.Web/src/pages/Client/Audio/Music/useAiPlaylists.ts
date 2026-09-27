import { useCallback, useEffect, useState } from 'react';
import { aiPlaylistService, type AiPlaylistsVM } from '../../../../api/Music/aiPlaylistService';

export function useAiPlaylists(serverId?: string) {
    const [data, setData] = useState<AiPlaylistsVM | null>(null);

    const reload = useCallback(() => {
        aiPlaylistService.get(serverId).then(setData).catch(() => setData(null));
    }, [serverId]);

    useEffect(() => { reload(); }, [reload]);

    return { data, reload };
}
