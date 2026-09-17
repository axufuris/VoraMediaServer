import { useState, useEffect, useMemo, useCallback } from 'react';
import { useParams } from 'react-router-dom';
import {
    musicService,
    type AdminMusicHistoryRowVM,
    type AdminMusicSummaryVM,
    type AdminMusicHistoryQuery,
} from '../../api/Music/musicService';
import { userService, type UserVM } from '../../api/Users/userService';
import PageHeader from '../../components/Admin/Primitives/PageHeader';
import StatCard from '../../components/Admin/Primitives/StatCard';
import ListCard from '../../components/Admin/Primitives/ListCard';

interface ProfileOption {
    id: string;
    name: string;
}

const PAGE_SIZES = [25, 50, 100, 200];

export default function MusicHistoryPage() {
    const { serverId } = useParams<{ serverId?: string }>();

    const [profiles, setProfiles] = useState<ProfileOption[]>([]);
    const [rows, setRows] = useState<AdminMusicHistoryRowVM[]>([]);
    const [summary, setSummary] = useState<AdminMusicSummaryVM | null>(null);
    const [total, setTotal] = useState(0);

    const [profileFilter, setProfileFilter] = useState<string>('');
    const [fromDate, setFromDate] = useState<string>('');
    const [toDate, setToDate] = useState<string>('');
    const [search, setSearch] = useState<string>('');
    const [page, setPage] = useState<number>(1);
    const [pageSize, setPageSize] = useState<number>(50);

    const [loading, setLoading] = useState(false);
    const [summaryLoading, setSummaryLoading] = useState(false);
    const [refreshTick, setRefreshTick] = useState(0);

    useEffect(() => {
        let mounted = true;
        userService.getAllUsers(serverId)
            .then((users: UserVM[]) => {
                if (!mounted) return;
                const opts: ProfileOption[] = [];
                for (const u of users) {
                    for (const p of u.profiles || []) {
                        opts.push({ id: p.id, name: p.name });
                    }
                }
                opts.sort((a, b) => a.name.localeCompare(b.name));
                setProfiles(opts);
            })
            .catch(console.error);
        return () => { mounted = false; };
    }, [serverId]);

    const buildHistoryQuery = useCallback((): AdminMusicHistoryQuery => {
        const q: AdminMusicHistoryQuery = { page, pageSize };
        if (profileFilter) q.profileId = profileFilter;
        if (fromDate) q.from = new Date(fromDate).toISOString();
        if (toDate) {
            const end = new Date(toDate);
            end.setHours(23, 59, 59, 999);
            q.to = end.toISOString();
        }
        if (search.trim().length > 0) q.search = search.trim();
        return q;
    }, [page, pageSize, profileFilter, fromDate, toDate, search]);

    useEffect(() => {
        let mounted = true;
        queueMicrotask(() => { if (mounted) setLoading(true); });
        musicService.getAdminMusicHistory(buildHistoryQuery(), serverId)
            .then(res => {
                if (!mounted) return;
                setRows(res.rows);
                setTotal(res.total);
            })
            .catch(err => {
                console.error(err);
                if (mounted) {
                    setRows([]);
                    setTotal(0);
                }
            })
            .finally(() => { if (mounted) setLoading(false); });
        return () => { mounted = false; };
    }, [buildHistoryQuery, serverId, refreshTick]);

    useEffect(() => {
        let mounted = true;
        queueMicrotask(() => { if (mounted) setSummaryLoading(true); });
        const params: { from?: string; to?: string } = {};
        if (fromDate) params.from = new Date(fromDate).toISOString();
        if (toDate) {
            const end = new Date(toDate);
            end.setHours(23, 59, 59, 999);
            params.to = end.toISOString();
        }
        musicService.getAdminMusicSummary(params, serverId)
            .then(res => { if (mounted) setSummary(res); })
            .catch(err => {
                console.error(err);
                if (mounted) setSummary(null);
            })
            .finally(() => { if (mounted) setSummaryLoading(false); });
        return () => { mounted = false; };
    }, [fromDate, toDate, serverId, refreshTick]);

    const totalPages = useMemo(() => Math.max(1, Math.ceil(total / pageSize)), [total, pageSize]);

    const formatPlayedAt = (iso: string): string => {
        const d = new Date(iso);
        return `${(d.getMonth() + 1).toString().padStart(2, '0')}/${d.getDate().toString().padStart(2, '0')}/${d.getFullYear()} ${d.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' })}`;
    };

    const formatDuration = (seconds: number): string => {
        if (!seconds || seconds < 0) return '0s';
        const m = Math.floor(seconds / 60);
        const s = seconds % 60;
        if (m === 0) return `${s}s`;
        return `${m}m ${s}s`;
    };

    const clearFilters = () => {
        setProfileFilter('');
        setFromDate('');
        setToDate('');
        setSearch('');
        setPage(1);
    };

    const maxPlaysPerProfile = useMemo(() => {
        if (!summary || summary.playsPerProfile.length === 0) return 1;
        return Math.max(...summary.playsPerProfile.map(p => p.playCount));
    }, [summary]);

    return (
        <div data-vora-page="">
            <PageHeader
                title="Music History"
                description="Listening sessions, top tracks, and per-profile play counts."
                actions={
                    <button
                        type="button"
                        onClick={() => setRefreshTick(t => t + 1)}
                        className="vora-button-secondary flex items-center gap-2"
                    >
                        <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" /></svg>
                        Refresh
                    </button>
                }
            />

            <div className="p-8 max-w-[1400px] mx-auto space-y-6">
                <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
                    <StatCard
                        label="Total Plays"
                        value={summaryLoading ? '—' : (summary?.totalPlays ?? 0).toLocaleString()}
                    />
                    <StatCard
                        label="Active Listeners"
                        value={summaryLoading ? '—' : (summary?.distinctProfileCount ?? 0)}
                    />
                    <StatCard
                        label="Top Track"
                        value={summaryLoading ? '—' : (summary?.topTracks[0]?.trackTitle ?? '—')}
                        footer={
                            <div className="text-xs text-[var(--vora-text-muted)] truncate">
                                {summary?.topTracks[0] ? `${summary.topTracks[0].artist ?? 'Unknown'} · ${summary.topTracks[0].playCount} plays` : ''}
                            </div>
                        }
                    />
                    <StatCard
                        label="Top Artist"
                        value={summaryLoading ? '—' : (summary?.topArtists[0]?.artistName ?? '—')}
                        footer={
                            <div className="text-xs text-[var(--vora-text-muted)]">
                                {summary?.topArtists[0] ? `${summary.topArtists[0].playCount} plays` : ''}
                            </div>
                        }
                    />
                </div>

                <div className="grid grid-cols-1 lg:grid-cols-3 gap-4">
                    <ListCard title="Top Tracks" maxBodyHeight="380px">
                        {summaryLoading ? (
                            <div className="p-4 text-xs text-[var(--vora-text-muted)]">Loading…</div>
                        ) : !summary || summary.topTracks.length === 0 ? (
                            <div className="p-4 text-xs text-[var(--vora-text-muted)]">No plays in range.</div>
                        ) : (
                            <ol className="divide-y divide-[var(--vora-border-subtle)]">
                                {summary.topTracks.slice(0, 10).map((t, i) => (
                                    <li key={t.trackId} className="flex items-center gap-3 px-5 py-2 text-xs hover:bg-[var(--vora-bg-sunken)]/40 transition-colors">
                                        <span className="text-[var(--vora-text-muted)] w-5 text-right tabular-nums">{i + 1}.</span>
                                        {t.albumArtworkUrl ? (
                                            <img src={t.albumArtworkUrl} alt="" className="w-8 h-8 rounded object-cover shrink-0" />
                                        ) : (
                                            <div className="w-8 h-8 rounded bg-[var(--vora-bg-sunken)] shrink-0" />
                                        )}
                                        <div className="flex-1 min-w-0">
                                            <div className="text-[var(--vora-text-primary)] font-semibold truncate" title={t.trackTitle}>{t.trackTitle}</div>
                                            <div className="text-[var(--vora-text-muted)] truncate">{t.artist ?? 'Unknown Artist'}</div>
                                        </div>
                                        <span className="text-[var(--vora-text-secondary)] font-mono tabular-nums">{t.playCount}</span>
                                    </li>
                                ))}
                            </ol>
                        )}
                    </ListCard>

                    <ListCard title="Top Artists" maxBodyHeight="380px">
                        {summaryLoading ? (
                            <div className="p-4 text-xs text-[var(--vora-text-muted)]">Loading…</div>
                        ) : !summary || summary.topArtists.length === 0 ? (
                            <div className="p-4 text-xs text-[var(--vora-text-muted)]">No plays in range.</div>
                        ) : (
                            <ol className="divide-y divide-[var(--vora-border-subtle)]">
                                {summary.topArtists.slice(0, 10).map((a, i) => (
                                    <li key={a.artistId} className="flex items-center gap-3 px-5 py-2 text-xs hover:bg-[var(--vora-bg-sunken)]/40 transition-colors">
                                        <span className="text-[var(--vora-text-muted)] w-5 text-right tabular-nums">{i + 1}.</span>
                                        {a.artworkUrl ? (
                                            <img src={a.artworkUrl} alt="" className="w-8 h-8 rounded-full object-cover shrink-0" />
                                        ) : (
                                            <div className="w-8 h-8 rounded-full bg-[var(--vora-bg-sunken)] shrink-0" />
                                        )}
                                        <div className="flex-1 min-w-0">
                                            <div className="text-[var(--vora-text-primary)] font-semibold truncate" title={a.artistName}>{a.artistName}</div>
                                        </div>
                                        <span className="text-[var(--vora-text-secondary)] font-mono tabular-nums">{a.playCount}</span>
                                    </li>
                                ))}
                            </ol>
                        )}
                    </ListCard>

                    <ListCard title="Plays per Profile" maxBodyHeight="380px">
                        {summaryLoading ? (
                            <div className="p-4 text-xs text-[var(--vora-text-muted)]">Loading…</div>
                        ) : !summary || summary.playsPerProfile.length === 0 ? (
                            <div className="p-4 text-xs text-[var(--vora-text-muted)]">No plays in range.</div>
                        ) : (
                            <ul className="px-5 py-3 space-y-3">
                                {summary.playsPerProfile.slice(0, 10).map(p => {
                                    const widthPct = Math.max(4, (p.playCount / maxPlaysPerProfile) * 100);
                                    return (
                                        <li key={p.profileId} className="text-xs">
                                            <div className="flex justify-between mb-1">
                                                <span className="text-[var(--vora-text-primary)] font-semibold truncate">{p.profileName}</span>
                                                <span className="text-[var(--vora-text-muted)] font-mono tabular-nums">{p.playCount}</span>
                                            </div>
                                            <div className="w-full h-1.5 bg-[var(--vora-bg-sunken)] rounded-full overflow-hidden">
                                                <div className="h-full bg-[var(--vora-accent-500)] rounded-full transition-all" style={{ width: `${widthPct}%` }} />
                                            </div>
                                        </li>
                                    );
                                })}
                            </ul>
                        )}
                    </ListCard>
                </div>

                <div className="vora-card p-4 flex flex-wrap items-end gap-3">
                    <div className="flex flex-col">
                        <label className="text-[10px] font-bold uppercase tracking-widest text-[var(--vora-text-muted)] mb-1">Profile</label>
                        <select
                            value={profileFilter}
                            onChange={(e) => { setProfileFilter(e.target.value); setPage(1); }}
                            className="vora-input !w-auto min-w-[160px]"
                        >
                            <option value="">All profiles</option>
                            {profiles.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}
                        </select>
                    </div>
                    <div className="flex flex-col">
                        <label className="text-[10px] font-bold uppercase tracking-widest text-[var(--vora-text-muted)] mb-1">From</label>
                        <input
                            type="date"
                            value={fromDate}
                            onChange={(e) => { setFromDate(e.target.value); setPage(1); }}
                            className="vora-input !w-auto"
                        />
                    </div>
                    <div className="flex flex-col">
                        <label className="text-[10px] font-bold uppercase tracking-widest text-[var(--vora-text-muted)] mb-1">To</label>
                        <input
                            type="date"
                            value={toDate}
                            onChange={(e) => { setToDate(e.target.value); setPage(1); }}
                            className="vora-input !w-auto"
                        />
                    </div>
                    <div className="flex flex-col flex-1 min-w-[200px]">
                        <label className="text-[10px] font-bold uppercase tracking-widest text-[var(--vora-text-muted)] mb-1">Search</label>
                        <input
                            type="text"
                            value={search}
                            onChange={(e) => { setSearch(e.target.value); setPage(1); }}
                            className="vora-input"
                            placeholder="Track, artist, album, profile…"
                        />
                    </div>
                    <button type="button" onClick={clearFilters} className="vora-button-secondary">Clear</button>
                </div>

                <div className="flex justify-between items-center text-xs text-[var(--vora-text-muted)]">
                    <div className="flex items-center gap-2">
                        <span>Show</span>
                        <select
                            value={pageSize}
                            onChange={(e) => { setPageSize(Number(e.target.value)); setPage(1); }}
                            className="vora-input !w-auto !py-1"
                        >
                            {PAGE_SIZES.map(s => <option key={s} value={s}>{s}</option>)}
                        </select>
                        <span>entries per page</span>
                    </div>
                    <span>
                        {total > 0 ? `Showing ${(page - 1) * pageSize + 1}–${Math.min(page * pageSize, total)} of ${total.toLocaleString()}` : 'No results'}
                    </span>
                </div>

                <div className="vora-card overflow-hidden">
                    <div className="overflow-x-auto min-h-[400px]">
                        <table className="w-full text-left text-sm whitespace-nowrap">
                            <thead className="bg-[var(--vora-bg-sunken)] border-b border-[var(--vora-border-subtle)] text-[var(--vora-text-muted)] text-[11px] uppercase tracking-wider">
                                <tr>
                                    <th className="px-4 py-3 font-semibold w-44">Played At</th>
                                    <th className="px-4 py-3 font-semibold">Profile</th>
                                    <th className="px-4 py-3 font-semibold">Track</th>
                                    <th className="px-4 py-3 font-semibold">Artist</th>
                                    <th className="px-4 py-3 font-semibold">Album</th>
                                    <th className="px-4 py-3 font-semibold text-right">Listened</th>
                                    <th className="px-4 py-3 font-semibold text-center">Completed</th>
                                </tr>
                            </thead>
                            <tbody className="divide-y divide-[var(--vora-border-subtle)]">
                                {loading ? (
                                    <tr><td colSpan={7} className="px-4 py-12 text-center text-[var(--vora-text-muted)]">Loading…</td></tr>
                                ) : rows.length === 0 ? (
                                    <tr><td colSpan={7} className="px-4 py-12 text-center text-[var(--vora-text-muted)]">No play history matches the current filters.</td></tr>
                                ) : (
                                    rows.map(row => (
                                        <tr key={row.id} className="hover:bg-[var(--vora-bg-sunken)]/40 transition-colors">
                                            <td className="px-4 py-2.5 text-[var(--vora-text-secondary)] tabular-nums">{formatPlayedAt(row.playedAt)}</td>
                                            <td className="px-4 py-2.5 font-semibold text-[var(--vora-text-primary)]">{row.profileName}</td>
                                            <td className="px-4 py-2.5">
                                                <div className="flex items-center gap-2">
                                                    {row.albumArtworkUrl ? (
                                                        <img src={row.albumArtworkUrl} alt="" className="w-8 h-8 rounded object-cover shrink-0" />
                                                    ) : (
                                                        <div className="w-8 h-8 rounded bg-[var(--vora-bg-sunken)] shrink-0" />
                                                    )}
                                                    <span className="font-semibold text-[var(--vora-text-primary)] truncate max-w-[260px]" title={row.trackTitle}>{row.trackTitle}</span>
                                                </div>
                                            </td>
                                            <td className="px-4 py-2.5 text-[var(--vora-text-secondary)] truncate max-w-[200px]" title={row.artist ?? ''}>{row.artist ?? '—'}</td>
                                            <td className="px-4 py-2.5 text-[var(--vora-text-secondary)] truncate max-w-[220px]" title={row.albumTitle ?? ''}>{row.albumTitle ?? '—'}</td>
                                            <td className="px-4 py-2.5 text-right text-[var(--vora-text-secondary)] font-mono tabular-nums">{formatDuration(row.durationListenedSeconds)}</td>
                                            <td className="px-4 py-2.5 text-center">
                                                {row.completed ? (
                                                    <span className="inline-block w-3.5 h-3.5 rounded-full bg-[var(--vora-success-500)]" title="Completed" />
                                                ) : (
                                                    <span className="inline-block w-3.5 h-3.5 rounded-full border-2 border-[var(--vora-border-strong)]" title="Partial" />
                                                )}
                                            </td>
                                        </tr>
                                    ))
                                )}
                            </tbody>
                        </table>
                    </div>
                </div>

                {totalPages > 1 && (
                    <div className="flex justify-center items-center gap-2">
                        <button
                            type="button"
                            disabled={page <= 1}
                            onClick={() => setPage(p => Math.max(1, p - 1))}
                            className="vora-button-secondary text-xs disabled:opacity-40"
                        >
                            Previous
                        </button>
                        <span className="text-xs text-[var(--vora-text-muted)] px-3 tabular-nums">Page {page} of {totalPages}</span>
                        <button
                            type="button"
                            disabled={page >= totalPages}
                            onClick={() => setPage(p => Math.min(totalPages, p + 1))}
                            className="vora-button-secondary text-xs disabled:opacity-40"
                        >
                            Next
                        </button>
                    </div>
                )}
            </div>
        </div>
    );
}
