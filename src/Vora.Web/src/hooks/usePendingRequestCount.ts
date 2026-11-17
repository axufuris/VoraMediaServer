import { useCallback, useEffect, useState } from 'react';
import { requestAdminService } from '../api/Discovery/requestAdminService';
import { useSignalREvent } from './useSignalREvent';

export function usePendingRequestCount(serverId: string | undefined): number {
    const [count, setCount] = useState(0);

    const refresh = useCallback(() => {
        requestAdminService.getPendingCount(serverId)
            .then(setCount)
            .catch(() => { /* the badge keeps what it showed */ });
    }, [serverId]);

    useEffect(() => {
        refresh();
    }, [refresh]);

    useSignalREvent('RequestQueueChanged', refresh);

    return count;
}
