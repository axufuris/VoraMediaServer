import { useEffect, useState, useMemo, useCallback } from 'react';
import { useParams } from 'react-router-dom';
import { dvrService, type IptvRecordingSessionVM } from '../../../api/Iptv/dvrService';
import { dvrPlaybackService } from '../../../api/Iptv/dvrPlaybackService';
import { serverVault } from '../../../utils/serverVault';
import { usePlayer } from '../../../contexts/usePlayer';
import { useSignalREvent } from '../../../hooks/useSignalREvent';
import DvrSessionCard from '../../../components/Dvr/DvrSessionCard';
import { useDialog } from '../../../dialogs';
import { StorageKeys, getProfileIdFromToken } from '../../../utils/storageKeys';
import PageHeader from '../../../components/Client/Primitives/PageHeader';
import EmptyState from '../../../components/Client/Primitives/EmptyState';
import Tabs from '../../../components/Client/Primitives/Tabs';
import { Modal } from '../../../components/Common/Modal';

type DvrTabKey = 'Completed' | 'Upcoming' | 'Failed';

interface SeriesPromptState {
    title: string;
    onEpisode: () => void;
    onSeries: () => void;
}

export default function DvrDashboard() {
    const { serverId } = useParams<{ serverId?: string }>();
    const { playMedia } = usePlayer();
    const dialog = useDialog();
    const [sessions, setSessions] = useState<IptvRecordingSessionVM[]>([]);
    const [isLoading, setIsLoading] = useState(true);
    const [activeTab, setActiveTab] = useState<DvrTabKey>('Completed');
    const [playingId, setPlayingId] = useState<string | null>(null);
    const [seriesPrompt, setSeriesPrompt] = useState<SeriesPromptState | null>(null);
    const [expandedGroup, setExpandedGroup] = useState<string | null>(null);

    useEffect(() => {
        setExpandedGroup(null);
    }, [activeTab]);

    useEffect(() => {
        const loadSessions = async () => {
            try {
                const activeServer = serverVault.getActiveServer();
                if (!activeServer) return;

                const profileToken = localStorage.getItem(StorageKeys.profileToken);
                const activeProfileId = getProfileIdFromToken(profileToken) ?? activeServer.profileId;

                const data = await dvrService.getRecordingSessions(activeProfileId, serverId);
                setSessions(data.sort((a, b) => new Date(b.startTime).getTime() - new Date(a.startTime).getTime()));
            } catch (error) {
                console.error('Failed to load DVR sessions', error);
            } finally {
                setIsLoading(false);
            }
        };

        loadSessions();
        const interval = setInterval(loadSessions, 60000);
        return () => clearInterval(interval);
    }, [serverId]);

    useSignalREvent('DvrSessionsUpdated', useCallback(() => {
        const fetchFreshSessions = async () => {
            try {
                const activeServer = serverVault.getActiveServer();
                if (!activeServer) return;

                const profileToken = localStorage.getItem(StorageKeys.profileToken);
                const activeProfileId = getProfileIdFromToken(profileToken) ?? activeServer.profileId;

                const freshData = await dvrService.getRecordingSessions(activeProfileId, activeServer.id);
                setSessions(freshData.sort((a, b) => new Date(b.startTime).getTime() - new Date(a.startTime).getTime()));
            } catch (e) {
                console.error('SignalR: Failed to refresh DVR sessions', e);
            }
        };
        fetchFreshSessions();
    }, []));

    const formatTime = (dateStr: string) => {
        const date = new Date(dateStr.endsWith('Z') ? dateStr : dateStr + 'Z');
        return date.toLocaleString([], { weekday: 'short', month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' });
    };

    const getDurationString = (start: string, end: string) => {
        const diff = new Date(end.endsWith('Z') ? end : end + 'Z').getTime() - new Date(start.endsWith('Z') ? start : start + 'Z').getTime();
        return Math.max(1, Math.round(diff / 60000)) + ' min';
    };

    const getStatusColor = (status: string) => {
        switch (status) {
            case 'Recording': return 'text-[var(--vora-danger-500)] bg-red-500/10 border-red-500/50';
            case 'Pending': return 'text-blue-400 bg-blue-400/10 border-blue-400/50';
            case 'Completed': return 'text-green-500 bg-green-500/10 border-green-500/50';
            case 'Completed (Partial)': return 'text-yellow-500 bg-yellow-500/10 border-yellow-500/50';
            case 'Post-Processing': return 'text-[var(--vora-accent-500)] bg-orange-400/10 border-orange-400/50';
            default: return 'text-[var(--vora-text-muted)] bg-[var(--vora-bg-raised)]/50 border-[var(--vora-border-subtle)]/50';
        }
    };

    const filteredSessions = useMemo(() => {
        return sessions.filter(s => {
            if (activeTab === 'Upcoming') return s.status === 'Pending' || s.status === 'Recording';
            if (activeTab === 'Completed') return s.status === 'Completed' || s.status === 'Completed (Partial)' || s.status === 'Post-Processing';
            return s.status === 'Failed' || s.status === 'Conflict' || s.status === 'Cancelled';
        }).sort((a, b) => new Date(a.startTime).getTime() - new Date(b.startTime).getTime());
    }, [sessions, activeTab]);

    const groupedSessions = useMemo(() => {
        const groups: Record<string, IptvRecordingSessionVM[]> = {};
        filteredSessions.forEach(s => {
            const key = s.title || 'Unknown Program';
            if (!groups[key]) groups[key] = [];
            groups[key].push(s);
        });

        return Object.entries(groups).map(([title, groupSessions]) => {
            const targetTime = activeTab === 'Upcoming'
                ? Math.min(...groupSessions.map(x => new Date(x.startTime).getTime()))
                : Math.max(...groupSessions.map(x => new Date(x.startTime).getTime()));

            return {
                title,
                sessions: groupSessions,
                targetStart: new Date(targetTime),
                channelLogo: groupSessions[0]?.schedule?.channel?.logoUrl,
                channelName: groupSessions[0]?.schedule?.channel?.name,
            };
        }).sort((a, b) => activeTab === 'Upcoming'
            ? a.targetStart.getTime() - b.targetStart.getTime()
            : b.targetStart.getTime() - a.targetStart.getTime());
    }, [filteredSessions, activeTab]);

    const activeGroupData = useMemo(() => {
        if (!expandedGroup) return null;
        return groupedSessions.find(g => g.title === expandedGroup)?.sessions || [];
    }, [expandedGroup, groupedSessions]);

    const counts = useMemo(() => {
        const c: Record<DvrTabKey, number> = { Completed: 0, Upcoming: 0, Failed: 0 };
        sessions.forEach(s => {
            if (s.status === 'Pending' || s.status === 'Recording') c.Upcoming++;
            else if (s.status === 'Completed' || s.status === 'Completed (Partial)' || s.status === 'Post-Processing') c.Completed++;
            else c.Failed++;
        });
        return c;
    }, [sessions]);

    const handlePlay = async (session: IptvRecordingSessionVM) => {
        try {
            setPlayingId(session.id);
            const targetServer = serverId ? serverVault.getServer(serverId) : serverVault.getActiveServer();
            const data = await dvrPlaybackService.playDvrSession(session.id, targetServer?.id);

            playMedia({
                id: session.id,
                title: session.title,
                subtitle: session.episodeTitle || formatTime(session.startTime),
                streamUrl: `${import.meta.env.VITE_API_URL || ''}${data.url}`,
                serverId: targetServer?.id,
                container: 'mp4',
                playbackContextType: 'Dvr',
                commercialMarkers: session.commercialMarkersJson ? JSON.parse(session.commercialMarkersJson) : [],
            });
        } catch (err) {
            console.error('DVR Playback Error:', err);
            await dialog.alert({
                title: 'Playback error',
                message: 'Failed to start playback. The file may be missing or corrupt.',
                tone: 'danger',
            });
        } finally {
            setPlayingId(null);
        }
    };

    const cancelSingleSession = async (sessionId: string, action: 'cancel' | 'delete') => {
        try {
            const activeServer = serverVault.getActiveServer();
            await dvrService.deleteDvrSession(sessionId, activeServer?.id);
        } catch (error) {
            console.error(`Failed to ${action} recording:`, error);
            await dialog.alert({
                title: 'Error',
                message: `Failed to ${action} the recording. Please try again.`,
                tone: 'danger',
            });
        }
    };

    const cancelEntireSeries = async (sessionId: string) => {
        try {
            const activeServer = serverVault.getActiveServer();
            await dvrService.cancelDvrSeries(sessionId, activeServer?.id);
        } catch (error) {
            console.error('Failed to cancel series:', error);
            await dialog.alert({
                title: 'Error',
                message: 'Failed to cancel the series. Please try again.',
                tone: 'danger',
            });
        }
    };

    const handleDelete = async (session: IptvRecordingSessionVM) => {
        const action: 'cancel' | 'delete' = activeTab === 'Upcoming' ? 'cancel' : 'delete';

        if (activeTab === 'Upcoming' && session.schedule?.isSeries) {
            setSeriesPrompt({
                title: session.title,
                onEpisode: async () => {
                    setSeriesPrompt(null);
                    await cancelSingleSession(session.id, 'cancel');
                },
                onSeries: async () => {
                    setSeriesPrompt(null);
                    await cancelEntireSeries(session.id);
                },
            });
            return;
        }

        const ok = await dialog.confirm({
            title: action === 'cancel' ? 'Cancel recording?' : 'Delete recording?',
            message: `Are you sure you want to ${action} this recording? This action cannot be undone.`,
            confirmText: action === 'cancel' ? 'Cancel recording' : 'Delete',
            cancelText: 'Keep',
            tone: 'danger',
        });
        if (ok) await cancelSingleSession(session.id, action);
    };

    const handleCancelSeries = async (title: string, firstSessionId: string) => {
        const ok = await dialog.confirm({
            title: `Cancel "${title}"?`,
            message: 'This will stop all future recordings of this series.',
            confirmText: 'Cancel series',
            cancelText: 'Keep recording',
            tone: 'danger',
        });
        if (ok) await cancelEntireSeries(firstSessionId);
    };

    const renderSessionCard = (session: IptvRecordingSessionVM) => (
        <DvrSessionCard
            key={session.id}
            session={session}
            activeTab={activeTab}
            playingId={playingId}
            formatTime={formatTime}
            getDurationString={getDurationString}
            getStatusColor={getStatusColor}
            onPlay={handlePlay}
            onDelete={handleDelete}
        />
    );

    if (isLoading) {
        return (
            <div className="p-10">
                <div className="vora-skeleton mb-6 h-10 w-64" />
                <div className="vora-skeleton mb-6 h-8 w-96" />
                <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-6">
                    {Array.from({ length: 8 }, (_, i) => <div key={i} className="vora-skeleton h-60" />)}
                </div>
            </div>
        );
    }

    const tabBadge = (count: number) => count > 0 ? (
        <span className="rounded-full px-1.5 py-0.5 text-[10px] font-semibold" style={{ background: 'var(--vora-accent-soft)', color: 'var(--vora-accent-text)' }}>{count}</span>
    ) : undefined;

    const backAction = expandedGroup ? (
        <button
            type="button"
            onClick={() => setExpandedGroup(null)}
            className="vora-button-secondary cursor-pointer inline-flex items-center gap-2"
        >
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M19 12H5M12 19l-7-7 7-7" /></svg>
            All shows
        </button>
    ) : undefined;

    return (
        <div className="min-h-full pb-16">
            <PageHeader
                title={expandedGroup ?? 'DVR Recordings'}
                subtitle={expandedGroup ? `Episodes of "${expandedGroup}"` : 'Schedule, watch, and manage everything you have recorded.'}
                actions={backAction}
            />

            <div className="px-8">
                <Tabs<DvrTabKey>
                    tabs={[
                        { key: 'Completed', label: 'Completed', badge: tabBadge(counts.Completed) },
                        { key: 'Upcoming', label: 'Upcoming', badge: tabBadge(counts.Upcoming) },
                        { key: 'Failed', label: 'Failed', badge: tabBadge(counts.Failed) },
                    ]}
                    active={activeTab}
                    onChange={setActiveTab}
                />
            </div>

            <div className="px-8 pt-6">
                {filteredSessions.length === 0 ? (
                    <EmptyState
                        title={`No ${activeTab.toLowerCase()} recordings`}
                        description={activeTab === 'Upcoming'
                            ? 'Schedule a recording from the Live TV guide and it will appear here.'
                            : activeTab === 'Failed'
                                ? 'Nothing has failed recently. That is a good thing.'
                                : 'Recordings you finish watching live here, ready to play back.'}
                        icon={(
                            <svg width="40" height="40" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5">
                                <circle cx="12" cy="12" r="9" />
                                <circle cx="12" cy="12" r="3" />
                            </svg>
                        )}
                    />
                ) : (
                    <div className="grid auto-rows-max content-start grid-cols-1 gap-6 pb-10 md:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
                        {expandedGroup && activeGroupData && activeGroupData.map(session => renderSessionCard(session))}

                        {!expandedGroup && groupedSessions.map(group => {
                            if (group.sessions.length === 1) return renderSessionCard(group.sessions[0]);

                            const isUpcomingSeries = activeTab === 'Upcoming' && group.sessions[0]?.schedule?.isSeries;

                            return (
                                <div
                                    key={group.title}
                                    className="vora-card relative flex min-h-[220px] flex-col overflow-hidden"
                                >
                                    <span aria-hidden="true" className="absolute -top-1 left-3 right-3 h-2 rounded-t-lg" style={{ background: 'var(--vora-bg-raised)', opacity: 0.55, transform: 'translateY(-4px)' }} />
                                    <span aria-hidden="true" className="absolute -top-1 left-5 right-5 h-2 rounded-t-lg" style={{ background: 'var(--vora-bg-raised)', opacity: 0.3, transform: 'translateY(-9px)' }} />

                                    <div className="flex items-start gap-4 p-4" style={{ borderBottom: '1px solid var(--vora-border-subtle)' }}>
                                        <div className="flex h-12 w-16 shrink-0 items-center justify-center rounded p-1" style={{ background: 'var(--vora-bg-sunken)', border: '1px solid var(--vora-border-subtle)' }}>
                                            {group.channelLogo ? (
                                                <img src={group.channelLogo} alt="" className="max-h-full max-w-full object-contain" />
                                            ) : (
                                                <span className="text-[10px] font-bold" style={{ color: 'var(--vora-text-disabled)' }}>No logo</span>
                                            )}
                                        </div>
                                        <div className="min-w-0 flex-1">
                                            <h3 className="m-0 truncate text-base font-semibold" style={{ color: 'var(--vora-text-primary)' }}>{group.title}</h3>
                                            <p className="m-0 mt-0.5 truncate text-xs" style={{ color: 'var(--vora-text-muted)' }}>{group.channelName || 'Multiple channels'}</p>
                                        </div>
                                    </div>

                                    <div className="flex flex-1 flex-col items-center justify-center p-6 text-center">
                                        <div
                                            className="mb-3 flex h-16 w-16 items-center justify-center rounded-full"
                                            style={{
                                                background: 'var(--vora-accent-soft)',
                                                border: '1px solid var(--vora-accent-soft-hover)',
                                                color: 'var(--vora-accent-text)',
                                            }}
                                        >
                                            <span className="text-2xl font-bold">{group.sessions.length}</span>
                                        </div>
                                        <p className="m-0 text-sm font-semibold" style={{ color: 'var(--vora-text-primary)' }}>Episodes {activeTab.toLowerCase()}</p>
                                        <p className="m-0 mt-1 text-xs" style={{ color: 'var(--vora-text-muted)' }}>
                                            {activeTab === 'Upcoming' ? 'Next: ' : 'Latest: '}
                                            {formatTime(group.targetStart.toISOString())}
                                        </p>
                                    </div>

                                    <div className="flex w-full" style={{ borderTop: '1px solid var(--vora-border-subtle)' }}>
                                        <button
                                            type="button"
                                            onClick={() => setExpandedGroup(group.title)}
                                            className="flex flex-1 cursor-pointer items-center justify-center gap-2 py-3 text-sm font-semibold transition-colors hover:bg-white/5"
                                            style={{ color: 'var(--vora-text-primary)' }}
                                        >
                                            <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75"><rect x="3" y="7" width="18" height="13" rx="2" /><path d="M3 7l3-3h5l2 3" /></svg>
                                            View episodes
                                        </button>
                                        {isUpcomingSeries && (
                                            <button
                                                type="button"
                                                onClick={(e) => { e.stopPropagation(); handleCancelSeries(group.title, group.sessions[0].id); }}
                                                title="Cancel series"
                                                aria-label="Cancel series"
                                                className="flex w-14 cursor-pointer items-center justify-center transition-colors hover:bg-white/5"
                                                style={{ borderLeft: '1px solid var(--vora-border-subtle)', color: 'var(--vora-danger-text)' }}
                                            >
                                                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                                                    <polyline points="3 6 5 6 21 6" />
                                                    <path d="M19 6l-1 14a2 2 0 01-2 2H8a2 2 0 01-2-2L5 6" />
                                                </svg>
                                            </button>
                                        )}
                                    </div>
                                </div>
                            );
                        })}
                    </div>
                )}
            </div>

            <Modal
                isOpen={seriesPrompt !== null}
                onClose={() => setSeriesPrompt(null)}
                size="md"
                surface="gray-900"
                closeOnBackdropClick
            >
                <div className="p-6">
                    <h2 className="m-0 mb-2 text-xl font-semibold" style={{ color: 'var(--vora-text-primary)' }}>Cancel recording?</h2>
                    <p className="m-0 mb-6 text-sm leading-relaxed" style={{ color: 'var(--vora-text-secondary)' }}>
                        Do you want to cancel just this single episode, or stop recording the entire <strong>{seriesPrompt?.title}</strong> series?
                    </p>
                    <div className="flex flex-col gap-2 sm:flex-row sm:justify-end">
                        <button
                            type="button"
                            onClick={() => setSeriesPrompt(null)}
                            className="vora-button-secondary cursor-pointer"
                        >
                            Keep it
                        </button>
                        <button
                            type="button"
                            onClick={() => seriesPrompt?.onEpisode()}
                            className="vora-button-secondary cursor-pointer"
                        >
                            Just this episode
                        </button>
                        <button
                            type="button"
                            onClick={() => seriesPrompt?.onSeries()}
                            className="cursor-pointer rounded-md px-4 py-2.5 text-sm font-semibold transition-colors"
                            style={{ background: 'var(--vora-danger-500)', color: '#ffffff' }}
                        >
                            Entire series
                        </button>
                    </div>
                </div>
            </Modal>
        </div>
    );
}
