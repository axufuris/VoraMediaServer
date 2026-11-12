import type { IptvConnectionTestVM, IptvPlaylistRequest, IptvPlaylistVM, IptvSourceType } from '../../../api/Iptv/iptvAdminService';

export interface PasswordDraft {
    value: string;
    clear: boolean;
    editing: boolean;
}

export const EMPTY_PASSWORD: PasswordDraft = { value: '', clear: false, editing: false };

export interface IptvSourceDraft {
    sourceType: IptvSourceType;
    m3uUrl: string;
    serverUrl: string;
    username: string;
    password: PasswordDraft;
    userAgent: string;
    referer: string;
    useProviderGuide: boolean;
}

export type IptvSourceRequestFields = Pick<IptvPlaylistRequest,
    'sourceType' | 'm3uUrl' | 'serverUrl' | 'username' | 'newPassword' | 'clearPassword' | 'userAgent' | 'referer' | 'useProviderGuide'>;

export function emptySourceDraft(): IptvSourceDraft {
    return { sourceType: 'M3u', m3uUrl: '', serverUrl: '', username: '', password: EMPTY_PASSWORD, userAgent: '', referer: '', useProviderGuide: true };
}

export function draftFromPlaylist(playlist: IptvPlaylistVM): IptvSourceDraft {
    return {
        sourceType: playlist.sourceType ?? 'M3u',
        m3uUrl: playlist.m3uUrl ?? '',
        serverUrl: playlist.serverUrl ?? '',
        username: playlist.username ?? '',
        password: EMPTY_PASSWORD,
        userAgent: playlist.userAgent ?? '',
        referer: playlist.referer ?? '',
        useProviderGuide: playlist.useProviderGuide ?? true,
    };
}

export function sourceRequestFields(draft: IptvSourceDraft): IptvSourceRequestFields {
    const isXtream = draft.sourceType === 'Xtream';
    return {
        sourceType: draft.sourceType,
        m3uUrl: isXtream ? '' : draft.m3uUrl.trim(),
        serverUrl: isXtream ? draft.serverUrl.trim() : '',
        username: draft.username.trim(),
        newPassword: draft.password.clear || draft.password.value.length === 0 ? null : draft.password.value,
        clearPassword: draft.password.clear,
        userAgent: draft.userAgent.trim(),
        referer: draft.referer.trim(),
        useProviderGuide: draft.useProviderGuide,
    };
}

export function hasPassword(draft: IptvSourceDraft, passwordIsSet: boolean): boolean {
    if (draft.password.clear) return false;
    return draft.password.value.length > 0 || passwordIsSet;
}

export function missingSourceField(draft: IptvSourceDraft, passwordIsSet: boolean): string | null {
    if (draft.sourceType === 'Xtream') {
        if (!draft.serverUrl.trim()) return 'Enter the server address.';
        if (!draft.username.trim()) return 'Enter the username.';
        if (!hasPassword(draft, passwordIsSet)) return 'Enter the password.';
        return null;
    }
    if (!draft.m3uUrl.trim()) return 'Enter the M3U address.';
    if (hasPassword(draft, passwordIsSet) && !draft.username.trim()) return 'A password needs a username.';
    return null;
}

export function hasAdvancedValues(draft: IptvSourceDraft, passwordIsSet: boolean): boolean {
    return draft.userAgent.trim().length > 0
        || draft.referer.trim().length > 0
        || (draft.sourceType === 'M3u' && (draft.username.trim().length > 0 || passwordIsSet));
}

const MASK = '•••';
const CREDENTIAL_KEYS = 'username|user|usr|login|password|pass|passwd|pwd|token|access_token|auth|auth_token|authtoken|apikey|api_key|api-key|key|secret|signature|sig|hdnts|hdnea|wmsauthsign|md5';
const USER_INFO = /(\b[a-z][a-z0-9+.-]*:\/\/)[^/\s?#@]+@/gi;
const CREDENTIAL_QUERY = new RegExp(`([?&;](?:${CREDENTIAL_KEYS})=)[^&#\\s]*`, 'gi');
const XTREAM_PATH = /(\/(?:live|movie|series|timeshift)\/)[^/\s?#]+\/[^/\s?#]+\//gi;

export function maskUrlCredentials(url: string | null | undefined): string {
    if (!url) return '';
    return url
        .replace(USER_INFO, `$1${MASK}@`)
        .replace(CREDENTIAL_QUERY, `$1${MASK}`)
        .replace(XTREAM_PATH, `$1${MASK}/${MASK}/`);
}

export function hostOf(url: string | null | undefined): string {
    if (!url) return '';
    try {
        return new URL(url.includes('://') ? url : `http://${url}`).host;
    } catch {
        return maskUrlCredentials(url);
    }
}

export function describeSource(playlist: IptvPlaylistVM): string {
    if (playlist.sourceType === 'Xtream') {
        const host = hostOf(playlist.serverUrl);
        return playlist.username ? `Xtream Codes · ${host} · ${playlist.username}` : `Xtream Codes · ${host}`;
    }
    return maskUrlCredentials(playlist.m3uUrl);
}

export function formatAccountDate(value: string | null | undefined): string | null {
    if (!value) return null;
    const date = new Date(value);
    return Number.isNaN(date.getTime()) ? null : date.toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' });
}

export function describeTestResult(result: IptvConnectionTestVM, unit: string): string {
    if (!result.succeeded) return result.error ?? 'The source could not be read.';
    const parts = ['Connected'];
    if (result.accountStatus) parts.push(`account ${result.accountStatus.toLowerCase()}`);
    const expiry = formatAccountDate(result.accountExpiresAt);
    if (expiry) parts.push(`expires ${expiry}`);
    if (result.accountMaxConnections) parts.push(`${result.accountMaxConnections} ${result.accountMaxConnections === 1 ? 'connection' : 'connections'}`);
    parts.push(`${result.channelCount.toLocaleString()} ${unit}`);
    return parts.join(' · ');
}
