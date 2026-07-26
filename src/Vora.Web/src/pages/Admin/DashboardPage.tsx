import { useEffect, useState, useMemo } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { streamingAdminService, type NowPlayingSession, type SystemStats } from '../../api/Streaming/streamingAdminService';
import { adminNotificationService, type AdminNotificationVM } from '../../api/System/adminNotificationService';
import { libraryService, type LibrarySummary } from '../../api/Media/libraryService';
import { pluginAdminService, type PluginVM } from '../../api/System/pluginAdminService';
import { useDialog } from '../../dialogs';
import PageHeader from '../../components/Admin/Primitives/PageHeader';
import StatCard from '../../components/Admin/Primitives/StatCard';
import ListCard from '../../components/Admin/Primitives/ListCard';
import EntityCard from '../../components/Admin/Primitives/EntityCard';
import HealthBadge from '../../components/Admin/Primitives/HealthBadge';
import StatusDot from '../../components/Admin/Primitives/StatusDot';
import EmptyState from '../../components/Admin/Primitives/EmptyState';

function formatRelative(iso: string): string {
    const diffMs = Date.now() - new Date(iso).getTime();
    const mins = Math.floor(diffMs / 60000);
    if (mins < 1) return 'just now';
    if (mins < 60) return `${mins}m ago`;
    const hours = Math.floor(mins / 60);
    if (hours < 24) return `${hours}h ago`;
    const days = Math.floor(hours / 24);
    if (days < 7) return `${days}d ago`;
    return new Date(iso).toLocaleDateString();
}

function formatTime(seconds: number): string {
    const h = Math.floor(seconds / 3600);
    const m = Math.floor((seconds % 3600) / 60);
    const s = Math.floor(seconds % 60);
    if (h > 0) return `${h}:${m.toString().padStart(2, '0')}:${s.toString().padStart(2, '0')}`;
    return `${m}:${s.toString().padStart(2, '0')}`;
}

function cpuTone(cpu: number): 'default' | 'warn' | 'danger' {
    if (cpu >= 90) return 'danger';
    if (cpu >= 70) return 'warn';
    return 'default';
}

const LIBRARY_TYPE_LABEL: Record<string, string> = {
    Movie: 'Movies',
    TvShow: 'TV Shows',
    Music: 'Music',
    HomeVideo: 'Home Videos',
    AudioBook: 'Audiobooks',
    Photo: 'Photos',
};

// Render a compact "source → output" chip showing what's actually
// being delivered. When the session is transcoding and the output
// differs from source, show "4K HDR10 → 1080p SDR"; otherwise just
// the single representation. Only renders if we have something
// meaningful to show.
function renderResolutionChip(
    sourceRes: string | undefined,
    sourceHdr: string | undefined,
    outputRes: string | undefined,
    outputHdr: string | undefined,
    isTranscoding: boolean,
) {
    const fmt = (res: string | undefined, hdr: string | undefined) => {
        const r = res === '2160p' ? '4K' : res ?? '';
        const h = hdr && hdr.toLowerCase() !== 'sdr' && hdr.toLowerCase() !== 'none' ? hdr : '';
        if (!r && !h) return '';
        return [r, h].filter(Boolean).join(' ');
    };
    const sourceText = fmt(sourceRes, sourceHdr);
    const outputText = fmt(outputRes, outputHdr);
    const changed = isTranscoding && outputText && sourceText && outputText !== sourceText;
    const label = changed ? `${sourceText} → ${outputText}` : (outputText || sourceText);
    if (!label) return null;
    return (
        <>
            <span>·</span>
            <span className="font-medium text-[var(--vora-text-secondary)]">{label}</span>
        </>
    );
}

function NowPlayingRow({ session, onPlay, onPause, onStop }: { session: NowPlayingSession, onPlay: () => void, onPause: () => void, onStop: () => void }) {
    const percent = session.durationSeconds > 0 ? (session.currentPosition / session.durationSeconds) * 100 : 0;
    const isTranscoding = session.videoStrategy?.toLowerCase().includes('transcode') || session.audioStrategy?.toLowerCase().includes('transcode');
    const strategyTone: 'ok' | 'warn' | 'error' = session.strategy === 'DirectPlay' ? 'ok' : session.strategy === 'Remux' ? 'warn' : 'error';

    const fullTitle = session.tvShowTitle
        ? `${session.tvShowTitle} — ${session.title}`
        : session.title;
    const subtitle = session.tvShowTitle && session.seasonNumber && session.episodeNumber
        ? `S${session.seasonNumber} · E${session.episodeNumber}`
        : null;

    return (
        <div className="flex items-center gap-4 px-5 py-4 border-b border-[var(--vora-border-subtle)] last:border-b-0 hover:bg-[var(--vora-bg-sunken)]/50 transition-colors">
            <div className="w-12 h-16 rounded-md bg-[var(--vora-bg-sunken)] overflow-hidden shrink-0">
                {session.posterUrl
                    ? <img src={session.posterUrl} alt={fullTitle} className="w-full h-full object-cover" />
                    : <div className="w-full h-full flex items-center justify-center text-[var(--vora-text-disabled)]">
                          <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M4 16l4-4 3 3 5-5 4 4M4 6h16v14H4V6z" /></svg>
                      </div>
                }
            </div>
            <div className="flex-1 min-w-0">
                <div className="flex items-center gap-2 mb-0.5">
                    <span className="text-sm font-semibold text-[var(--vora-text-primary)] truncate">{fullTitle}</span>
                    {session.isPaused && (
                        <HealthBadge tone="neutral" showDot={false}>Paused</HealthBadge>
                    )}
                </div>
                <div className="flex items-center gap-2 text-xs text-[var(--vora-text-muted)] mb-2 flex-wrap">
                    {subtitle && <><span>{subtitle}</span><span>·</span></>}
                    <span className="font-medium text-[var(--vora-text-secondary)]">{session.userName}</span>
                    <span>·</span>
                    <span className="truncate">{session.deviceName}</span>
                    <span>·</span>
                    <span className="tabular-nums">{(session.bandwidthKbps / 1000).toFixed(1)} Mbps</span>
                    {renderResolutionChip(session.resolution, session.hdrType, session.outputResolution, session.outputHdrType, isTranscoding)}
                </div>
                <div className="flex items-center gap-3">
                    <div className="flex-1 h-1 bg-[var(--vora-bg-sunken)] rounded-full overflow-hidden">
                        <div className="h-full bg-[var(--vora-accent-500)] rounded-full transition-all" style={{ width: `${percent}%` }} />
                    </div>
                    <span className="text-[11px] tabular-nums text-[var(--vora-text-muted)] shrink-0">
                        {formatTime(session.currentPosition)} / {formatTime(session.durationSeconds)}
                    </span>
                </div>
            </div>
            <div className="flex flex-col items-end gap-2 shrink-0">
                <div className="flex items-center gap-1.5">
                    <HealthBadge tone={strategyTone}>{session.strategy}</HealthBadge>
                    {isTranscoding && <HealthBadge tone="info" showDot={false}>Transcode</HealthBadge>}
                </div>
                <div className="flex items-center gap-1">
                    {session.isPaused ? (
                        <button onClick={onPlay} title="Resume" className="p-1.5 rounded hover:bg-[var(--vora-bg-sunken)] text-[var(--vora-text-secondary)] hover:text-[var(--vora-text-primary)] cursor-pointer transition-colors">
                            <svg className="w-3.5 h-3.5" fill="currentColor" viewBox="0 0 20 20"><path d="M4 4l12 6-12 6z" /></svg>
                        </button>
                    ) : (
                        <button onClick={onPause} title="Pause" className="p-1.5 rounded hover:bg-[var(--vora-bg-sunken)] text-[var(--vora-text-secondary)] hover:text-[var(--vora-text-primary)] cursor-pointer transition-colors">
                            <svg className="w-3.5 h-3.5" fill="currentColor" viewBox="0 0 20 20"><path d="M5 4h3v12H5zm7 0h3v12h-3z" /></svg>
                        </button>
                    )}
                    <button onClick={onStop} title="Terminate" className="p-1.5 rounded hover:bg-[var(--vora-danger-soft)] text-[var(--vora-text-secondary)] hover:text-[var(--vora-danger-text)] cursor-pointer transition-colors">
                        <svg className="w-3.5 h-3.5" fill="currentColor" viewBox="0 0 20 20"><path d="M5 5h10v10H5z" /></svg>
                    </button>
                </div>
            </div>
        </div>
    );
}

function NotificationRow({ n }: { n: AdminNotificationVM }) {
    const tone = n.severity === 'Error' ? 'error' : n.severity === 'Warning' ? 'warn' : 'info';
    return (
        <div className="flex items-start gap-3 px-5 py-3 border-b border-[var(--vora-border-subtle)] last:border-b-0">
            <span className="mt-1.5 shrink-0">
                <StatusDot tone={tone} />
            </span>
            <div className="flex-1 min-w-0">
                <div className="text-sm font-semibold text-[var(--vora-text-primary)] truncate">{n.title}</div>
                <div className="text-xs text-[var(--vora-text-secondary)] mt-0.5 line-clamp-2">{n.message}</div>
                <div className="text-[11px] text-[var(--vora-text-muted)] mt-1">{formatRelative(n.createdAt)}</div>
            </div>
        </div>
    );
}

export default function DashboardPage() {
    const { serverId } = useParams<{ serverId?: string }>();
    const navigate = useNavigate();
    const dialog = useDialog();

    const [sessions, setSessions] = useState<NowPlayingSession[]>([]);
    const [stats, setStats] = useState<SystemStats | null>(null);
    const [libraries, setLibraries] = useState<LibrarySummary[]>([]);
    const [notifications, setNotifications] = useState<AdminNotificationVM[]>([]);
    const [plugins, setPlugins] = useState<PluginVM[]>([]);
    const [librariesLoaded, setLibrariesLoaded] = useState(false);
    const [notificationsLoaded, setNotificationsLoaded] = useState(false);

    useEffect(() => {
        let isMounted = true;
        const fetchLive = async () => {
            try {
                const [sessionsData, statsData] = await Promise.all([
                    streamingAdminService.getNowPlaying(serverId),
                    streamingAdminService.getSystemStats(serverId),
                ]);
                if (!isMounted) return;
                setSessions(sessionsData);
                setStats(statsData);
            } catch (error) {
                console.error('Failed to fetch live dashboard data:', error);
            }
        };
        fetchLive();
        const interval = setInterval(fetchLive, 5000);
        return () => { isMounted = false; clearInterval(interval); };
    }, [serverId]);

    useEffect(() => {
        let isMounted = true;
        Promise.all([
            libraryService.getLibraries(serverId).catch(() => [] as LibrarySummary[]),
            adminNotificationService.getRecent(8, false, serverId).catch(() => [] as AdminNotificationVM[]),
            pluginAdminService.getPlugins(serverId).catch(() => [] as PluginVM[]),
        ]).then(([libs, notifs, plug]) => {
            if (!isMounted) return;
            setLibraries(libs);
            setLibrariesLoaded(true);
            setNotifications(notifs);
            setNotificationsLoaded(true);
            setPlugins(plug);
        });
        return () => { isMounted = false; };
    }, [serverId]);

    const totalBandwidthMbps = sessions.reduce((sum, s) => sum + s.bandwidthKbps, 0) / 1000;
    const transcodeCount = sessions.filter(s =>
        s.videoStrategy?.toLowerCase().includes('transcode') ||
        s.audioStrategy?.toLowerCase().includes('transcode')
    ).length;

    const pluginsHealth = useMemo(() => {
        const installed = plugins.length;
        const enabled = plugins.filter(p => p.isEnabled).length;
        if (installed === 0) return { tone: 'neutral' as const, label: 'None installed' };
        if (enabled === 0) return { tone: 'warn' as const, label: `${installed} installed, 0 enabled` };
        return { tone: 'ok' as const, label: `${enabled} of ${installed} enabled` };
    }, [plugins]);

    const handleSessionCommand = async (sessionId: string, command: 'play' | 'pause') => {
        try {
            await streamingAdminService.sendCommand(sessionId, command, undefined, serverId);
        } catch {
            await dialog.alert('Failed to send command.');
        }
    };

    const handleSessionStop = async (sessionId: string) => {
        const message = await dialog.prompt('Reason for stopping (optional):');
        if (message === null) return;
        try {
            await streamingAdminService.sendCommand(sessionId, 'stop', message, serverId);
        } catch {
            await dialog.alert('Failed to terminate stream.');
        }
    };

    return (
        <div data-vora-page="">
            <PageHeader
                title="Dashboard"
                description="Live activity and system health at a glance."
                actions={
                    <div className="flex items-center gap-2 text-xs text-[var(--vora-text-muted)]">
                        <span className="relative flex h-2 w-2">
                            <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-[var(--vora-success-500)] opacity-75"></span>
                            <span className="relative inline-flex rounded-full h-2 w-2 bg-[var(--vora-success-500)]"></span>
                        </span>
                        Live · refreshes every 5s
                    </div>
                }
            />

            <div className="p-8 space-y-8 max-w-[1400px] mx-auto">
                {/* Hero stat row */}
                <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
                    <StatCard
                        label="Now Streaming"
                        value={sessions.length}
                        tone={sessions.length > 0 ? 'accent' : 'default'}
                        footer={
                            <div className="text-xs text-[var(--vora-text-muted)]">
                                {transcodeCount > 0
                                    ? <><span className="font-semibold text-[var(--vora-text-secondary)] tabular-nums">{transcodeCount}</span> transcoding</>
                                    : 'All direct play'}
                            </div>
                        }
                    />
                    <StatCard
                        label="CPU Usage"
                        value={stats ? stats.cpuUsagePercentage.toFixed(1) : '—'}
                        unit="%"
                        tone={stats ? cpuTone(stats.cpuUsagePercentage) : 'default'}
                    />
                    <StatCard
                        label="Memory"
                        value={stats ? stats.ramUsageGb.toFixed(2) : '—'}
                        unit="GB"
                    />
                    <StatCard
                        label="Total Bandwidth"
                        value={totalBandwidthMbps.toFixed(1)}
                        unit="Mbps"
                        tone={totalBandwidthMbps > 0 ? 'info' : 'default'}
                    />
                </div>

                {/* Two-column row: Now Playing + Recent Activity */}
                <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
                    <ListCard
                        title="Now Playing"
                        description={sessions.length === 0 ? 'No active streams' : `${sessions.length} active session${sessions.length === 1 ? '' : 's'}`}
                        className="lg:col-span-2"
                        maxBodyHeight="520px"
                    >
                        {sessions.length === 0 ? (
                            <EmptyState
                                title="Quiet right now"
                                description="When users start watching or listening, you'll see live sessions here."
                                icon={<svg className="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M14.752 11.168l-3.197-2.132A1 1 0 0010 9.87v4.263a1 1 0 001.555.832l3.197-2.132a1 1 0 000-1.664z" /><circle cx="12" cy="12" r="9" strokeWidth={1.5} /></svg>}
                            />
                        ) : (
                            sessions.map(session => (
                                <NowPlayingRow
                                    key={session.sessionId}
                                    session={session}
                                    onPlay={() => handleSessionCommand(session.sessionId, 'play')}
                                    onPause={() => handleSessionCommand(session.sessionId, 'pause')}
                                    onStop={() => handleSessionStop(session.sessionId)}
                                />
                            ))
                        )}
                    </ListCard>

                    <ListCard
                        title="Recent Activity"
                        description={notificationsLoaded ? `${notifications.length} recent event${notifications.length === 1 ? '' : 's'}` : 'Loading…'}
                        maxBodyHeight="520px"
                    >
                        {!notificationsLoaded ? (
                            <div className="p-5 space-y-3">
                                {[1, 2, 3].map(i => <div key={i} className="vora-skeleton h-12" />)}
                            </div>
                        ) : notifications.length === 0 ? (
                            <EmptyState
                                title="All clear"
                                description="No recent alerts or notifications."
                                icon={<svg className="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M5 13l4 4L19 7" /></svg>}
                            />
                        ) : (
                            notifications.map(n => <NotificationRow key={n.id} n={n} />)
                        )}
                    </ListCard>
                </div>

                {/* Libraries strip */}
                <section className="space-y-3">
                    <div className="flex items-end justify-between">
                        <div>
                            <h2 className="text-base font-semibold text-[var(--vora-text-primary)]">Libraries</h2>
                            <p className="text-sm text-[var(--vora-text-muted)]">
                                {librariesLoaded ? `${libraries.length} configured` : 'Loading…'}
                            </p>
                        </div>
                        <button
                            type="button"
                            onClick={() => navigate(serverId ? `/admin/server/${serverId}/libraries` : '/admin/libraries')}
                            className="text-xs font-semibold text-[var(--vora-accent-text)] hover:text-[var(--vora-accent-active)] cursor-pointer"
                        >
                            Manage all →
                        </button>
                    </div>
                    {!librariesLoaded ? (
                        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-4">
                            {[1, 2, 3].map(i => <div key={i} className="vora-skeleton h-32" />)}
                        </div>
                    ) : libraries.length === 0 ? (
                        <div className="vora-card">
                            <EmptyState
                                title="No libraries yet"
                                description="Add a library to start indexing your media."
                                actionLabel="Add Library"
                                onAction={() => navigate(serverId ? `/admin/server/${serverId}/libraries/new` : '/admin/libraries/new')}
                            />
                        </div>
                    ) : (
                        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-4">
                            {[...libraries].sort((a, b) => a.name.localeCompare(b.name)).map(lib => (
                                <EntityCard
                                    key={lib.id}
                                    title={lib.name}
                                    subtitle={LIBRARY_TYPE_LABEL[lib.type] ?? lib.type}
                                    badge={<HealthBadge tone={lib.isBeingWatched ? 'ok' : 'neutral'}>{lib.isBeingWatched ? 'Watching' : 'Idle'}</HealthBadge>}
                                    onClick={() => navigate(serverId ? `/admin/server/${serverId}/libraries/${lib.id}/manage` : `/admin/libraries/${lib.id}/manage`)}
                                />
                            ))}
                        </div>
                    )}
                </section>

                {/* System Health */}
                <ListCard
                    title="System Health"
                    description="High-level status of core subsystems."
                >
                    <div className="divide-y divide-[var(--vora-border-subtle)]">
                        <HealthRow
                            label="CPU"
                            value={stats ? `${stats.cpuUsagePercentage.toFixed(1)}%` : '—'}
                            tone={stats ? cpuTone(stats.cpuUsagePercentage) === 'danger' ? 'error' : cpuTone(stats.cpuUsagePercentage) === 'warn' ? 'warn' : 'ok' : 'neutral'}
                            badge={stats ? (stats.cpuUsagePercentage >= 90 ? 'High' : stats.cpuUsagePercentage >= 70 ? 'Elevated' : 'Healthy') : 'Loading'}
                        />
                        <HealthRow
                            label="Streaming"
                            value={`${sessions.length} active`}
                            tone={'ok'}
                            badge={transcodeCount > 0 ? `${transcodeCount} transcoding` : 'All direct'}
                        />
                        <HealthRow
                            label="Plugins"
                            value={pluginsHealth.label}
                            tone={pluginsHealth.tone}
                            badge={pluginsHealth.tone === 'ok' ? 'Healthy' : pluginsHealth.tone === 'warn' ? 'Attention' : 'Empty'}
                            onClick={() => navigate(serverId ? `/admin/server/${serverId}/plugins` : '/admin/plugins')}
                        />
                        {(() => {
                            if (!stats || stats.diskTotalBytes === 0) {
                                return (
                                    <HealthRow
                                        label="Storage"
                                        value="—"
                                        tone="neutral"
                                        badge="Unavailable"
                                    />
                                );
                            }
                            const pctUsed = (stats.diskUsedBytes / stats.diskTotalBytes) * 100;
                            const tone: 'ok' | 'warn' | 'error' =
                                pctUsed >= 95 ? 'error' :
                                pctUsed >= 85 ? 'warn' :
                                'ok';
                            const badge = tone === 'error' ? 'Critical' : tone === 'warn' ? 'Low' : 'Healthy';
                            return (
                                <HealthRow
                                    label="Storage"
                                    value={`${formatBytes(stats.diskUsedBytes)} / ${formatBytes(stats.diskTotalBytes)} used · ${pctUsed.toFixed(1)}%`}
                                    tone={tone}
                                    badge={badge}
                                />
                            );
                        })()}
                    </div>
                </ListCard>
            </div>
        </div>
    );
}

function formatBytes(bytes: number): string {
    if (bytes <= 0) return '0 B';
    const units = ['B', 'KB', 'MB', 'GB', 'TB', 'PB'];
    const i = Math.min(units.length - 1, Math.floor(Math.log(bytes) / Math.log(1024)));
    const value = bytes / Math.pow(1024, i);
    return `${value.toFixed(value >= 100 || i === 0 ? 0 : 1)} ${units[i]}`;
}

function HealthRow({ label, value, tone, badge, onClick }: { label: string, value: string, tone: 'ok' | 'warn' | 'error' | 'info' | 'neutral', badge: string, onClick?: () => void }) {
    return (
        <div
            onClick={onClick}
            className={`flex items-center justify-between px-5 py-3 ${onClick ? 'cursor-pointer hover:bg-[var(--vora-bg-sunken)]/60' : ''} transition-colors`}
        >
            <div className="flex items-center gap-3">
                <StatusDot tone={tone} size="md" />
                <div>
                    <div className="text-sm font-semibold text-[var(--vora-text-primary)]">{label}</div>
                    <div className="text-xs text-[var(--vora-text-muted)]">{value}</div>
                </div>
            </div>
            <HealthBadge tone={tone}>{badge}</HealthBadge>
        </div>
    );
}
