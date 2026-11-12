import { useEffect, useState } from 'react';
import { userService } from '../../api/Users/userService';
import { serverVault } from '../../utils/serverVault';
import { StorageKeys, decodeJwtPayload } from '../../utils/storageKeys';

export interface LivePermissions {
    canTimeshift: boolean;
    canRecord: boolean;
}

const truthy = (value: unknown) => value === true || value === 'True' || value === 'true' || value === '1' || value === 1;

export function readLivePermissions(): LivePermissions {
    const isServerAdmin = localStorage.getItem(StorageKeys.isServerAdmin) === 'true';
    const token = localStorage.getItem(StorageKeys.profileToken);
    if (!token) return { canTimeshift: isServerAdmin, canRecord: isServerAdmin };
    try {
        const payload = decodeJwtPayload(token) ?? {};
        return {
            canTimeshift: isServerAdmin || truthy(payload.canTimeshiftIptv),
            canRecord: isServerAdmin || truthy(payload.canRecordLiveTv),
        };
    } catch {
        return { canTimeshift: isServerAdmin, canRecord: isServerAdmin };
    }
}

export function useLivePermissions(refresh = true): LivePermissions {
    const [permissions, setPermissions] = useState<LivePermissions>(readLivePermissions);

    useEffect(() => {
        if (!refresh) return;
        const userId = localStorage.getItem(StorageKeys.userId);
        if (!userId) return;
        let cancelled = false;
        userService.getUserAccount(userId, serverVault.getActiveServerId() || undefined)
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
