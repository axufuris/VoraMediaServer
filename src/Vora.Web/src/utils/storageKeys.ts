export const StorageKeys = {
    deviceId: 'device_id',
    profileToken: 'profile_token',
    accountToken: 'account_token',
    userId: 'user_id',
    profileName: 'profile_name',
    isServerAdmin: 'is_server_admin',
    isProfileAdmin: 'is_profile_admin',
    autoLoginProfileId: 'auto_login_profile_id',
    spotlight: (profileId: string) => `vora_show_spotlight_${profileId}`,
    iptvPrefs: (profileId: string, deviceId: string) => `iptv_prefs_${profileId}_${deviceId}`,
} as const;

export const SessionKeys = {
    pendingServerUrl: 'pending_server_url',
    pendingUserToken: 'pending_user_token',
    musicNavState: 'music_nav_state',
    musicNavProfile: 'music_nav_profile',
    freshServerSetup: 'fresh_server_setup',
} as const;

interface JwtPayload {
    sub?: string;
    [key: string]: unknown;
}

export function decodeJwtPayload(token: string | null | undefined): JwtPayload | null {
    if (!token) return null;
    try {
        const parts = token.split('.');
        if (parts.length !== 3) return null;
        const padded = parts[1].replace(/-/g, '+').replace(/_/g, '/');
        const payload = JSON.parse(atob(padded));
        return payload as JwtPayload;
    } catch {
        return null;
    }
}

export function getProfileIdFromToken(token: string | null | undefined): string | null {
    const payload = decodeJwtPayload(token);
    return typeof payload?.sub === 'string' ? payload.sub : null;
}
