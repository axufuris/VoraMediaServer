import type { DiscoveryRequestStatus } from '../../api/Discovery/discoveryService';

interface DiscoveryStatusBadgeProps {
    inLibrary?: boolean;
    requestStatus?: DiscoveryRequestStatus | null;
}

export default function DiscoveryStatusBadge({ inLibrary, requestStatus }: DiscoveryStatusBadgeProps) {
    if (inLibrary) {
        return (
            <span
                className="inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wider"
                style={{ background: 'var(--vora-accent-500)', color: 'var(--vora-accent-contrast)' }}
                title="Already in your library"
            >
                <svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="3"><path strokeLinecap="round" strokeLinejoin="round" d="m5 13 4 4L19 7" /></svg>
                In Library
            </span>
        );
    }

    if (requestStatus) {
        const label = requestStatus === 'Available' ? 'Available' : 'Requested';
        return (
            <span
                className="inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wider"
                style={{ background: 'var(--vora-info-soft)', color: 'var(--vora-info-text)' }}
                title={`Request status: ${requestStatus}`}
            >
                <svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5"><circle cx="12" cy="12" r="9" /><path strokeLinecap="round" strokeLinejoin="round" d="M12 7v5l3 2" /></svg>
                {label}
            </span>
        );
    }

    return null;
}
