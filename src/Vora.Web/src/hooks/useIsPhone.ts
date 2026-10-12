import { useSyncExternalStore } from 'react';

export const PHONE_MEDIA_QUERY = '(width < 48rem)';

const phoneQuery = (): MediaQueryList | null =>
    typeof window.matchMedia === 'function' ? window.matchMedia(PHONE_MEDIA_QUERY) : null;

const subscribe = (onChange: () => void) => {
    const query = phoneQuery();
    query?.addEventListener('change', onChange);
    return () => query?.removeEventListener('change', onChange);
};

const isPhoneNow = () => phoneQuery()?.matches ?? false;

export function useIsPhone(): boolean {
    return useSyncExternalStore(subscribe, isPhoneNow, () => false);
}
