import type { DesktopCapabilities, DesktopSettings, MpvStatus, VoraDesktopBridge } from './desktopBridge';

export const MIN_DESKTOP_BRIDGE_VERSION = 1;

export interface DesktopPlaybackState {
    bridge: VoraDesktopBridge | null;
    mpv: MpvStatus | null;
    settings: DesktopSettings | null;
    capabilities: DesktopCapabilities | null;
    builtInPlayerForced: boolean;
}

const initialState: DesktopPlaybackState = { bridge: null, mpv: null, settings: null, capabilities: null, builtInPlayerForced: false };

let state = initialState;
let loading: Promise<void> | null = null;
const listeners = new Set<() => void>();

function update(patch: Partial<DesktopPlaybackState>): void {
    state = { ...state, ...patch };
    for (const listener of listeners) listener();
}

export function getDesktopBridge(): VoraDesktopBridge | null {
    if (typeof window === 'undefined') return null;
    const bridge = window.voraDesktop;
    if (!bridge || typeof bridge.bridgeVersion !== 'number' || bridge.bridgeVersion < MIN_DESKTOP_BRIDGE_VERSION) return null;
    return bridge;
}

export function loadDesktopPlayback(): Promise<void> {
    if (loading) return loading;
    const bridge = getDesktopBridge();
    if (!bridge) {
        loading = Promise.resolve();
        return loading;
    }

    update({ bridge, mpv: bridge.mpv });
    bridge.onMpvChanged(mpv => update({ mpv }));
    loading = Promise.all([bridge.settings.get(), bridge.capabilities()])
        .then(([settings, capabilities]) => update({ settings, capabilities }))
        .catch((error: unknown) => console.error('Could not read the Vora Desktop settings', error));
    return loading;
}

export function getDesktopPlaybackState(): DesktopPlaybackState {
    return state;
}

export function subscribeDesktopPlayback(listener: () => void): () => void {
    listeners.add(listener);
    return () => { listeners.delete(listener); };
}

export function playsVideoInMpv(current: DesktopPlaybackState): boolean {
    return !!current.bridge
        && current.mpv?.available === true
        && current.settings?.useMpvForVideo === true
        && current.capabilities !== null
        && !current.builtInPlayerForced;
}

export function isDesktopPlayback(): boolean {
    return playsVideoInMpv(state);
}

export function desktopPlaybackCapabilities(): DesktopCapabilities | null {
    return isDesktopPlayback() ? state.capabilities : null;
}

export async function updateDesktopSettings(patch: Partial<DesktopSettings>): Promise<DesktopSettings | null> {
    const bridge = state.bridge;
    if (!bridge) return null;
    const settings = await bridge.settings.set(patch);
    update({ settings });
    return settings;
}

export function forceBuiltInPlayer(): void {
    if (!state.builtInPlayerForced) update({ builtInPlayerForced: true });
}

export function releaseBuiltInPlayer(): void {
    if (state.builtInPlayerForced) update({ builtInPlayerForced: false });
}
