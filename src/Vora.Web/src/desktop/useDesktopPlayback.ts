import { useEffect, useSyncExternalStore } from 'react';
import { getDesktopPlaybackState, loadDesktopPlayback, subscribeDesktopPlayback, type DesktopPlaybackState } from './desktopPlayback';

export function useDesktopPlayback(): DesktopPlaybackState {
    useEffect(() => { void loadDesktopPlayback(); }, []);
    return useSyncExternalStore(subscribeDesktopPlayback, getDesktopPlaybackState, getDesktopPlaybackState);
}
