import type { WebhookConnectionVM } from '../../../api/System/webhookAdminService';
import HealthBadge from '../Primitives/HealthBadge';
import { FOCUS_CONTRAST_TEXT, FOCUS_FILL, connectionStatus, lastDeliveryFailed, lastDeliveryText } from './libraryUpdates';

interface ConnectionTilesProps {
    connections: WebhookConnectionVM[] | null;
    now: number;
    onOpen: (connection: WebhookConnectionVM) => void;
    onAdd: () => void;
}

const TILE = `group flex min-h-[4.75rem] w-full cursor-pointer flex-col gap-2 p-3 text-left ${FOCUS_FILL}`;

export default function ConnectionTiles({ connections, now, onOpen, onAdd }: ConnectionTilesProps) {
    if (connections === null) {
        return <div className="vora-skeleton h-20" />;
    }

    return (
        <ul aria-label="Connections" className="grid grid-cols-[repeat(auto-fill,minmax(14rem,1fr))] gap-3">
            {connections.map(connection => {
                const status = connectionStatus(connection);
                const failed = lastDeliveryFailed(connection);
                return (
                    <li key={connection.id} className="flex">
                        <button type="button" onClick={() => onOpen(connection)} className={`vora-card justify-between ${TILE}`}>
                            <span className="flex items-start justify-between gap-2">
                                <span className="min-w-0">
                                    <span className={`block truncate text-sm font-semibold text-[var(--vora-text-primary)] ${FOCUS_CONTRAST_TEXT}`}>{connection.name}</span>
                                    <span className={`block truncate text-xs text-[var(--vora-text-muted)] ${FOCUS_CONTRAST_TEXT}`}>{connection.providerName}</span>
                                </span>
                                <HealthBadge tone={status.tone} showDot={false} className="shrink-0">{status.label}</HealthBadge>
                            </span>
                            <span className={`block truncate text-xs ${failed ? 'font-semibold text-[var(--vora-danger-text)]' : 'text-[var(--vora-text-secondary)]'} ${FOCUS_CONTRAST_TEXT}`}>
                                {lastDeliveryText(connection, now)}
                            </span>
                        </button>
                    </li>
                );
            })}
            <li className="flex">
                <button
                    type="button"
                    onClick={onAdd}
                    className={`${TILE} items-center justify-center rounded-[var(--vora-radius-lg)] border border-dashed border-[var(--vora-border-strong)] bg-[var(--vora-bg-surface)] text-sm font-semibold text-[var(--vora-text-primary)]`}
                >
                    <span className="flex items-center gap-1.5">
                        <svg aria-hidden="true" viewBox="0 0 24 24" className="h-4 w-4" fill="none" stroke="currentColor" strokeWidth={2.5} strokeLinecap="round">
                            <path d="M12 5v14M5 12h14" />
                        </svg>
                        Add connection
                    </span>
                </button>
            </li>
        </ul>
    );
}
