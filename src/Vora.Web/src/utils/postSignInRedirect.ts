import { SessionKeys } from './storageKeys';

const MAX_AGE_MS = 10 * 60 * 1000;

interface Remembered {
    path: string;
    savedAt: number;
}

function isAppPath(path: string): boolean {
    return path.startsWith('/') && !path.startsWith('//') && !path.includes('\\');
}

function isRemembered(value: Remembered | null): value is Remembered {
    return value !== null && typeof value.path === 'string' && typeof value.savedAt === 'number';
}

export function rememberPostSignIn(path: string, now: number = Date.now()): void {
    if (!isAppPath(path)) return;
    try {
        sessionStorage.setItem(SessionKeys.postSignInRedirect, JSON.stringify({ path, savedAt: now } satisfies Remembered));
    } catch {
        return;
    }
}

export function peekPostSignIn(now: number = Date.now()): string | null {
    try {
        const raw = sessionStorage.getItem(SessionKeys.postSignInRedirect);
        if (!raw) return null;
        const parsed: Remembered | null = JSON.parse(raw);
        if (!isRemembered(parsed) || !isAppPath(parsed.path) || now - parsed.savedAt > MAX_AGE_MS) return null;
        return parsed.path;
    } catch {
        return null;
    }
}

export function clearPostSignIn(): void {
    try {
        sessionStorage.removeItem(SessionKeys.postSignInRedirect);
    } catch {
        return;
    }
}
