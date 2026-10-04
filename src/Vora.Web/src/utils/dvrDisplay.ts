import { parseServerDate } from './serverTime';

// Recording statuses as the server names them (IptvRecordingSessionStatus).
export type DvrTabKey = 'Completed' | 'Upcoming' | 'Failed';

export const isDvrProcessing = (status: string): boolean =>
    status === 'PostProcessing' || status === 'DetectingCommercials';

export const isDvrPlayable = (status: string): boolean => status === 'Completed';

export function dvrTabFor(status: string): DvrTabKey {
    if (status === 'Pending' || status === 'Recording') return 'Upcoming';
    if (isDvrPlayable(status) || isDvrProcessing(status)) return 'Completed';
    return 'Failed';
}

const LABELS: Record<string, string> = {
    Pending: 'Scheduled',
    Recording: 'Recording',
    PostProcessing: 'Processing',
    DetectingCommercials: 'Finding commercials',
    Completed: 'Completed',
    Failed: 'Failed',
    Conflict: 'Conflict',
    Cancelled: 'Cancelled',
};

export const dvrStatusLabel = (status: string): string => LABELS[status] ?? status;

export function dvrStatusClasses(status: string): string {
    if (status === 'Recording') return 'text-[var(--vora-danger-text)] bg-[var(--vora-danger-soft)] border-[var(--vora-danger-500)]';
    if (status === 'Pending') return 'text-[var(--vora-info-text)] bg-[var(--vora-info-soft)] border-[var(--vora-info-500)]';
    if (isDvrPlayable(status)) return 'text-[var(--vora-success-text)] bg-[var(--vora-success-soft)] border-[var(--vora-success-500)]';
    if (isDvrProcessing(status)) return 'text-[var(--vora-warning-text)] bg-[var(--vora-warning-soft)] border-[var(--vora-warning-500)]';
    return 'text-[var(--vora-text-muted)] bg-[var(--vora-bg-raised)] border-[var(--vora-border-subtle)]';
}

export const formatAiredAt = (dateStr: string): string =>
    (parseServerDate(dateStr) ?? new Date(0)).toLocaleString([], { weekday: 'short', month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' });
