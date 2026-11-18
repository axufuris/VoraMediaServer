import type { AuthResponse } from '../api/Auth/authService';
import { StorageKeys, SessionKeys } from './storageKeys';

export function storeAccountSession(serverUrl: string, serverName: string | undefined, auth: AuthResponse): void {
    sessionStorage.setItem(SessionKeys.pendingServerUrl, serverUrl);
    sessionStorage.setItem('pending_server_name', serverName || 'Vora Server');
    sessionStorage.setItem(SessionKeys.pendingUserToken, auth.accessToken);
    sessionStorage.setItem('pending_user_id', auth.userId);

    localStorage.setItem(StorageKeys.accountToken, auth.accessToken);
    localStorage.setItem(StorageKeys.userId, auth.userId);
}
