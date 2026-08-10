import { useEffect, useState, useMemo, useCallback } from 'react';
import { useParams } from 'react-router-dom';
import { aiStatsService, type AiStatsDashboardVM, AI_FEATURE_OPTIONS, aiFeatureLabel } from '../../api/System/aiStatsService';
import { useDialog } from '../../dialogs';
import PageHeader from '../../components/Admin/Primitives/PageHeader';
import StatCard from '../../components/Admin/Primitives/StatCard';
import HealthBadge from '../../components/Admin/Primitives/HealthBadge';
import EmptyState from '../../components/Admin/Primitives/EmptyState';

const getLocalDateString = (date: Date) => {
    const year = date.getFullYear();
    const month = String(date.getMonth() + 1).padStart(2, '0');
    const day = String(date.getDate()).padStart(2, '0');
    return `${year}-${month}-${day}`;
};

export default function AiStatsPage() {
    const dialog = useDialog();
    const { serverId } = useParams<{ serverId?: string }>();
    const [data, setData] = useState<AiStatsDashboardVM | null>(null);
    const [loading, setLoading] = useState(true);
    const [isTriggering, setIsTriggering] = useState(false);

    const today = useMemo(() => new Date(), []);
    const firstOfMonth = useMemo(() => new Date(today.getFullYear(), today.getMonth(), 1), [today]);

    const [startDate, setStartDate] = useState(getLocalDateString(firstOfMonth));
    const [endDate, setEndDate] = useState(getLocalDateString(today));
    const [page, setPage] = useState(1);
    const [pageSize, setPageSize] = useState(10);
    const [feature, setFeature] = useState('');

    const fetchStats = useCallback(async () => {
        setLoading(true);
        try {
            const dashboard = await aiStatsService.getDashboard(startDate || undefined, endDate || undefined, page, pageSize, feature || undefined, serverId);
            setData(dashboard);
        } catch (error) {
            console.error('Failed to fetch AI stats', error);
        } finally {
            setLoading(false);
        }
    }, [startDate, endDate, page, pageSize, feature, serverId]);

    useEffect(() => {
        fetchStats();
    }, [fetchStats]);

    const handleFilter = (e: React.SyntheticEvent) => {
        e.preventDefault();
        setPage(1);
        fetchStats();
    };

    const resetFilters = () => {
        setStartDate(getLocalDateString(firstOfMonth));
        setEndDate(getLocalDateString(today));
        setPage(1);
    };

    const handlePageSizeChange = (e: React.ChangeEvent<HTMLSelectElement>) => {
        setPageSize(Number(e.target.value));
        setPage(1);
    };

    const handleTriggerTask = async () => {
        setIsTriggering(true);
        try {
            await aiStatsService.triggerAiTask(serverId);
            await dialog.alert('The AI Vector Generation task has been added to the background queue.');
        } catch (error) {
            console.error(error);
            await dialog.alert('Failed to trigger the background task.');
        } finally {
            setIsTriggering(false);
        }
    };

    const costTotals = useMemo(() => {
        if (!data) return { prompt: 0, comp: 0, total: 0, cost: 0 };
        let prompt = 0;
        let comp = 0;
        let totalCost = 0;

        data.dailyStats.forEach(stat => {
            prompt += stat.promptTokens;
            comp += stat.completionTokens;

            // Approximate OpenAI list prices per token (input, output). Check the
            // "-mini"/"-nano" variants before their base model since the base name
            // is a substring of the variant.
            const m = stat.modelUsed;
            if (m.includes('gpt-4o-mini')) {
                totalCost += (stat.promptTokens * 0.00000015) + (stat.completionTokens * 0.00000060);
            } else if (m.includes('gpt-4.1-mini') || m.includes('gpt-4.1-nano')) {
                totalCost += (stat.promptTokens * 0.00000040) + (stat.completionTokens * 0.00000160);
            } else if (m.includes('gpt-4.1')) {
                totalCost += (stat.promptTokens * 0.00000200) + (stat.completionTokens * 0.00000800);
            } else if (m.includes('gpt-4o')) {
                totalCost += (stat.promptTokens * 0.00000250) + (stat.completionTokens * 0.00001000);
            } else if (m.includes('embedding')) {
                totalCost += (stat.promptTokens * 0.00000002);
            }
        });

        return { prompt, comp, total: prompt + comp, cost: totalCost };
    }, [data]);

    return (
        <div data-vora-page="">
            <PageHeader
                title="AI Usage & Cost"
                description="Token consumption and estimated OpenAI cost across all profiles."
                actions={
                    <button
                        type="button"
                        onClick={handleTriggerTask}
                        disabled={isTriggering}
                        className="vora-button-primary flex items-center gap-2"
                    >
                        <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M13 10V3L4 14h7v7l9-11h-7z" /></svg>
                        {isTriggering ? 'Queuing…' : 'Generate vectors now'}
                    </button>
                }
            />

            <div className="p-8 max-w-7xl mx-auto space-y-6">
                <form onSubmit={handleFilter} className="vora-card p-4 flex flex-wrap items-end gap-3">
                    <div>
                        <label className="block text-[10px] font-bold uppercase tracking-widest text-[var(--vora-text-muted)] mb-1">Start Date</label>
                        <input
                            type="date"
                            value={startDate}
                            onChange={e => setStartDate(e.target.value)}
                            className="vora-input"
                        />
                    </div>
                    <div>
                        <label className="block text-[10px] font-bold uppercase tracking-widest text-[var(--vora-text-muted)] mb-1">End Date</label>
                        <input
                            type="date"
                            value={endDate}
                            onChange={e => setEndDate(e.target.value)}
                            className="vora-input"
                        />
                    </div>
                    <div>
                        <label className="block text-[10px] font-bold uppercase tracking-widest text-[var(--vora-text-muted)] mb-1">Feature</label>
                        <select
                            value={feature}
                            onChange={e => { setFeature(e.target.value); setPage(1); }}
                            className="vora-input cursor-pointer"
                        >
                            <option value="">All features</option>
                            {AI_FEATURE_OPTIONS.map(o => <option key={o.id} value={o.id}>{o.label}</option>)}
                        </select>
                    </div>
                    <div className="flex gap-2">
                        <button type="submit" className="vora-button-primary">Apply filter</button>
                        <button type="button" onClick={resetFilters} className="vora-button-secondary">Current month</button>
                    </div>
                </form>

                <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
                    <StatCard
                        label="Prompt Tokens"
                        value={costTotals.prompt.toLocaleString()}
                    />
                    <StatCard
                        label="Completion Tokens"
                        value={costTotals.comp.toLocaleString()}
                    />
                    <StatCard
                        label="Total Tokens"
                        value={costTotals.total.toLocaleString()}
                        tone="info"
                    />
                    <StatCard
                        label="Estimated Cost"
                        value={`$${costTotals.cost.toFixed(3)}`}
                        tone="accent"
                    />
                </div>

                <div className="vora-card overflow-hidden">
                    <div className="px-5 py-3 border-b border-[var(--vora-border-subtle)]">
                        <h2 className="text-sm font-semibold text-[var(--vora-text-primary)]">Usage Log</h2>
                    </div>
                    <div className="overflow-x-auto">
                        <table className="w-full text-left">
                            <thead>
                                <tr className="bg-[var(--vora-bg-sunken)] border-b border-[var(--vora-border-subtle)] text-[11px] uppercase tracking-wider text-[var(--vora-text-muted)]">
                                    <th className="px-5 py-3 font-semibold">Timestamp</th>
                                    <th className="px-5 py-3 font-semibold">Feature</th>
                                    <th className="px-5 py-3 font-semibold">Profile</th>
                                    <th className="px-5 py-3 font-semibold">Model</th>
                                    <th className="px-5 py-3 font-semibold text-right">Prompt</th>
                                    <th className="px-5 py-3 font-semibold text-right">Completion</th>
                                    <th className="px-5 py-3 font-semibold text-right">Total</th>
                                </tr>
                            </thead>
                            <tbody className="divide-y divide-[var(--vora-border-subtle)]">
                                {loading ? (
                                    <tr>
                                        <td colSpan={7} className="p-8 text-center text-[var(--vora-text-muted)]">Loading…</td>
                                    </tr>
                                ) : data?.logs.length === 0 ? (
                                    <tr>
                                        <td colSpan={7} className="p-0">
                                            <EmptyState
                                                title="No usage in this period"
                                                description="Adjust the date range above to see other periods, or kick off a vector generation task."
                                            />
                                        </td>
                                    </tr>
                                ) : (
                                    data?.logs.map(log => (
                                        <tr key={log.id} className="hover:bg-[var(--vora-bg-sunken)]/50 transition-colors">
                                            <td className="px-5 py-2.5 text-sm text-[var(--vora-text-secondary)]">{new Date(log.timestamp).toLocaleString()}</td>
                                            <td className="px-5 py-2.5 text-sm font-medium text-[var(--vora-text-primary)]">{aiFeatureLabel(log.pluginId)}</td>
                                            <td className="px-5 py-2.5 text-sm font-semibold text-[var(--vora-text-primary)]">
                                                {log.profileName}
                                                {log.profileName === 'System Task' && (
                                                    <span className="ml-2"><HealthBadge tone="info" showDot={false}>Auto</HealthBadge></span>
                                                )}
                                            </td>
                                            <td className="px-5 py-2.5 text-sm text-[var(--vora-text-secondary)] font-mono">{log.modelUsed}</td>
                                            <td className="px-5 py-2.5 text-sm text-[var(--vora-text-secondary)] text-right tabular-nums">{log.promptTokens.toLocaleString()}</td>
                                            <td className="px-5 py-2.5 text-sm text-[var(--vora-text-secondary)] text-right tabular-nums">{log.completionTokens.toLocaleString()}</td>
                                            <td className="px-5 py-2.5 text-sm font-semibold text-[var(--vora-text-primary)] text-right tabular-nums">{log.totalTokens.toLocaleString()}</td>
                                        </tr>
                                    ))
                                )}
                            </tbody>
                        </table>
                    </div>

                    {data && data.logs.length > 0 && (
                        <div className="px-5 py-3 border-t border-[var(--vora-border-subtle)] bg-[var(--vora-bg-sunken)]/40 flex justify-between items-center flex-wrap gap-3">
                            <div className="flex items-center gap-4">
                                <div className="flex items-center gap-2">
                                    <label htmlFor="pageSize" className="text-xs font-semibold text-[var(--vora-text-muted)]">Rows:</label>
                                    <select
                                        id="pageSize"
                                        value={pageSize}
                                        onChange={handlePageSizeChange}
                                        className="vora-input !py-1 !w-auto"
                                    >
                                        <option value={10}>10</option>
                                        <option value={25}>25</option>
                                        <option value={50}>50</option>
                                        <option value={100}>100</option>
                                    </select>
                                </div>
                                <span className="text-xs text-[var(--vora-text-muted)]">
                                    Showing {data.totalLogs === 0 ? 0 : (page - 1) * pageSize + 1}–{Math.min(page * pageSize, data.totalLogs)} of {data.totalLogs.toLocaleString()}
                                </span>
                            </div>
                            <div className="flex gap-2">
                                <button
                                    type="button"
                                    onClick={() => setPage(p => Math.max(1, p - 1))}
                                    disabled={page === 1}
                                    className="vora-button-secondary text-xs disabled:opacity-40"
                                >
                                    Previous
                                </button>
                                <button
                                    type="button"
                                    onClick={() => setPage(p => p + 1)}
                                    disabled={page * pageSize >= data.totalLogs}
                                    className="vora-button-secondary text-xs disabled:opacity-40"
                                >
                                    Next
                                </button>
                            </div>
                        </div>
                    )}
                </div>
            </div>
        </div>
    );
}
