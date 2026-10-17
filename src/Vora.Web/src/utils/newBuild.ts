import { lazy, type ComponentType, type LazyExoticComponent } from 'react';
import { SessionKeys } from './storageKeys';

export const ReloadWindowMs = 10_000;

// After the server is updated, a tab opened before it still asks for the old
// build's page files, which no longer exist. Reloading picks up the new build.
// One reload per window, so a page that keeps failing reaches the error
// boundary instead of reloading forever.
export function reloadForNewBuild(
    now: number = Date.now(),
    storage: Storage = window.sessionStorage,
    reload: () => void = () => window.location.reload(),
): boolean {
    try {
        const last = Number(storage.getItem(SessionKeys.newBuildReloadAt)) || 0;
        if (now - last < ReloadWindowMs) return false;
        storage.setItem(SessionKeys.newBuildReloadAt, String(now));
    } catch {
        return false;
    }
    reload();
    return true;
}

export function lazyPage<P extends object>(
    load: () => Promise<{ default: ComponentType<P> }>,
    reloadOnFailure: () => boolean = () => reloadForNewBuild(),
): LazyExoticComponent<ComponentType<P>> {
    return lazy(() => load().catch((error: Error) => {
        if (reloadOnFailure()) return new Promise<{ default: ComponentType<P> }>(() => { });
        throw error;
    }));
}
