import { useEffect, useState } from 'react';
import { getVoraPosters, type VoraPosterCatalog } from '../../api/Media/voraPosterService';

interface VoraPostersState {
    catalog: VoraPosterCatalog | null;
    failed: boolean;
}

export function useVoraPosters(serverId: string | undefined, enabled: boolean): VoraPostersState {
    const [state, setState] = useState<VoraPostersState & { serverId: string | undefined }>({ catalog: null, failed: false, serverId });

    useEffect(() => {
        if (!enabled) return;
        let current = true;
        getVoraPosters(serverId)
            .then(catalog => { if (current) setState({ catalog, failed: false, serverId }); })
            .catch(() => { if (current) setState({ catalog: null, failed: true, serverId }); });
        return () => { current = false; };
    }, [serverId, enabled]);

    return state.serverId === serverId ? state : { catalog: null, failed: false };
}
