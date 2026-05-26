import { useState, useEffect, Fragment } from 'react';
import { useParams } from 'react-router-dom';
import { type UserProfileHistoryDto, type UserVM, userService } from '../../api/Users/userService';
import { StorageKeys, getProfileIdFromToken } from '../../utils/storageKeys';
export default function ProfileHistoryPage() {
    const { serverId } = useParams<{ serverId?: string }>();
    const [data, setData] = useState<UserProfileHistoryDto[]>([]);
    const [loading, setLoading] = useState(true);
    const [totalItems, setTotalItems] = useState(0);
    const [expandedRows, setExpandedRows] = useState<Set<string>>(new Set());

    const userId = localStorage.getItem(StorageKeys.userId);
    const profileToken = localStorage.getItem(StorageKeys.profileToken);
    const activeProfileId = getProfileIdFromToken(profileToken);
    const isServerAdmin = localStorage.getItem(StorageKeys.isServerAdmin) === 'true';

    const [userAccount, setUserAccount] = useState<UserVM | null>(null);
    const [selectedProfileId, setSelectedProfileId] = useState<string | null>(activeProfileId);
    const [typeFilter, setTypeFilter] = useState('All');
    const [search, setSearch] = useState('');
    const [page, setPage] = useState(1);
    const [pageSize, setPageSize] = useState(25);

    useEffect(() => {
        if (userId && isServerAdmin) {
            userService.getUserAccount(userId, serverId).then(setUserAccount).catch(console.error);
        }
    }, [userId, isServerAdmin, serverId]);

    useEffect(() => {
        let isMounted = true;
        if (!userId) return;

        window.setTimeout(() => {
            if (isMounted) setLoading(true);
        }, 0);

        userService.getPlayHistory(userId, selectedProfileId, page, pageSize, search, typeFilter, serverId)
            .then(res => {
                if (isMounted) {
                    setData(res.data);
                    setTotalItems(res.total);
                    setLoading(false);
                }
            })
            .catch(err => {
                console.error(err);
                if (isMounted) setLoading(false);
            });

        return () => { isMounted = false; };
    }, [userId, selectedProfileId, page, pageSize, search, typeFilter, serverId]);

    const toggleRow = (id: string) => {
        const newSet = new Set(expandedRows);
        if (newSet.has(id)) newSet.delete(id);
        else newSet.add(id);
        setExpandedRows(newSet);
    };

    const formatLocalDate = (iso?: string) => {
        if (!iso) return '';
        const d = new Date(iso);
        return `${(d.getMonth() + 1).toString().padStart(2, '0')}-${d.getDate().toString().padStart(2, '0')}-${d.getFullYear()}`;
    };

    const formatLocalTime = (iso?: string) => {
        if (!iso) return <span className="text-[var(--vora-accent-text)] font-bold text-xs uppercase">Playing</span>;
        return new Date(iso).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' });
    };

    const formatDuration = (mins: number) => {
        if (!mins) return '--';
        const h = Math.floor(mins / 60);
        const m = mins % 60;
        return h > 0 ? `${h}h ${m}m` : `${m}m`;
    };

    const totalPages = Math.ceil(totalItems / pageSize) || 1;

    return (
        <div className="p-8 pt-24 bg-[var(--vora-bg-sunken)] min-h-full text-[var(--vora-text-secondary)] font-sans">
            <h1 className="text-3xl font-bold text-[var(--vora-text-primary)] mb-8 border-b border-[var(--vora-border-subtle)] pb-4">Play History</h1>

            {isServerAdmin && userAccount?.profiles && (
                <div className="mb-6">
                    <h3 className="text-xs font-bold text-[var(--vora-text-disabled)] uppercase tracking-widest mb-3">Viewing History For</h3>
                    <div className="flex gap-3 overflow-x-auto custom-scrollbar pb-2">
                        {userAccount.profiles.map(p => (
                            <button
                                key={p.id}
                                onClick={() => { setSelectedProfileId(p.id); setPage(1); }}
                                className={`px-4 py-2 rounded-full text-sm font-bold transition-colors whitespace-nowrap flex items-center gap-2 shadow-sm border border-[var(--vora-border-subtle)] cursor-pointer ${selectedProfileId === p.id ? 'bg-[var(--vora-accent-500)] text-[var(--vora-text-primary)] border-[var(--vora-accent-500)]' : 'bg-[var(--vora-bg-surface)] text-[var(--vora-text-muted)] hover:bg-[var(--vora-bg-raised)] hover:text-[var(--vora-text-primary)]'}`}
                            >
                                {p.profileImageUrl && <img src={p.profileImageUrl} alt="" className="w-5 h-5 rounded-full object-cover" />}
                                {p.name}
                            </button>
                        ))}
                        <button
                            onClick={() => { setSelectedProfileId(null); setPage(1); }}
                            className={`px-5 py-2 rounded-full text-sm font-bold transition-colors whitespace-nowrap shadow-sm border border-[var(--vora-border-subtle)] cursor-pointer ${selectedProfileId === null ? 'bg-[var(--vora-accent-500)] text-[var(--vora-text-primary)] border-[var(--vora-accent-500)]' : 'bg-[var(--vora-bg-surface)] text-[var(--vora-text-muted)] hover:bg-[var(--vora-bg-raised)] hover:text-[var(--vora-text-primary)]'}`}
                        >
                            All Profiles
                        </button>
                    </div>
                </div>
            )}

            <div className="flex flex-wrap justify-between items-center gap-4 mb-4">
                <div className="flex bg-[var(--vora-bg-surface)] border border-[var(--vora-border-subtle)] rounded-md overflow-hidden font-medium text-sm shadow-sm">
                    {['All', 'Movies', 'TV Shows'].map(type => (
                        <button
                            key={type}
                            onClick={() => { setTypeFilter(type); setPage(1); }}
                            className={`px-4 py-2 border-r last:border-r-0 border-[var(--vora-border-subtle)] transition-colors cursor-pointer ${typeFilter === type ? 'bg-[var(--vora-accent-500)] text-[var(--vora-text-primary)]' : 'text-[var(--vora-text-muted)] hover:bg-[var(--vora-bg-raised)] hover:text-[var(--vora-text-primary)]'}`}
                        >
                            {type}
                        </button>
                    ))}
                </div>

                <div className="flex items-center gap-3">
                    <input
                        type="text"
                        placeholder="Search history..."
                        value={search}
                        onChange={(e) => { setSearch(e.target.value); setPage(1); }}
                        className="bg-[var(--vora-bg-surface)] border border-[var(--vora-border-subtle)] rounded-md px-4 py-2 w-64 focus:outline-none focus:border-[var(--vora-accent-500)] text-[var(--vora-text-primary)] text-sm shadow-sm"
                    />
                    <select value={pageSize} onChange={(e) => { setPageSize(Number(e.target.value)); setPage(1); }} className="bg-[var(--vora-bg-surface)] border border-[var(--vora-border-subtle)] rounded-md px-3 py-2 text-sm text-[var(--vora-text-secondary)] focus:outline-none cursor-pointer shadow-sm">
                        <option value={25}>25 / page</option>
                        <option value={50}>50 / page</option>
                        <option value={100}>100 / page</option>
                    </select>
                </div>
            </div>

            <div className="bg-[var(--vora-bg-canvas)] rounded-lg shadow-xl overflow-hidden border border-[var(--vora-border-subtle)]">
                <div className="overflow-x-auto min-h-[500px]">
                    <table className="w-full text-left text-sm whitespace-nowrap">
                        <thead className="bg-[var(--vora-bg-sunken)] text-[var(--vora-text-muted)] font-semibold border-b border-[var(--vora-border-subtle)]">
                            <tr>
                                <th className="px-5 py-4 w-40">Date</th>
                                {isServerAdmin && selectedProfileId === null && <th className="px-5 py-4">Profile</th>}
                                <th className="px-5 py-4 w-1/3">Media Item</th>
                                <th className="px-5 py-4 text-center">Year</th>
                                <th className="px-5 py-4">Type</th>
                                <th className="px-5 py-4 text-center">Rating</th>
                                <th className="px-5 py-4 text-right">Paused</th>
                                <th className="px-5 py-4 text-right">Duration</th>
                                <th className="px-5 py-4 text-right">Started</th>
                                <th className="px-5 py-4 text-right">Stopped</th>
                            </tr>
                        </thead>
                        <tbody className="divide-y divide-gray-800">
                            {loading ? (
                                <tr><td colSpan={10} className="px-5 py-12 text-center text-[var(--vora-text-disabled)]">Loading history...</td></tr>
                            ) : data.length === 0 ? (
                                <tr><td colSpan={10} className="px-5 py-12 text-center text-[var(--vora-text-disabled)]">No play history found.</td></tr>
                            ) : (
                                data.map((row) => (
                                    <Fragment key={row.sessionId}>
                                        <tr className="hover:bg-[color-mix(in_srgb,var(--vora-bg-surface)_50%,transparent)] transition-colors">
                                            <td className="px-5 py-3 text-[var(--vora-text-muted)] font-medium flex items-center gap-2">
                                                {row.isGrouped ? (
                                                    <button onClick={() => toggleRow(row.sessionId)} className="w-4 h-4 flex items-center justify-center bg-[var(--vora-bg-raised)] rounded-sm text-[var(--vora-text-primary)] font-bold text-xs hover:bg-[var(--vora-bg-raised)] cursor-pointer">
                                                        {expandedRows.has(row.sessionId) ? '-' : '+'}
                                                    </button>
                                                ) : <span className="w-4"></span>}
                                                {formatLocalDate(row.timeStarted)}
                                            </td>
                                            {isServerAdmin && selectedProfileId === null && (
                                                <td className="px-5 py-3 font-bold text-[var(--vora-text-secondary)]">{row.profileName}</td>
                                            )}
                                            <td className="px-5 py-3">
                                                <div className="flex flex-col">
                                                    <span className="font-bold text-[var(--vora-text-primary)] text-[15px]">
                                                        {row.type === 'Episode' ? row.tvShowTitle : row.title}
                                                    </span>
                                                    {row.type === 'Episode' && (
                                                        <span className="text-xs font-semibold text-[var(--vora-text-disabled)] mt-0.5">
                                                            S{row.seasonNumber} E{row.episodeNumber} - {row.title}
                                                        </span>
                                                    )}
                                                </div>
                                            </td>
                                            <td className="px-5 py-3 text-center text-[var(--vora-text-muted)]">{row.releaseYear || '--'}</td>
                                            <td className="px-5 py-3">
                                                <span className={`px-2 py-1 rounded text-[10px] font-bold uppercase tracking-wider ${row.type === 'Movie' ? 'bg-[var(--vora-info-soft)] text-[var(--vora-info-text)] border border-blue-800' : 'bg-purple-900/30 text-purple-400 border border-purple-800'}`}>
                                                    {row.type}
                                                </span>
                                            </td>
                                            <td className="px-5 py-3 text-center">
                                                {row.contentRating ? <span className="text-xs font-bold text-[var(--vora-text-muted)] border border-[var(--vora-border-subtle)] px-1.5 py-0.5 rounded">{row.contentRating}</span> : <span className="text-[var(--vora-text-muted)]">--</span>}
                                            </td>
                                            <td className="px-5 py-3 text-right text-[var(--vora-text-disabled)]">{row.pausedMinutes > 0 ? `${row.pausedMinutes}m` : '--'}</td>
                                            <td className="px-5 py-3 text-right text-[var(--vora-text-muted)]">{formatDuration(row.durationMinutes)}</td>
                                            <td className="px-5 py-3 text-right text-[var(--vora-text-secondary)] font-medium">{formatLocalTime(row.timeStarted)}</td>
                                            <td className="px-5 py-3 text-right text-[var(--vora-text-muted)]">{formatLocalTime(row.timeStopped)}</td>
                                        </tr>

                                        {row.isGrouped && expandedRows.has(row.sessionId) && row.subSessions?.map((sub) => (
                                            <tr key={sub.sessionId} className="bg-[#111] hover:bg-[var(--vora-bg-sunken)] transition-colors border-l-4 border-l-gray-600">
                                                <td className="px-5 py-3 pl-11 text-[var(--vora-text-disabled)]">{formatLocalDate(sub.timeStarted)}</td>
                                                {isServerAdmin && selectedProfileId === null && (
                                                    <td className="px-5 py-3 text-[var(--vora-text-disabled)]">{sub.profileName}</td>
                                                )}
                                                <td className="px-5 py-3">
                                                    <div className="flex flex-col">
                                                        <span className="font-semibold text-[var(--vora-text-muted)] text-sm">
                                                            {sub.type === 'Episode' ? sub.tvShowTitle : sub.title}
                                                        </span>
                                                        {sub.type === 'Episode' && (
                                                            <span className="text-xs font-semibold text-[var(--vora-text-muted)] mt-0.5">
                                                                S{sub.seasonNumber} E{sub.episodeNumber} - {sub.title}
                                                            </span>
                                                        )}
                                                    </div>
                                                </td>
                                                <td className="px-5 py-3 text-center text-[var(--vora-text-disabled)]">{sub.releaseYear || '--'}</td>
                                                <td className="px-5 py-3">
                                                    <span className={`px-2 py-1 rounded text-[10px] font-bold uppercase tracking-wider opacity-60 ${sub.type === 'Movie' ? 'bg-[var(--vora-info-soft)] text-[var(--vora-info-text)]' : 'bg-purple-900/30 text-purple-400'}`}>
                                                        {sub.type}
                                                    </span>
                                                </td>
                                                <td className="px-5 py-3 text-center">
                                                    {sub.contentRating ? <span className="text-xs font-bold text-[var(--vora-text-muted)] border border-[var(--vora-border-subtle)] px-1.5 py-0.5 rounded">{sub.contentRating}</span> : <span className="text-[var(--vora-text-muted)]">--</span>}
                                                </td>
                                                <td className="px-5 py-3 text-right text-[var(--vora-text-muted)]">{sub.pausedMinutes > 0 ? `${sub.pausedMinutes}m` : '--'}</td>
                                                <td className="px-5 py-3 text-right text-[var(--vora-text-disabled)]">{formatDuration(sub.durationMinutes)}</td>
                                                <td className="px-5 py-3 text-right text-[var(--vora-text-muted)] font-medium">{formatLocalTime(sub.timeStarted)}</td>
                                                <td className="px-5 py-3 text-right text-[var(--vora-text-disabled)]">{formatLocalTime(sub.timeStopped)}</td>
                                            </tr>
                                        ))}
                                    </Fragment>
                                ))
                            )}
                        </tbody>
                    </table>
                </div>

                {!loading && data.length > 0 && (
                    <div className="p-4 border-t border-[var(--vora-border-subtle)] bg-[var(--vora-bg-sunken)] flex justify-between items-center">
                        <div className="text-sm text-[var(--vora-text-disabled)] font-medium">
                            Showing {(page - 1) * pageSize + 1} to {Math.min(page * pageSize, totalItems)} of {totalItems} entries
                        </div>
                        <div className="flex rounded overflow-hidden text-sm border border-[var(--vora-border-subtle)] shadow-sm">
                            <button disabled={page === 1} onClick={() => setPage(1)} className="px-3 py-1.5 bg-[var(--vora-bg-surface)] text-[var(--vora-text-muted)] hover:bg-[var(--vora-bg-raised)] hover:text-[var(--vora-text-primary)] disabled:opacity-50 border-r border-[var(--vora-border-subtle)] transition-colors cursor-pointer">First</button>
                            <button disabled={page === 1} onClick={() => setPage(p => Math.max(1, p - 1))} className="px-3 py-1.5 bg-[var(--vora-bg-surface)] text-[var(--vora-text-muted)] hover:bg-[var(--vora-bg-raised)] hover:text-[var(--vora-text-primary)] disabled:opacity-50 border-r border-[var(--vora-border-subtle)] transition-colors cursor-pointer">Previous</button>
                            <div className="px-4 py-1.5 bg-[var(--vora-accent-500)] text-[var(--vora-text-primary)] font-bold border-r border-[var(--vora-border-subtle)]">Page {page} of {totalPages}</div>
                            <button disabled={page >= totalPages} onClick={() => setPage(p => Math.min(totalPages, p + 1))} className="px-3 py-1.5 bg-[var(--vora-bg-surface)] text-[var(--vora-text-muted)] hover:bg-[var(--vora-bg-raised)] hover:text-[var(--vora-text-primary)] disabled:opacity-50 border-r border-[var(--vora-border-subtle)] transition-colors cursor-pointer">Next</button>
                            <button disabled={page >= totalPages} onClick={() => setPage(totalPages)} className="px-3 py-1.5 bg-[var(--vora-bg-surface)] text-[var(--vora-text-muted)] hover:bg-[var(--vora-bg-raised)] hover:text-[var(--vora-text-primary)] disabled:opacity-50 transition-colors cursor-pointer">Last</button>
                        </div>
                    </div>
                )}
            </div>
        </div>
    );
}