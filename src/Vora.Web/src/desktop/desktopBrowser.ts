export function canOpenInBrowser(): boolean {
    return typeof window !== 'undefined' && typeof window.voraDesktop?.openExternal === 'function';
}

export async function openInBrowser(url: string): Promise<boolean> {
    const bridge = window.voraDesktop;
    if (!bridge?.openExternal) return false;
    try {
        return await bridge.openExternal(url);
    } catch {
        return false;
    }
}
