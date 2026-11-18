import type { WebhookProviderVM } from '../../../api/System/webhookAdminService';

export const FOCUS_FILL = 'focus-visible:bg-[var(--vora-accent-500)]! focus-visible:text-[var(--vora-accent-contrast)]! focus-visible:outline-none!';

export function providerTitle(name: string): string {
    const title = name.replace(/\bwebhook\b/gi, '').replace(/\s+/g, ' ').trim();
    return title || name;
}

export interface ProviderStatus {
    label: 'Off' | 'Needs a secret' | 'Ready';
    tone: 'neutral' | 'warn' | 'ok';
}

export function providerStatus(provider: Pick<WebhookProviderVM, 'isEnabled' | 'isConfigured'>): ProviderStatus {
    if (!provider.isEnabled) return { label: 'Off', tone: 'neutral' };
    if (!provider.isConfigured) return { label: 'Needs a secret', tone: 'warn' };
    return { label: 'Ready', tone: 'ok' };
}

export function receiveUrl(receivePath: string): string {
    return `${window.location.origin}${receivePath}`;
}
