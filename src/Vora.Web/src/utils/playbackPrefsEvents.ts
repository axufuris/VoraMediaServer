const PLAYBACK_PREFS_CHANGED = 'vora:playback-prefs-changed';
const PLAYBACK_PREFS_KEY_PREFIX = 'playback_prefs_';

export function announcePlaybackPrefsChanged(): void {
    window.dispatchEvent(new Event(PLAYBACK_PREFS_CHANGED));
}

export function onPlaybackPrefsChanged(listener: () => void): () => void {
    const onStorage = (event: StorageEvent) => {
        if (event.key === null || event.key.startsWith(PLAYBACK_PREFS_KEY_PREFIX)) listener();
    };
    window.addEventListener(PLAYBACK_PREFS_CHANGED, listener);
    window.addEventListener('storage', onStorage);
    return () => {
        window.removeEventListener(PLAYBACK_PREFS_CHANGED, listener);
        window.removeEventListener('storage', onStorage);
    };
}
