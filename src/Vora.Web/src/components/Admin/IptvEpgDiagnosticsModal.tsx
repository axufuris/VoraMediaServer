import { useState, useEffect } from 'react';
import { iptvEpgAdminService, type IptvEpgDiagnosticsVM } from '../../api/Iptv/iptvEpgAdminService';
import { Modal, ModalHeader } from '../Common/Modal';

interface Props {
    isOpen: boolean;
    onClose: () => void;
    serverId?: string;
}

export default function IptvEpgDiagnosticsModal({ isOpen, onClose, serverId }: Props) {
    const [data, setData] = useState<IptvEpgDiagnosticsVM | null>(null);
    const [isLoading, setIsLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        if (!isOpen) return;
        setIsLoading(true);
        setError(null);
        iptvEpgAdminService.getDiagnostics(serverId)
            .then(setData)
            .catch(err => {
                console.error('Failed to load diagnostics', err);
                setError('Failed to load diagnostics.');
            })
            .finally(() => setIsLoading(false));
    }, [isOpen, serverId]);

    return (
        <Modal
            isOpen={isOpen}
            onClose={onClose}
            size="4xl"
            zIndex="z-[200]"
            surface="light"
            closeOnBackdropClick
            overlayPadding="p-6"
            cardClassName="p-6 flex flex-col max-h-[90vh] h-full"
        >
            <ModalHeader
                title="EPG Match Diagnostics"
                subtitle={<span className="text-[var(--vora-text-muted)] text-sm">Compare what the M3U calls each channel against what the XMLTV calls them.</span>}
                onClose={onClose}
                bordered={false}
                surface="light"
            />
            <div className="border-b border-[var(--vora-border-subtle)] mb-4" />

            <div className="flex-1 overflow-y-auto pr-2 space-y-6">
                {isLoading && <div className="text-[var(--vora-text-muted)] text-sm">Loading diagnostics…</div>}
                {error && <div className="text-sm text-[var(--vora-danger-text)] bg-[var(--vora-danger-soft)] p-3 rounded-[var(--vora-radius-md)] border border-[var(--vora-danger-500)]/20">{error}</div>}

                {data && (
                    <>
                        <section className="vora-card p-4">
                            <h3 className="text-xs font-bold uppercase tracking-widest text-[var(--vora-text-muted)] mb-3">Channel coverage</h3>
                            <div className="flex items-baseline gap-3 mb-3">
                                <span className={`text-3xl font-semibold ${data.coverage.coverageRate >= 0.5 ? 'text-[var(--vora-success-text)]' : data.coverage.coverageRate >= 0.2 ? 'text-[var(--vora-warning-text)]' : 'text-[var(--vora-danger-text)]'}`}>
                                    {data.coverage.channelsWithEpg.toLocaleString()}
                                </span>
                                <span className="text-[var(--vora-text-muted)]">of {data.coverage.totalChannels.toLocaleString()} channels have EPG data ({(data.coverage.coverageRate * 100).toFixed(1)}%)</span>
                            </div>
                            <p className="text-xs text-[var(--vora-text-muted)] mb-3">Channels below have <span className="text-[var(--vora-danger-text)] font-semibold">no programmes</span> in any active EPG source. If a major channel is listed here, its tvg-id likely needs to be in one of your XMLTV feeds — check the unmatched samples lower down for similar names.</p>
                            {data.coverage.uncoveredSamples.length === 0 ? (
                                <div className="text-xs text-[var(--vora-text-disabled)] italic">All channels have at least one programme. Great.</div>
                            ) : (
                                <div className="max-h-72 overflow-y-auto rounded border border-[var(--vora-border-subtle)]">
                                    <table className="w-full text-xs">
                                        <thead className="sticky top-0 bg-[var(--vora-bg-sunken)]">
                                            <tr className="text-[var(--vora-text-muted)] uppercase tracking-wider">
                                                <th className="text-left px-3 py-2 font-semibold">tvg-id</th>
                                                <th className="text-left px-3 py-2 font-semibold">Display Name</th>
                                                <th className="text-left px-3 py-2 font-semibold">Playlist</th>
                                            </tr>
                                        </thead>
                                        <tbody className="divide-y divide-[var(--vora-border-subtle)]">
                                            {data.coverage.uncoveredSamples.map((c, i) => (
                                                <tr key={i}>
                                                    <td className="py-1.5 px-3 font-mono text-[var(--vora-accent-text)]">{c.externalChannelId || <span className="italic text-[var(--vora-text-disabled)]">(empty)</span>}</td>
                                                    <td className="py-1.5 px-3 text-[var(--vora-text-primary)]">{c.name}</td>
                                                    <td className="py-1.5 px-3 text-[var(--vora-text-muted)]">{c.playlistName}</td>
                                                </tr>
                                            ))}
                                        </tbody>
                                    </table>
                                </div>
                            )}
                        </section>

                        <section>
                            <h3 className="text-xs font-bold uppercase tracking-widest text-[var(--vora-text-muted)] mb-3">M3U channels (tvg-ids in your DB)</h3>
                            <p className="text-xs text-[var(--vora-text-muted)] mb-3">Sample of the first {data.dbSampleIds.length} channels. These are what the XMLTV feeds need to match against.</p>
                            <div className="vora-card p-3 max-h-60 overflow-y-auto">
                                <table className="w-full text-xs">
                                    <thead>
                                        <tr className="text-[var(--vora-text-muted)] uppercase tracking-wider">
                                            <th className="text-left pb-2 font-semibold">tvg-id (ExternalChannelId)</th>
                                            <th className="text-left pb-2 font-semibold">Display Name</th>
                                            <th className="text-left pb-2 font-semibold">Playlist</th>
                                        </tr>
                                    </thead>
                                    <tbody className="divide-y divide-[var(--vora-border-subtle)]">
                                        {data.dbSampleIds.map((c, i) => (
                                            <tr key={i}>
                                                <td className="py-1.5 font-mono text-[var(--vora-accent-text)]">{c.externalChannelId || <span className="italic text-[var(--vora-text-disabled)]">(empty)</span>}</td>
                                                <td className="py-1.5 text-[var(--vora-text-primary)]">{c.name}</td>
                                                <td className="py-1.5 text-[var(--vora-text-muted)]">{c.playlistName}</td>
                                            </tr>
                                        ))}
                                        {data.dbSampleIds.length === 0 && (
                                            <tr><td colSpan={3} className="py-4 text-center text-[var(--vora-text-disabled)]">No channels in DB yet.</td></tr>
                                        )}
                                    </tbody>
                                </table>
                            </div>
                        </section>

                        <section>
                            <h3 className="text-xs font-bold uppercase tracking-widest text-[var(--vora-text-muted)] mb-3">EPG sources (per-source match stats)</h3>
                            <p className="text-xs text-[var(--vora-text-muted)] mb-3">Each source's last parse. If matched is near zero, the XMLTV's channel IDs don't line up with the M3U tvg-ids shown above. Look at the sample unmatched IDs to see what the XMLTV is using.</p>
                            <div className="space-y-3">
                                {data.sources.map(s => (
                                    <div key={s.sourceId} className="vora-card p-3">
                                        <div className="flex items-center justify-between mb-2">
                                            <div>
                                                <span className="font-semibold text-[var(--vora-text-primary)]">{s.name}</span>
                                                {s.syncedAt && <span className="text-xs text-[var(--vora-text-muted)] ml-2">last sync {new Date(s.syncedAt).toLocaleString()}</span>}
                                            </div>
                                            <div className="text-xs text-right">
                                                <div>
                                                    <span className={s.matchedChannels === 0 ? 'text-[var(--vora-danger-text)] font-semibold' : 'text-[var(--vora-accent-text)] font-semibold'}>
                                                        {s.matchedChannels.toLocaleString()} channels
                                                    </span>
                                                    <span className="text-[var(--vora-text-muted)]"> contributed</span>
                                                </div>
                                                <div className="mt-0.5">
                                                    <span className={s.matchedProgrammes === 0 ? 'text-[var(--vora-danger-text)] font-semibold' : 'text-[var(--vora-success-text)] font-semibold'}>
                                                        {s.matchedProgrammes.toLocaleString()} programmes
                                                    </span>
                                                    <span className="text-[var(--vora-text-muted)]"> / {s.totalProgrammes.toLocaleString()} ({(s.matchRate * 100).toFixed(2)}%)</span>
                                                </div>
                                            </div>
                                        </div>
                                        {s.lastError && (
                                            <div className="text-xs text-[var(--vora-danger-text)] mb-2 bg-[var(--vora-danger-soft)] p-2 rounded border border-[var(--vora-danger-500)]/20">
                                                <span className="font-bold">Error:</span> {s.lastError}
                                            </div>
                                        )}
                                        <div className="text-xs text-[var(--vora-text-muted)] mb-1 font-bold uppercase tracking-widest">Sample of XMLTV channel IDs that didn't match</div>
                                        {s.unmatchedSamples.length === 0 ? (
                                            <div className="text-xs text-[var(--vora-text-disabled)] italic">none — all parsed programmes matched, or no data yet</div>
                                        ) : (
                                            <div className="flex flex-wrap gap-1.5">
                                                {s.unmatchedSamples.map((id, i) => (
                                                    <span key={i} className="font-mono text-xs bg-[var(--vora-warning-soft)] text-[var(--vora-warning-text)] px-2 py-0.5 rounded border border-[var(--vora-warning-500)]/20">{id}</span>
                                                ))}
                                            </div>
                                        )}
                                    </div>
                                ))}
                                {data.sources.length === 0 && (
                                    <div className="text-[var(--vora-text-disabled)] text-sm italic">No EPG sources configured.</div>
                                )}
                            </div>
                        </section>

                        <section className="vora-card p-4">
                            <h3 className="text-sm font-semibold text-[var(--vora-text-primary)] mb-2">How to read this</h3>
                            <ul className="text-xs text-[var(--vora-text-secondary)] space-y-1.5 list-disc list-inside">
                                <li>Compare the <span className="text-[var(--vora-accent-text)] font-mono">tvg-id column</span> above against the <span className="text-[var(--vora-warning-text)] font-mono">yellow unmatched IDs</span> below.</li>
                                <li>If they look like the same channel with different formatting (e.g. <span className="font-mono">ComedyCentralEast.us</span> vs <span className="font-mono">Comedy.Central.East</span>), tell me — I'll adjust the matcher.</li>
                                <li>If they look completely different (e.g. M3U has cryptic UUIDs, XMLTV has channel names), your M3U and XMLTV aren't designed to pair — you'll need a different EPG source.</li>
                                <li>Run "Refresh all" on the EPG sources to populate this data.</li>
                            </ul>
                        </section>
                    </>
                )}
            </div>
        </Modal>
    );
}
