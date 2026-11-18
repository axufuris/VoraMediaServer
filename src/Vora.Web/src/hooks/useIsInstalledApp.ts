import { useSyncExternalStore } from 'react';

export const INSTALLED_APP_MEDIA_QUERY = '(display-mode: standalone)';

const installedQuery = (): MediaQueryList | null =>
    typeof window.matchMedia === 'function' ? window.matchMedia(INSTALLED_APP_MEDIA_QUERY) : null;

const subscribe = (onChange: () => void) => {
    const query = installedQuery();
    query?.addEventListener('change', onChange);
    return () => query?.removeEventListener('change', onChange);
};

// Where Vora has no browser around it: an installed web app (the manifest asks
// for standalone; iOS reports it as navigator.standalone) or Vora Desktop's
// window. A browser tab has its own Back button and never counts.
const isInstalledNow = (): boolean => {
    if (typeof window.voraDesktop !== 'undefined') return true;
    if ((navigator as Navigator & { standalone?: boolean }).standalone === true) return true;
    return installedQuery()?.matches ?? false;
};

export function useIsInstalledApp(): boolean {
    return useSyncExternalStore(subscribe, isInstalledNow, () => false);
}
