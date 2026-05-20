import { useState, useEffect, Fragment } from 'react';
import { useParams } from 'react-router-dom';
import { historyService, type HistorySessionDto } from '../../api/Media/historyService';
import { libraryService, type LibrarySummary } from '../../api/Media/libraryService';
import PageHeader from '../../components/Admin/Primitives/PageHeader';

interface DecisionLogEntry {
    Reason?: string;
    [key: string]: unknown;
}

function StrategyTag({ strategy }: { strategy: string }) {
    const tone =
        strategy === 'Transcode' ? 'bg-[var(--vora-accent-500)] text-white' :
        strategy === 'Remux' || strategy === 'Copy' ? 'bg-[var(--vora-warning-500)] text-white' :
        'bg-[var(--vora-border-strong)] text-[var(--vora-text-primary)]';
    const label =
        strategy === 'Transcode' ? 'TR' :
        strategy === 'Remux' || strategy === 'Copy' ? 'DS' :
        'DP';
    return (
        <span title={strategy} className={`px-1 py-0.5 rounded text-[9px] font-bold leading-none ${tone}`}>
            {label}
        </span>
    );
}

function CompletionIcon({ percent }: { percent: number }) {
    if (percent >= 90) {
        return <div className="w-3.5 h-3.5 rounded-full bg-[var(--vora-success-500)] mx-auto" title="Completed" />;
    }
    if (percent >= 40) {
        return (
            <div className="w-3.5 h-3.5 rounded-full border border-[var(--vora-border-strong)] mx-auto relative overflow-hidden" title="In progress">
                <div className="absolute left-0 top-0 bottom-0 w-1/2 bg-[var(--vora-warning-500)]" />
            </div>
        );
    }
    return <div className="w-3.5 h-3.5 rounded-full border-2 border-[var(--vora-border-strong)] mx-auto" title="Barely watched" />;
}

export default function HistoryPage() {
    const { serverId } = useParams<{ serverId?: string }>();
    const [data, setData] = useState<HistorySessionDto[]>([]);
    const [loading, setLoading] = useState(true);
    const [expandedRows, setExpandedRows] = useState<Set<string>>(new Set());
    const [refreshTrigger, setRefreshTrigger] = useState(0);

    const [libraries, setLibraries] = useState<LibrarySummary[]>([]);
    const [activeLibraryIds, setActiveLibraryIds] = useState<Set<string>>(new Set());

    const [selectedUser, setSelectedUser] = useState('All users');
    const [streamFilters, setStreamFilters] = useState({ directPlay: true, directStream: true, transcode: true });
    const [search, setSearch] = useState('');
    const [pageSize, setPageSize] = useState(25);

    useEffect(() => {
        let isMounted = true;
        libraryService.getLibraries(serverId).then(libs => {
            if (isMounted) {
                setLibraries(libs);
                setActiveLibraryIds(new Set(libs.map(l => l.id)));
            }
        }).catch(console.error);
        return () => { isMounted = false; };
    }, [serverId]);

    useEffect(() => {
        let isMounted = true;
        setLoading(true);

        const fetchHistory = async () => {
            try {
                const response = await historyService.getHistory(1, pageSize, search, serverId);
                if (isMounted) setData(response.data);
            } catch (error) {
                console.error('Failed to fetch history:', error);
            } finally {
                if (isMounted) setLoading(false);
            }
        };

        fetchHistory();
        return () => { isMounted = false; };
    }, [pageSize, search, refreshTrigger, serverId]);

    const toggleRow = (id: string) => {
        const newSet = new Set(expandedRows);
        if (newSet.has(id)) newSet.delete(id);
        else newSet.add(id);
        setExpandedRows(newSet);
    };

    const formatLocalDate = (isoString: string) => {
        if (!isoString) return '';
        const d = new Date(isoString);
        return `${(d.getMonth() + 1).toString().padStart(2, '0')}-${d.getDate().toString().padStart(2, '0')}-${d.getFullYear()}`;
    };

    const formatLocalTime = (isoString: string) => {
        if (!isoString) return '';
        return new Date(isoString).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' });
    };

    const renderStreamDetails = (row: HistorySessionDto) => (
        <div className="flex flex-col gap-1 text-[11px]">
            <div className="flex items-center gap-1.5">
                <StrategyTag strategy={row.strategy} />
                <span className="text-[var(--vora-text-muted)] font-bold uppercase w-7 text-[9px]">ALL</span>
                <span className="text-[var(--vora-text-secondary)]">{row.strategy}</span>
            </div>

            <div className="flex items-center gap-1.5">
                <StrategyTag strategy={row.videoStrategy} />
                <span className="text-[var(--vora-text-muted)] font-bold uppercase w-7 text-[9px]">VID</span>
                <span className="text-[var(--vora-text-secondary)] flex items-center gap-1">
                    {row.videoStrategy === 'Transcode' ? (
                        <>
                            <span>{row.originalVideoCodec?.toUpperCase() || 'SOURCE'}</span>
                            <span className="text-[var(--vora-text-disabled)]">→</span>
                            <span>{row.videoCodec?.toUpperCase() || 'UNK'}</span>
                        </>
                    ) : (
                        <span>{row.originalVideoCodec?.toUpperCase() || row.videoCodec?.toUpperCase() || 'UNKNOWN'}</span>
                    )}
                </span>
            </div>

            <div className="flex items-center gap-1.5">
                <StrategyTag strategy={row.audioStrategy} />
                <span className="text-[var(--vora-text-muted)] font-bold uppercase w-7 text-[9px]">AUD</span>
                <span className="text-[var(--vora-text-secondary)] flex items-center gap-1 flex-wrap">
                    {row.audioStrategy === 'Transcode' ? (
                        <>
                            <span>{row.originalAudioCodec?.toUpperCase() || 'SOURCE'}</span>
                            {row.originalAudioChannels !== undefined && <span className="px-1 py-0.5 bg-[var(--vora-bg-sunken)] text-[var(--vora-text-secondary)] text-[9px] font-bold rounded">{row.originalAudioChannels}ch</span>}
                            <span className="text-[var(--vora-text-disabled)]">→</span>
                            <span>{row.audioCodec?.toUpperCase() || 'UNK'}</span>
                            {row.targetAudioChannels !== undefined && <span className="px-1 py-0.5 bg-[var(--vora-info-soft)] text-[var(--vora-info-text)] text-[9px] font-bold rounded">{row.targetAudioChannels}ch</span>}
                        </>
                    ) : (
                        <>
                            <span>{row.originalAudioCodec?.toUpperCase() || row.audioCodec?.toUpperCase() || 'UNKNOWN'}</span>
                            {(row.targetAudioChannels ?? row.originalAudioChannels) !== undefined && <span className="px-1 py-0.5 bg-[var(--vora-bg-sunken)] text-[var(--vora-text-secondary)] text-[9px] font-bold rounded">{(row.targetAudioChannels ?? row.originalAudioChannels)}ch</span>}
                        </>
                    )}
                </span>
            </div>

            {row.subtitleStrategy && row.subtitleStrategy !== 'None' && (
                <div className="flex items-center gap-1.5">
                    <StrategyTag strategy={row.subtitleStrategy === 'BurnIn' ? 'Transcode' : 'DirectPlay'} />
                    <span className="text-[var(--vora-text-muted)] font-bold uppercase w-7 text-[9px]">SUB</span>
                    <span className="text-[var(--vora-text-secondary)]">
                        {row.subtitleStrategy === 'BurnIn' ? (
                            <>
                                <span>{row.originalSubtitleCodec?.toUpperCase() || 'SOURCE'}</span>
                                <span className="text-[var(--vora-text-disabled)] mx-1">→</span>
                                <span>Burn-in</span>
                            </>
                        ) : (
                            <span>{row.originalSubtitleCodec?.toUpperCase() || 'UNKNOWN'}</span>
                        )}
                    </span>
                </div>
            )}

            {row.strategy !== 'DirectPlay' && row.decisionLog && (
                <div className="mt-1.5 pt-1.5 border-t border-[var(--vora-border-subtle)] flex flex-col gap-0.5">
                    {(() => {
                        try {
                            const logs = JSON.parse(row.decisionLog) as DecisionLogEntry[];
                            const firstLog = logs[0];
                            if (!firstLog || !firstLog.Reason) return null;

                            const reasons: string[] = firstLog.Reason.split('. ').filter(r => r.trim().length > 0);

                            return reasons.map((r, i) => (
                                <span key={i} className="text-[var(--vora-accent-text)] text-[10px] font-medium whitespace-normal leading-tight flex gap-1">
                                    <span className="opacity-60">•</span>
                                    <span>{r.trim()}{r.endsWith('.') ? '' : '.'}</span>
                                </span>
                            ));
                        } catch (error) {
                            console.error('Failed to parse decision log JSON:', error);
                            return <span className="text-[var(--vora-accent-text)] text-[10px]">{row.decisionLog}</span>;
                        }
                    })()}
                </div>
            )}
        </div>
    );

    const filteredData = data.filter(row => {
        if (selectedUser !== 'All users' && row.userName !== selectedUser) return false;
        if (!activeLibraryIds.has(row.libraryId)) return false;

        if (row.strategy === 'DirectPlay' && !streamFilters.directPlay) return false;
        if ((row.strategy === 'Copy' || row.strategy === 'Remux' || row.strategy === 'DirectStream') && !streamFilters.directStream) return false;
        if (row.strategy === 'Transcode' && !streamFilters.transcode) return false;

        return true;
    });

    const userOptions = Array.from(new Set(data.map(d => d.userName)));

    return (
        <div data-vora-page="">
            <PageHeader
                title="Watch History"
                description="Every playback session, with transcoding decisions and quality details."
                actions={
                    <button type="button" onClick={() => setRefreshTrigger(prev => prev + 1)} className="vora-button-secondary flex items-center gap-2">
                        <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" /></svg>
                        Refresh
                    </button>
                }
            />

            <div className="p-8 max-w-[1500px] mx-auto space-y-4">
                <div className="vora-card p-4 flex flex-wrap items-center gap-3">
                    <select
                        value={selectedUser}
                        onChange={(e) => setSelectedUser(e.target.value)}
                        className="vora-input !w-auto"
                    >
                        <option value="All users">All users</option>
                        {userOptions.map(user => <option key={user} value={user}>{user}</option>)}
                    </select>

                    <div className="flex border border-[var(--vora-border-strong)] rounded-[var(--vora-radius-md)] overflow-hidden">
                        {libraries.map(lib => {
                            const isActive = activeLibraryIds.has(lib.id);
                            return (
                                <button
                                    key={lib.id}
                                    type="button"
                                    onClick={() => {
                                        const newSet = new Set(activeLibraryIds);
                                        if (isActive) newSet.delete(lib.id);
                                        else newSet.add(lib.id);
                                        setActiveLibraryIds(newSet);
                                    }}
                                    className={`px-3 py-1.5 text-xs font-semibold border-r last:border-r-0 border-[var(--vora-border-strong)] transition-colors cursor-pointer ${isActive ? 'bg-[var(--vora-accent-500)] text-white' : 'bg-[var(--vora-bg-surface)] text-[var(--vora-text-secondary)] hover:bg-[var(--vora-bg-sunken)]'}`}
                                >
                                    {lib.name}
                                </button>
                            );
                        })}
                    </div>

                    <div className="flex border border-[var(--vora-border-strong)] rounded-[var(--vora-radius-md)] overflow-hidden">
                        <button
                            type="button"
                            onClick={() => setStreamFilters(p => ({ ...p, directPlay: !p.directPlay }))}
                            className={`px-3 py-1.5 text-xs font-semibold border-r border-[var(--vora-border-strong)] transition-colors cursor-pointer ${streamFilters.directPlay ? 'bg-[var(--vora-accent-500)] text-white' : 'bg-[var(--vora-bg-surface)] text-[var(--vora-text-secondary)] hover:bg-[var(--vora-bg-sunken)]'}`}
                        >Direct Play</button>
                        <button
                            type="button"
                            onClick={() => setStreamFilters(p => ({ ...p, directStream: !p.directStream }))}
                            className={`px-3 py-1.5 text-xs font-semibold border-r border-[var(--vora-border-strong)] transition-colors cursor-pointer ${streamFilters.directStream ? 'bg-[var(--vora-accent-500)] text-white' : 'bg-[var(--vora-bg-surface)] text-[var(--vora-text-secondary)] hover:bg-[var(--vora-bg-sunken)]'}`}
                        >Direct Stream</button>
                        <button
                            type="button"
                            onClick={() => setStreamFilters(p => ({ ...p, transcode: !p.transcode }))}
                            className={`px-3 py-1.5 text-xs font-semibold transition-colors cursor-pointer ${streamFilters.transcode ? 'bg-[var(--vora-accent-500)] text-white' : 'bg-[var(--vora-bg-surface)] text-[var(--vora-text-secondary)] hover:bg-[var(--vora-bg-sunken)]'}`}
                        >Transcode</button>
                    </div>

                    <div className="ml-auto flex items-center gap-2">
                        <input
                            type="text"
                            value={search}
                            onChange={(e) => setSearch(e.target.value)}
                            placeholder="Search…"
                            className="vora-input !w-64"
                        />
                    </div>
                </div>

                <div className="flex justify-between items-center text-xs text-[var(--vora-text-muted)]">
                    <div className="flex items-center gap-2">
                        <span>Show</span>
                        <select value={pageSize} onChange={(e) => setPageSize(Number(e.target.value))} className="vora-input !w-auto !py-1">
                            <option value={25}>25</option>
                            <option value={50}>50</option>
                            <option value={100}>100</option>
                        </select>
                        <span>entries per page</span>
                    </div>
                </div>

                <div className="vora-card overflow-hidden">
                    <div className="overflow-x-auto min-h-[500px]">
                        <table className="w-full text-left text-sm whitespace-nowrap">
                            <thead className="bg-[var(--vora-bg-sunken)] border-b border-[var(--vora-border-subtle)] text-[var(--vora-text-muted)] text-[11px] uppercase tracking-wider">
                                <tr>
                                    <th className="px-4 py-3 font-semibold w-40">Date & Time</th>
                                    <th className="px-4 py-3 font-semibold">User</th>
                                    <th className="px-4 py-3 font-semibold">IP</th>
                                    <th className="px-4 py-3 font-semibold">Platform</th>
                                    <th className="px-4 py-3 font-semibold">Product</th>
                                    <th className="px-4 py-3 font-semibold">Player</th>
                                    <th className="px-4 py-3 font-semibold w-1/5">Title</th>
                                    <th className="px-4 py-3 font-semibold">Stream</th>
                                    <th className="px-4 py-3 font-semibold text-right">Paused</th>
                                    <th className="px-4 py-3 font-semibold text-right">Stopped</th>
                                    <th className="px-4 py-3 font-semibold text-right">Duration</th>
                                    <th className="px-4 py-3 font-semibold text-center">Status</th>
                                </tr>
                            </thead>
                            <tbody className="divide-y divide-[var(--vora-border-subtle)]">
                                {loading ? (
                                    <tr><td colSpan={12} className="px-4 py-12 text-center text-[var(--vora-text-muted)]">Loading history…</td></tr>
                                ) : filteredData.length === 0 ? (
                                    <tr><td colSpan={12} className="px-4 py-12 text-center text-[var(--vora-text-muted)]">No matching history found.</td></tr>
                                ) : (
                                    filteredData.map(row => (
                                        <Fragment key={row.id}>
                                            <tr className="hover:bg-[var(--vora-bg-sunken)]/40 transition-colors">
                                                <td className="px-4 py-2.5">
                                                    <div className="flex items-center gap-2">
                                                        {row.isGrouped ? (
                                                            <button
                                                                type="button"
                                                                onClick={() => toggleRow(row.id)}
                                                                className="w-4 h-4 flex items-center justify-center bg-[var(--vora-bg-sunken)] hover:bg-[var(--vora-border-strong)] rounded-sm text-[var(--vora-text-primary)] font-bold text-xs cursor-pointer"
                                                            >
                                                                {expandedRows.has(row.id) ? '−' : '+'}
                                                            </button>
                                                        ) : <span className="w-4 inline-block" />}
                                                        <div className="flex flex-col">
                                                            <span className="text-[var(--vora-text-primary)] tabular-nums">{formatLocalDate(row.date)}</span>
                                                            <span className="text-[var(--vora-text-muted)] text-[11px] tabular-nums">{formatLocalTime(row.startedAt)}</span>
                                                        </div>
                                                    </div>
                                                </td>
                                                <td className="px-4 py-2.5 font-semibold text-[var(--vora-text-primary)]">{row.userName}</td>
                                                <td className="px-4 py-2.5 font-mono text-xs text-[var(--vora-text-secondary)]">{row.ipAddress}</td>
                                                <td className="px-4 py-2.5 text-[var(--vora-text-secondary)]">{row.platform}</td>
                                                <td className="px-4 py-2.5 text-[var(--vora-text-secondary)]">{row.product}</td>
                                                <td className="px-4 py-2.5">
                                                    <div className="flex flex-col">
                                                        <span className="text-[var(--vora-text-secondary)]">{row.player}</span>
                                                        <span className="text-[var(--vora-text-muted)] text-[10px] tabular-nums">{(row.bandwidthKbps / 1000).toFixed(1)} Mbps</span>
                                                    </div>
                                                </td>
                                                <td className="px-4 py-2.5 font-medium">
                                                    <span className="text-[var(--vora-text-primary)] truncate max-w-[220px] inline-block align-bottom" title={row.title}>{row.title}</span>
                                                </td>
                                                <td className="px-4 py-2.5">
                                                    {renderStreamDetails(row)}
                                                </td>
                                                <td className="px-4 py-2.5 text-right text-[var(--vora-text-secondary)] tabular-nums">{row.pausedMinutes}m</td>
                                                <td className="px-4 py-2.5 text-right text-[var(--vora-text-secondary)] tabular-nums">{formatLocalTime(row.stoppedAt)}</td>
                                                <td className="px-4 py-2.5 text-right text-[var(--vora-text-secondary)] tabular-nums">{row.durationMinutes}m</td>
                                                <td className="px-4 py-2.5"><CompletionIcon percent={row.percentComplete} /></td>
                                            </tr>

                                            {row.isGrouped && expandedRows.has(row.id) && row.subSessions?.map(sub => (
                                                <tr key={sub.id} className="bg-[var(--vora-bg-sunken)]/40 hover:bg-[var(--vora-bg-sunken)]/70 transition-colors border-l-2 border-l-[var(--vora-border-strong)]">
                                                    <td className="px-4 py-2 pl-10">
                                                        <div className="flex flex-col">
                                                            <span className="text-[var(--vora-text-secondary)] tabular-nums">{formatLocalDate(sub.date)}</span>
                                                            <span className="text-[var(--vora-text-muted)] text-[11px] tabular-nums">{formatLocalTime(sub.startedAt)}</span>
                                                        </div>
                                                    </td>
                                                    <td className="px-4 py-2 text-[var(--vora-text-secondary)]">{sub.userName}</td>
                                                    <td className="px-4 py-2 font-mono text-xs text-[var(--vora-text-muted)]">{sub.ipAddress}</td>
                                                    <td className="px-4 py-2 text-[var(--vora-text-secondary)]">{sub.platform}</td>
                                                    <td className="px-4 py-2 text-[var(--vora-text-muted)]">{sub.product}</td>
                                                    <td className="px-4 py-2">
                                                        <div className="flex flex-col">
                                                            <span className="text-[var(--vora-text-secondary)]">{sub.player}</span>
                                                            <span className="text-[var(--vora-text-muted)] text-[10px] tabular-nums">{(sub.bandwidthKbps / 1000).toFixed(1)} Mbps</span>
                                                        </div>
                                                    </td>
                                                    <td className="px-4 py-2 text-[var(--vora-text-secondary)]">
                                                        <span className="truncate max-w-[220px] inline-block align-bottom" title={sub.title}>{sub.title}</span>
                                                    </td>
                                                    <td className="px-4 py-2">{renderStreamDetails(sub)}</td>
                                                    <td className="px-4 py-2 text-right text-[var(--vora-text-muted)] tabular-nums">{sub.pausedMinutes}m</td>
                                                    <td className="px-4 py-2 text-right text-[var(--vora-text-muted)] tabular-nums">{formatLocalTime(sub.stoppedAt)}</td>
                                                    <td className="px-4 py-2 text-right text-[var(--vora-text-muted)] tabular-nums">{sub.durationMinutes}m</td>
                                                    <td className="px-4 py-2"><CompletionIcon percent={sub.percentComplete} /></td>
                                                </tr>
                                            ))}
                                        </Fragment>
                                    ))
                                )}
                            </tbody>
                        </table>
                    </div>
                </div>
            </div>
        </div>
    );
}
