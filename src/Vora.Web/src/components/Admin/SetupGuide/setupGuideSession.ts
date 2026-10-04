import { SessionKeys } from '../../../utils/storageKeys';

const keyFor = (serverId?: string) => SessionKeys.setupGuidePrompted(serverId ?? 'local');

export function wasPromptedThisSession(serverId?: string): boolean {
    try {
        return sessionStorage.getItem(keyFor(serverId)) === 'true';
    } catch {
        return false;
    }
}

export function rememberPromptedThisSession(serverId?: string): void {
    try {
        sessionStorage.setItem(keyFor(serverId), 'true');
    } catch {
        /* storage unavailable — the prompt may show again this session */
    }
}
