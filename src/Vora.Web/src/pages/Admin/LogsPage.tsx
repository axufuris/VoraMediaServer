import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useParams } from 'react-router-dom';
import {
    logsService,
    ALL_LOG_LEVELS,
    type LogEntryVM,
    type LogLevel,
    type LogLevelStateVM,
    type LogQueryParams
} from '../../api/System/logsService';
import { useSignalREvent } from '../../hooks/useSignalREvent';
import { useDialog } from '../../dialogs';
import PageHeader from '../../components/Admin/Primitives/PageHeader';

type TimePreset = '5m' | '1h' | '24h' | 'all';

const LIVE_TAIL_CAP = 2000;
const PAGE_SIZE = 500;

function timestampLabel(iso: string): string {
    const d = new Date(iso);
    return d.toLocaleTimeString(undefined, { hour12: false }) + '.' + String(d.getMilliseconds()).padStart(3, '0');
}

function levelTone(level: LogLevel): { bg: string; fg: string; dot: string } {
    switch (level) {
        case 'Critical':
        case 'Error':
            return {
                bg: 'bg-[var(--vora-danger-soft)]',
                fg: 'text-[var(--vora-danger-text)]',
                dot: 'bg-[var(--vora-danger-500)]'
            };
        case 'Warning':
            return {
                bg: 'bg-[var(--vora-warning-soft)]',
                fg: 'text-[var(--vora-warning-text)]',
                dot: 'bg-[var(--vora-warning-500)]'
            };
        case 'Information':
            return {
                bg: 'bg-[var(--vora-info-soft)]',
                fg: 'text-[var(--vora-info-text)]',
                dot: 'bg-[var(--vora-info-500)]'
            };
        case 'Debug':
        case 'Trace':
        default:
            return {
                bg: 'bg-[var(--vora-bg-sunken)]',
                fg: 'text-[var(--vora-text-secondary)]',
                dot: 'bg-[var(--vora-text-muted)]'
            };
    }
}

function presetSince(preset: TimePreset): string | undefined {
    if (preset === 'all') return undefined;
    const now = Date.now();
    const minutes = preset === '5m' ? 5 : preset === '1h' ? 60 : 24 * 60;
    return new Date(now - minutes * 60_000).toISOString();
}

export default function LogsPage() {
    const dialog = useDialog();
    const { serverId } = useParams<{ serverId?: string }>();

    const [activeLevels, setActiveLevels] = useState<LogLevel[]>(['Information', 'Warning', 'Error', 'Critical']);
    const [categoryFilter, setCategoryFilter] = useState('');
    const [searchTerm, setSearchTerm] = useState('');
    const [timePreset, setTimePreset] = useState<TimePreset>('1h');
    const [customSince, setCustomSince] = useState('');
    const [customUntil, setCustomUntil] = useState('');
    const [useCustomRange, setUseCustomRange] = useState(false);

    const [entries, setEntries] = useState<LogEntryVM[]>([]);
    const [totalMatched, setTotalMatched] = useState(0);
    const [loading, setLoading] = useState(false);
    const [moreAvailable, setMoreAvailable] = useState(false);
    const [expanded, setExpanded] = useState<Record<number, boolean>>({});

    const [follow, setFollow] = useState(true);
    const [pendingNewCount, setPendingNewCount] = useState(0);
    const [showLevelsPanel, setShowLevelsPanel] = useState(false);

    const listRef = useRef<HTMLDivElement | null>(null);

    const queryParams = useMemo<LogQueryParams>(() => {
        const sinceUtc = useCustomRange && customSince ? new Date(customSince).toISOString() : presetSince(timePreset);
        const untilUtc = useCustomRange && customUntil ? new Date(customUntil).toISOString() : undefined;
        return {
            levels: activeLevels.length === ALL_LOG_LEVELS.length ? undefined : activeLevels,
            category: categoryFilter.trim() || undefined,
            search: searchTerm.trim() || undefined,
            sinceUtc,
            untilUtc,
            limit: PAGE_SIZE
        };
    }, [activeLevels, categoryFilter, searchTerm, timePreset, useCustomRange, customSince, customUntil]);

    const matchesFilters = useCallback((entry: LogEntryVM): boolean => {
        if (queryParams.levels && !queryParams.levels.includes(entry.level)) return false;
        if (queryParams.category && !entry.category.toLowerCase().startsWith(queryParams.category.toLowerCase())) return false;
        if (queryParams.search) {
            const needle = queryParams.search.toLowerCase();
            const inMessage = entry.message.toLowerCase().includes(needle);
            const inException = entry.exception ? entry.exception.toLowerCase().includes(needle) : false;
            if (!inMessage && !inException) return false;
        }
        if (queryParams.sinceUtc && entry.timestampUtc < queryParams.sinceUtc) return false;
        if (queryParams.untilUtc && entry.timestampUtc > queryParams.untilUtc) return false;
        return true;
    }, [queryParams]);

    const reload = useCallback(async () => {
        setLoading(true);
        try {
            const result = await logsService.query(queryParams, serverId);
            setEntries(result.entries);
            setTotalMatched(result.totalMatched);
            setMoreAvailable(result.moreAvailable);
            setPendingNewCount(0);
            setExpanded({});
        } catch (err) {
            console.error('Failed to load logs', err);
        } finally {
            setLoading(false);
        }
    }, [queryParams, serverId]);

    useEffect(() => {
        reload();
    }, [reload]);

    useEffect(() => {
        if (!follow) return;
        const el = listRef.current;
        if (!el) return;
        el.scrollTop = el.scrollHeight;
    }, [follow, entries]);

    const onIncoming = useCallback((batch: LogEntryVM[]) => {
        if (!batch || batch.length === 0) return;
        const accepted = batch.filter(matchesFilters);
        if (accepted.length === 0) return;
        setEntries(prev => {
            const merged = [...prev, ...accepted];
            return merged.length > LIVE_TAIL_CAP ? merged.slice(merged.length - LIVE_TAIL_CAP) : merged;
        });
        if (!follow) {
            setPendingNewCount(count => count + accepted.length);
        }
    }, [follow, matchesFilters]);

    useSignalREvent<LogEntryVM[]>('LogEntryBatch', onIncoming);

    const loadOlder = async () => {
        if (entries.length === 0) return;
        const oldestId = entries[0].id;
        setLoading(true);
        try {
            const result = await logsService.query({ ...queryParams, beforeId: oldestId }, serverId);
            setEntries(prev => [...result.entries, ...prev]);
            setMoreAvailable(result.moreAvailable);
        } catch (err) {
            console.error('Failed to load older entries', err);
        } finally {
            setLoading(false);
        }
    };

    const toggleLevel = (level: LogLevel) => {
        setActiveLevels(prev =>
            prev.includes(level) ? prev.filter(l => l !== level) : [...prev, level]
        );
    };

    const handleExport = async (format: 'txt' | 'json') => {
        try {
            const blob = await logsService.exportLogs(format, queryParams, serverId);
            const url = URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = `vora-logs-${new Date().toISOString().replace(/[:.]/g, '-')}.${format}`;
            document.body.appendChild(a);
            a.click();
            a.remove();
            URL.revokeObjectURL(url);
        } catch (err) {
            console.error('Export failed', err);
            await dialog.alert('Failed to export logs.');
        }
    };

    const onScroll = () => {
        const el = listRef.current;
        if (!el) return;
        const atBottom = el.scrollHeight - el.scrollTop - el.clientHeight < 32;
        if (atBottom && pendingNewCount > 0) {
            setPendingNewCount(0);
        }
        if (!atBottom && follow) {
            setFollow(false);
        }
    };

    const jumpToBottom = () => {
        const el = listRef.current;
        if (!el) return;
        el.scrollTop = el.scrollHeight;
        setFollow(true);
        setPendingNewCount(0);
    };

    const actions = (
        <div className="flex items-center gap-2">
            <button
                type="button"
                onClick={() => setShowLevelsPanel(true)}
                className="vora-button-secondary text-xs"
                title="Adjust per-category log levels at runtime"
            >
                Log Levels
            </button>
            <button
                type="button"
                onClick={() => handleExport('txt')}
                className="vora-button-secondary text-xs"
            >
                Export .txt
            </button>
            <button
                type="button"
                onClick={() => handleExport('json')}
                className="vora-button-secondary text-xs"
            >
                Export .json
            </button>
            <button
                type="button"
                onClick={() => setFollow(f => !f)}
                className={`text-xs font-semibold px-3 py-1.5 rounded-[var(--vora-radius-md)] cursor-pointer border transition-colors ${
                    follow
                        ? 'bg-[var(--vora-accent-soft)] text-[var(--vora-accent-text)] border-[var(--vora-accent-500)]/30'
                        : 'bg-[var(--vora-bg-sunken)] text-[var(--vora-text-secondary)] border-[var(--vora-border-subtle)]'
                }`}
            >
                {follow ? 'Following live' : 'Paused'}
            </button>
        </div>
    );

    return (
        <div data-vora-page="">
            <PageHeader
                title="Server Logs"
                description="Live view of server-side logs. Filter by level, category, time, or text — pause to investigate a row, resume to keep tailing."
                actions={actions}
            />

            <FilterBar
                activeLevels={activeLevels}
                onToggleLevel={toggleLevel}
                categoryFilter={categoryFilter}
                onCategoryChange={setCategoryFilter}
                searchTerm={searchTerm}
                onSearchChange={setSearchTerm}
                timePreset={timePreset}
                onTimePresetChange={(p: TimePreset) => { setTimePreset(p); setUseCustomRange(false); }}
                useCustomRange={useCustomRange}
                onToggleCustomRange={() => setUseCustomRange(v => !v)}
                customSince={customSince}
                customUntil={customUntil}
                onCustomSinceChange={setCustomSince}
                onCustomUntilChange={setCustomUntil}
                onReload={reload}
                loading={loading}
            />

            <div className="px-8 pb-10 max-w-7xl mx-auto pt-2 relative">
                <div className="vora-card overflow-hidden">
                    <div className="flex items-center justify-between px-4 py-2 border-b border-[var(--vora-border-subtle)] bg-[var(--vora-bg-sunken)]">
                        <div className="text-xs text-[var(--vora-text-secondary)]">
                            Showing <span className="font-semibold text-[var(--vora-text-primary)]">{entries.length}</span> of <span className="font-semibold text-[var(--vora-text-primary)]">{totalMatched}</span> matched entries
                        </div>
                        {moreAvailable && (
                            <button
                                type="button"
                                onClick={loadOlder}
                                className="text-xs font-semibold text-[var(--vora-accent-text)] hover:underline cursor-pointer"
                            >
                                Load older
                            </button>
                        )}
                    </div>

                    <div
                        ref={listRef}
                        onScroll={onScroll}
                        className="overflow-auto font-mono text-[12px] leading-relaxed"
                        style={{ maxHeight: 'calc(100vh - 320px)', minHeight: '420px' }}
                    >
                        {entries.length === 0 && !loading && (
                            <div className="p-10 text-center text-[var(--vora-text-muted)] text-sm font-sans">
                                No matching log entries.
                            </div>
                        )}
                        {entries.map(entry => (
                            <LogRow
                                key={entry.id}
                                entry={entry}
                                isExpanded={!!expanded[entry.id]}
                                onToggleExpand={() =>
                                    setExpanded(prev => ({ ...prev, [entry.id]: !prev[entry.id] }))
                                }
                            />
                        ))}
                    </div>
                </div>

                {!follow && pendingNewCount > 0 && (
                    <button
                        type="button"
                        onClick={jumpToBottom}
                        className="absolute right-12 bottom-16 z-10 px-4 py-2 rounded-full shadow-lg cursor-pointer bg-[var(--vora-accent-500)] text-[var(--vora-accent-contrast)] text-xs font-semibold hover:opacity-90 transition"
                    >
                        {pendingNewCount} new {pendingNewCount === 1 ? 'entry' : 'entries'} below ↓
                    </button>
                )}
            </div>

            {showLevelsPanel && (
                <LogLevelsDrawer
                    serverId={serverId}
                    onClose={() => setShowLevelsPanel(false)}
                />
            )}
        </div>
    );
}

interface FilterBarProps {
    activeLevels: LogLevel[];
    onToggleLevel: (level: LogLevel) => void;
    categoryFilter: string;
    onCategoryChange: (value: string) => void;
    searchTerm: string;
    onSearchChange: (value: string) => void;
    timePreset: TimePreset;
    onTimePresetChange: (preset: TimePreset) => void;
    useCustomRange: boolean;
    onToggleCustomRange: () => void;
    customSince: string;
    customUntil: string;
    onCustomSinceChange: (value: string) => void;
    onCustomUntilChange: (value: string) => void;
    onReload: () => void;
    loading: boolean;
}

function FilterBar(props: FilterBarProps) {
    return (
        <div className="px-8 pt-4 pb-3 max-w-7xl mx-auto">
            <div className="vora-card p-4 flex flex-col gap-3">
                <div className="flex flex-wrap items-center gap-2">
                    <span className="text-xs font-semibold text-[var(--vora-text-muted)] uppercase tracking-wide mr-1">Level</span>
                    {ALL_LOG_LEVELS.map(level => {
                        const active = props.activeLevels.includes(level);
                        const tone = levelTone(level);
                        return (
                            <button
                                key={level}
                                type="button"
                                onClick={() => props.onToggleLevel(level)}
                                className={`text-[11px] font-semibold px-2.5 py-1 rounded-full border cursor-pointer transition flex items-center gap-1.5 ${
                                    active
                                        ? `${tone.bg} ${tone.fg} border-transparent`
                                        : 'bg-transparent text-[var(--vora-text-muted)] border-[var(--vora-border-subtle)] hover:text-[var(--vora-text-primary)]'
                                }`}
                            >
                                <span className={`w-1.5 h-1.5 rounded-full ${tone.dot}`} />
                                {level}
                            </button>
                        );
                    })}
                </div>

                <div className="flex flex-wrap items-center gap-3">
                    <label className="flex items-center gap-2 flex-1 min-w-[220px]">
                        <span className="text-xs font-semibold text-[var(--vora-text-muted)] uppercase tracking-wide">Category</span>
                        <input
                            type="text"
                            value={props.categoryFilter}
                            onChange={e => props.onCategoryChange(e.target.value)}
                            placeholder="e.g. Vora.Application.Iptv"
                            className="vora-input text-xs flex-1"
                        />
                    </label>
                    <label className="flex items-center gap-2 flex-1 min-w-[220px]">
                        <span className="text-xs font-semibold text-[var(--vora-text-muted)] uppercase tracking-wide">Search</span>
                        <input
                            type="text"
                            value={props.searchTerm}
                            onChange={e => props.onSearchChange(e.target.value)}
                            placeholder="Substring in message or exception"
                            className="vora-input text-xs flex-1"
                        />
                    </label>
                    <button
                        type="button"
                        onClick={props.onReload}
                        className="vora-button-secondary text-xs"
                        disabled={props.loading}
                    >
                        {props.loading ? 'Loading…' : 'Refresh'}
                    </button>
                </div>

                <div className="flex flex-wrap items-center gap-2">
                    <span className="text-xs font-semibold text-[var(--vora-text-muted)] uppercase tracking-wide mr-1">Time</span>
                    {(['5m', '1h', '24h', 'all'] as TimePreset[]).map(preset => {
                        const active = !props.useCustomRange && props.timePreset === preset;
                        return (
                            <button
                                key={preset}
                                type="button"
                                onClick={() => props.onTimePresetChange(preset)}
                                className={`text-[11px] font-semibold px-2.5 py-1 rounded-full border cursor-pointer transition ${
                                    active
                                        ? 'bg-[var(--vora-accent-soft)] text-[var(--vora-accent-text)] border-[var(--vora-accent-500)]/30'
                                        : 'bg-transparent text-[var(--vora-text-muted)] border-[var(--vora-border-subtle)] hover:text-[var(--vora-text-primary)]'
                                }`}
                            >
                                {preset === 'all' ? 'All' : `Last ${preset}`}
                            </button>
                        );
                    })}
                    <button
                        type="button"
                        onClick={props.onToggleCustomRange}
                        className={`text-[11px] font-semibold px-2.5 py-1 rounded-full border cursor-pointer transition ${
                            props.useCustomRange
                                ? 'bg-[var(--vora-accent-soft)] text-[var(--vora-accent-text)] border-[var(--vora-accent-500)]/30'
                                : 'bg-transparent text-[var(--vora-text-muted)] border-[var(--vora-border-subtle)] hover:text-[var(--vora-text-primary)]'
                        }`}
                    >
                        Custom
                    </button>
                    {props.useCustomRange && (
                        <>
                            <input
                                type="datetime-local"
                                value={props.customSince}
                                onChange={e => props.onCustomSinceChange(e.target.value)}
                                className="vora-input text-xs"
                            />
                            <span className="text-xs text-[var(--vora-text-muted)]">→</span>
                            <input
                                type="datetime-local"
                                value={props.customUntil}
                                onChange={e => props.onCustomUntilChange(e.target.value)}
                                className="vora-input text-xs"
                            />
                        </>
                    )}
                </div>
            </div>
        </div>
    );
}

function LogRow({ entry, isExpanded, onToggleExpand }: { entry: LogEntryVM; isExpanded: boolean; onToggleExpand: () => void }) {
    const tone = levelTone(entry.level);
    const clickable = entry.hasException;
    return (
        <div className="border-b border-[var(--vora-border-subtle)]/50 hover:bg-[var(--vora-bg-sunken)]/50 transition-colors">
            <button
                type="button"
                onClick={clickable ? onToggleExpand : undefined}
                disabled={!clickable}
                className={`w-full text-left px-4 py-1.5 flex items-start gap-3 ${clickable ? 'cursor-pointer' : 'cursor-default'}`}
            >
                <span className="text-[var(--vora-text-muted)] shrink-0 tabular-nums">
                    {timestampLabel(entry.timestampUtc)}
                </span>
                <span className={`shrink-0 inline-flex items-center gap-1 px-1.5 py-0.5 rounded text-[10px] font-bold uppercase tracking-wider ${tone.bg} ${tone.fg}`}>
                    {entry.level.slice(0, 4)}
                </span>
                <span className="shrink-0 text-[var(--vora-text-secondary)] max-w-[280px] truncate" title={entry.category}>
                    {entry.category}
                </span>
                <span className="flex-1 min-w-0 text-[var(--vora-text-primary)] whitespace-pre-wrap break-words">
                    {entry.message}
                </span>
                {clickable && (
                    <span className="shrink-0 text-[10px] text-[var(--vora-text-muted)]">
                        {isExpanded ? '▾' : '▸'}
                    </span>
                )}
            </button>
            {clickable && isExpanded && entry.exception && (
                <pre className="px-4 pb-3 pt-1 text-[11px] text-[var(--vora-danger-text)] bg-[var(--vora-danger-soft)]/30 whitespace-pre-wrap break-words border-t border-[var(--vora-border-subtle)]/50">
                    {entry.exception}
                </pre>
            )}
        </div>
    );
}

function LogLevelsDrawer({ serverId, onClose }: { serverId?: string; onClose: () => void }) {
    const dialog = useDialog();
    const [state, setState] = useState<LogLevelStateVM | null>(null);
    const [loading, setLoading] = useState(true);
    const [newCategory, setNewCategory] = useState('');
    const [newLevel, setNewLevel] = useState<LogLevel>('Debug');

    const load = useCallback(async () => {
        setLoading(true);
        try {
            const result = await logsService.getLevels(serverId);
            setState(result);
        } catch (err) {
            console.error('Failed to load levels', err);
        } finally {
            setLoading(false);
        }
    }, [serverId]);

    useEffect(() => { load(); }, [load]);

    const apply = async (category: string, level: LogLevel) => {
        try {
            await logsService.setLevel(category, level, serverId);
            await load();
        } catch {
            await dialog.alert('Failed to apply log level.');
        }
    };

    const clearOverride = async (category: string) => {
        try {
            await logsService.clearLevel(category, serverId);
            await load();
        } catch {
            await dialog.alert('Failed to clear override.');
        }
    };

    const addOverride = async () => {
        if (!newCategory.trim()) return;
        await apply(newCategory.trim(), newLevel);
        setNewCategory('');
    };

    return (
        <div className="fixed inset-0 z-[200] flex">
            <div className="flex-1 bg-black/40" onClick={onClose} />
            <div className="w-[420px] h-full bg-[var(--vora-bg-canvas)] border-l border-[var(--vora-border-subtle)] flex flex-col">
                <div className="p-5 border-b border-[var(--vora-border-subtle)] flex items-center justify-between">
                    <div>
                        <h2 className="text-base font-semibold text-[var(--vora-text-primary)]">Log Levels</h2>
                        <p className="text-xs text-[var(--vora-text-muted)] mt-0.5">Overrides apply immediately and reset on restart.</p>
                    </div>
                    <button
                        type="button"
                        onClick={onClose}
                        className="text-[var(--vora-text-muted)] hover:text-[var(--vora-text-primary)] cursor-pointer text-lg leading-none"
                        aria-label="Close"
                    >
                        ✕
                    </button>
                </div>
                <div className="flex-1 overflow-auto p-5 space-y-5">
                    {loading || !state ? (
                        <div className="vora-skeleton h-24" />
                    ) : (
                        <>
                            <div>
                                <div className="text-xs font-semibold text-[var(--vora-text-muted)] uppercase tracking-wide mb-2">Default</div>
                                <div className="vora-card p-3 flex items-center justify-between">
                                    <span className="text-sm text-[var(--vora-text-secondary)]">All categories without an override</span>
                                    <span className="text-sm font-semibold text-[var(--vora-text-primary)]">{state.defaultLevel}</span>
                                </div>
                            </div>

                            <div>
                                <div className="text-xs font-semibold text-[var(--vora-text-muted)] uppercase tracking-wide mb-2">Active overrides</div>
                                {state.overrides.length === 0 ? (
                                    <div className="text-xs text-[var(--vora-text-muted)] italic">None</div>
                                ) : (
                                    <div className="space-y-2">
                                        {state.overrides.map(o => (
                                            <div key={o.category} className="vora-card p-3 flex items-center gap-2">
                                                <span className="flex-1 text-xs font-mono text-[var(--vora-text-primary)] truncate" title={o.category}>{o.category}</span>
                                                <select
                                                    value={o.level}
                                                    onChange={e => apply(o.category, e.target.value as LogLevel)}
                                                    className="vora-input text-xs py-1"
                                                >
                                                    {ALL_LOG_LEVELS.map(l => <option key={l} value={l}>{l}</option>)}
                                                </select>
                                                <button
                                                    type="button"
                                                    onClick={() => clearOverride(o.category)}
                                                    className="text-[11px] text-[var(--vora-text-muted)] hover:text-[var(--vora-danger-text)] cursor-pointer"
                                                >
                                                    Reset
                                                </button>
                                            </div>
                                        ))}
                                    </div>
                                )}
                            </div>

                            <div>
                                <div className="text-xs font-semibold text-[var(--vora-text-muted)] uppercase tracking-wide mb-2">Add override</div>
                                <div className="vora-card p-3 flex items-center gap-2">
                                    <input
                                        type="text"
                                        list="vora-log-category-suggestions"
                                        value={newCategory}
                                        onChange={e => setNewCategory(e.target.value)}
                                        placeholder="e.g. Vora.Application.Iptv"
                                        className="vora-input text-xs flex-1"
                                    />
                                    <datalist id="vora-log-category-suggestions">
                                        {state.knownCategories.map(c => <option key={c} value={c} />)}
                                    </datalist>
                                    <select
                                        value={newLevel}
                                        onChange={e => setNewLevel(e.target.value as LogLevel)}
                                        className="vora-input text-xs py-1"
                                    >
                                        {ALL_LOG_LEVELS.map(l => <option key={l} value={l}>{l}</option>)}
                                    </select>
                                    <button
                                        type="button"
                                        onClick={addOverride}
                                        className="vora-button-primary text-xs"
                                    >
                                        Add
                                    </button>
                                </div>
                            </div>
                        </>
                    )}
                </div>
            </div>
        </div>
    );
}
