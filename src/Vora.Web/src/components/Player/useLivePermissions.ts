import { useEffect, useState } from 'react';
import { userService } from '../../api/Users/userService';
import { tokenClaimsForServer, userIdForServer } from '../../api/client';
import { serverVault } from '../../utils/serverVault';
import { StorageKeys } from '../../utils/storageKeys';

export interface LivePermissions {
    canTimeshift: boolean;
    canRecord: boolean;
}

const truthy = (value: unknown) => value === true || value === 'True' || value === 'true' || value === '1' || value === 1;

const activeServerId = (): string | undefined => serverVault.getActiveServerId() || undefined;

export function readLivePermissions(): LivePermissions {
    const claims = tokenClaimsForServer(activeServerId());
    const isServerAdmin = claims && 'isAdmin' in claims
        ? truthy(claims.isAdmin)
        : localStorage.getItem(StorageKeys.isServerAdmin) === 'true';
    if (!claims) return { canTimeshift: isServerAdmin, canRecord: isServerAdmin };
    return {
        canTimeshift: isServerAdmin || truthy(claims.canTimeshiftIptv),
        canRecord: isServerAdmin || truthy(claims.canRecordLiveTv),
    };
}

export function useLivePermissions(refresh = true): LivePermissions {
    const [permissions, setPermissions] = useState<LivePermissions>(readLivePermissions);

    useEffect(() => {
        if (!refresh) return;
        const serverId = activeServerId();
        const userId = userIdForServer(serverId);
        if (!userId) return;
        let cancelled = false;
        userService.getUserAccount(userId, serverId)
            .then(u => {
                if (cancelled) return;
                setPermissions({
                    canTimeshift: !!u.canTimeshiftIptv || !!u.isAdmin,
                    canRecord: !!u.canRecordLiveTv || !!u.isAdmin,
                });
            })
            .catch(err => console.warn('Failed to refresh live TV permissions', err));
        return () => { cancelled = true; };
    }, [refresh]);

    return permissions;
}
