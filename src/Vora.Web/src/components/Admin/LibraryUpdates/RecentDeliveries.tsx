import { type WebhookDeliveryVM, type WebhookProviderVM } from '../../../api/System/webhookAdminService';
import { formatDateTime } from '../../../utils/serverTime';
import { FOCUS_FILL, providerTitle } from './libraryUpdates';

interface RecentDeliveriesProps {
    deliveries: WebhookDeliveryVM[] | null;
    providers: WebhookProviderVM[];
    refreshing: boolean;
    onRefresh: () => void;
}

const TH = 'py-3 text-left';
const TD = 'py-3';
const FIRST = 'pl-6 pr-3 max-md:pl-4';
const MID = 'pr-3';
const LAST = 'pr-6 max-md:pr-4';

function OutcomeMarker({ ok }: { ok: boolean }) {
    return (
        <span className={`inline-flex items-center gap-1 font-semibold ${ok ? 'text-[var(--vora-success-text)]' : 'text-[var(--vora-danger-text)]'}`}>
            <svg aria-hidden="true" viewBox="0 0 24 24" className="h-4 w-4 shrink-0" fill="none" stroke="currentColor" strokeWidth={2.5} strokeLinecap="round" strokeLinejoin="round">
                {ok ? <path d="M5 13l4 4L19 7" /> : <path d="M6 18L18 6M6 6l12 12" />}
            </svg>
            {ok ? 'OK' : 'Failed'}
        </span>
    );
}

export default function RecentDeliveries({ deliveries, providers, refreshing, onRefresh }: RecentDeliveriesProps) {
    const nameOf = (providerId: string) => {
        const provider = providers.find(p => p.id === providerId);
        return provider ? providerTitle(provider.name) : providerId;
    };

    return (
        <section className="vora-card overflow-hidden p-0" aria-labelledby="recent-deliveries-title">
            <header className="flex flex-wrap items-center justify-between gap-3 border-b border-[var(--vora-border-subtle)] px-6 py-4 max-md:px-4">
                <div className="min-w-0">
                    <h2 id="recent-deliveries-title" className="text-base font-semibold text-[var(--vora-text-primary)]">Recent deliveries</h2>
                    <p className="mt-0.5 text-xs text-[var(--vora-text-muted)]">What Vora last received, newest first. The list clears when the server restarts.</p>
                </div>
                <button type="button" onClick={onRefresh} disabled={refreshing} className={`vora-button-secondary text-xs disabled:opacity-50 ${FOCUS_FILL}`}>
                    {refreshing ? 'Refreshing…' : 'Refresh'}
                </button>
            </header>

            {deliveries === null ? (
                <div className="p-6"><div className="vora-skeleton h-24" /></div>
            ) : deliveries.length === 0 ? (
                <div className="p-8 text-center text-sm text-[var(--vora-text-muted)]">
                    Nothing received yet. Press Test in Radarr, Sonarr or Lidarr.
                </div>
            ) : (
                <div className="overflow-x-auto">
                    <table className="w-full text-sm max-md:min-w-[44rem]">
                        <thead>
                            <tr className="border-b border-[var(--vora-border-subtle)] text-[10px] font-bold uppercase tracking-wider text-[var(--vora-text-muted)]">
                                <th scope="col" className={`${TH} ${FIRST}`}>Time</th>
                                <th scope="col" className={`${TH} ${MID}`}>Source</th>
                                <th scope="col" className={`${TH} ${MID}`}>Event</th>
                                <th scope="col" className={`${TH} ${MID}`}>Path</th>
                                <th scope="col" className={`${TH} ${LAST}`}>Outcome</th>
                            </tr>
                        </thead>
                        <tbody>
                            {deliveries.map(delivery => (
                                <tr key={delivery.id} className="border-b border-[var(--vora-border-subtle)] last:border-b-0">
                                    <td className={`${TD} ${FIRST} whitespace-nowrap text-[var(--vora-text-secondary)]`}>{formatDateTime(delivery.at)}</td>
                                    <td className={`${TD} ${MID} whitespace-nowrap text-[var(--vora-text-primary)]`}>
                                        {nameOf(delivery.providerId)}
                                        {delivery.instance && <span className="text-[var(--vora-text-muted)]"> ({delivery.instance})</span>}
                                    </td>
                                    <td className={`${TD} ${MID} whitespace-nowrap text-[var(--vora-text-secondary)]`}>{delivery.event}</td>
                                    <td className={`${TD} ${MID} font-mono text-xs text-[var(--vora-text-secondary)]`}>
                                        <div className="max-w-[24rem] truncate" title={delivery.path}>{delivery.path}</div>
                                    </td>
                                    <td className={`${TD} ${LAST}`}>
                                        <div className="flex flex-col gap-0.5">
                                            <OutcomeMarker ok={delivery.ok} />
                                            <span className="text-xs text-[var(--vora-text-muted)]">{delivery.outcome}</span>
                                        </div>
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </div>
            )}
        </section>
    );
}
