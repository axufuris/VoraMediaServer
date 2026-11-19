import type { WebhookConnectionVM } from '../../../api/System/webhookAdminService';
import { formatDate, parseServerDate } from '../../../utils/serverTime';
import { serverVault } from '../../../utils/serverVault';

export const FOCUS_FILL = 'focus-visible:bg-[var(--vora-accent-500)]! focus-visible:text-[var(--vora-accent-contrast)]! focus-visible:outline-none!';

export const FOCUS_FILL_PRIMARY = 'focus-visible:bg-[var(--vora-accent-active)]! focus-visible:text-[var(--vora-accent-contrast)]! focus-visible:outline-none!';

export const FOCUS_CONTRAST_TEXT = 'group-focus-visible:text-[var(--vora-accent-contrast)]!';

export interface ConnectionStatus {
    label: 'Ready' | 'Off';
    tone: 'ok' | 'neutral';
}

export function connectionStatus(connection: Pick<WebhookConnectionVM, 'isEnabled'>): ConnectionStatus {
    return connection.isEnabled ? { label: 'Ready', tone: 'ok' } : { label: 'Off', tone: 'neutral' };
}

export function receiveUrl(receivePath: string, serverId?: string): string {
    const base = (serverId ? serverVault.getServer(serverId)?.url : undefined) || window.location.origin;
    return `${base.replace(/\/+$/, '')}${receivePath}`;
}

export function formatAgo(iso: string, now: number): string {
    const at = parseServerDate(iso);
    if (!at) return '';
    const minutes = Math.floor((now - at.getTime()) / 60_000);
    if (minutes < 1) return 'just now';
    if (minutes < 60) return `${minutes} min ago`;
    const hours = Math.floor(minutes / 60);
    if (hours < 24) return `${hours} h ago`;
    const days = Math.floor(hours / 24);
    if (days < 7) return `${days} d ago`;
    return formatDate(iso);
}

export function lastDeliveryText(connection: Pick<WebhookConnectionVM, 'lastDeliveryAt' | 'lastDeliveryOk'>, now: number): string {
    if (!connection.lastDeliveryAt) return 'Nothing received yet';
    return `Last: ${formatAgo(connection.lastDeliveryAt, now)}, ${connection.lastDeliveryOk === false ? 'failed' : 'ok'}`;
}

export function lastDeliveryFailed(connection: Pick<WebhookConnectionVM, 'lastDeliveryAt' | 'lastDeliveryOk'>): boolean {
    return !!connection.lastDeliveryAt && connection.lastDeliveryOk === false;
}
