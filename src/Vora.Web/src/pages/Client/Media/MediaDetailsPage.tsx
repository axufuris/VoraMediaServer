import { useEffect, useState, useCallback, useMemo } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { mediaService, type MediaItem } from '../../../api/Media/mediaService';
import { libraryAdminService } from '../../../api/Media/libraryAdminService';
import EditMetadataModal from '../../../components/Media/EditMetadataModal';
import AddToCollectionModal from '../../../components/Collections/AddToCollectionModal';
import AddToPlaylistModal from '../../../components/Collections/AddToPlaylistModal';
import MediaCastRow from '../../../components/Media/MediaCastRow';
import MediaExtrasRow from '../../../components/Media/MediaExtrasRow';
import MediaEpisodesList from '../../../components/Media/MediaEpisodesList';
import { useSignalREvent } from '../../../hooks/useSignalREvent';
import { usePlayer } from '../../../contexts/PlayerContext';
import { streamingService } from '../../../api/Streaming/streamingService';
import { scanDeviceCapabilities } from '../../../utils/hardwareScanner';
import { useDialog } from '../../../dialogs';
import CinematicBackdrop from '../../../components/Client/Primitives/CinematicBackdrop';
import MediaPoster from '../../../components/Client/Primitives/MediaPoster';
import MediaRail from '../../../components/Client/Primitives/MediaRail';
import EmptyState from '../../../components/Client/Primitives/EmptyState';
import QualityPanel, { QualityPanelSection, type QualityOption } from '../../../components/Client/Primitives/QualityPanel';

interface UpcomingEpisodeParsed {
    SeasonNumber: number;
    EpisodeNumber: number;
    Title: string;
    AirDate: string;
}

function formatRuntime(minutes?: number): string | null {
    if (!minutes) return null;
    const hours = Math.floor(minutes / 60);
    const mins = Math.round(minutes % 60);
    if (hours === 0) return `${mins} min`;
    return mins === 0 ? `${hours} hr` : `${hours} hr ${mins} min`;
}

export default function MediaDetailsPage() {
    const dialog = useDialog();
    const { serverId, id } = useParams<{ serverId?: string, id: string }>();
    const navigate = useNavigate();

    const [media, setMedia] = useState<MediaItem | null>(null);
    const [loading, setLoading] = useState(true);

    const [isEditModalOpen, setIsEditModalOpen] = useState(false);
    const [isCollectionModalOpen, setIsCollectionModalOpen] = useState(false);
    const [isPlaylistModalOpen, setIsPlaylistModalOpen] = useState(false);
    const [showMenu, setShowMenu] = useState(false);
    const [isQualityPanelOpen, setIsQualityPanelOpen] = useState(false);

    const [selectedVideoId, setSelectedVideoId] = useState<string>('');
    const [selectedAudioId, setSelectedAudioId] = useState<string>('');
    const [selectedSubtitleId, setSelectedSubtitleId] = useState<string>('none');

    const isAdmin = localStorage.getItem('is_server_admin') === 'true';
    const caps = useMemo(() => scanDeviceCapabilities(), []);
    const { playMedia, isPlaying } = usePlayer();

    const reloadMedia = useCallback(async () => {
        if (!id) return;
        try {
            const data = await mediaService.getMediaItem(id, serverId);
            setMedia(data);
        } catch {
            console.error('Failed to reload media details.');
        }
    }, [id, serverId]);

    useEffect(() => {
        let isMounted = true;
        if (!id) return;

        mediaService.getMediaItem(id, serverId)
            .then(data => {
                if (isMounted) {
                    setMedia(data);
                    setLoading(false);
                }
            })
            .catch(() => {
                if (isMounted) setLoading(false);
            });

        return () => { isMounted = false; };
    }, [id, serverId]);

    useEffect(() => {
        if (media?.mediaParts?.length) {
            let bestVideoId = '';
            let lowestVideoPenalty = 9999;
            let winningPart = media.mediaParts[0];

            for (const part of media.mediaParts) {
                for (const track of part.videoTracks || []) {
                    let penalty = 0;
                    const codec = track.codec?.toLowerCase() || '';
                    const is4k = part.resolution?.toLowerCase().includes('4k') || part.resolution?.includes('2160');

                    if (codec && !caps.videoCodecs.includes(codec)) penalty += 1000;
                    if (!is4k) penalty += 50;

                    if (penalty < lowestVideoPenalty) {
                        lowestVideoPenalty = penalty;
                        bestVideoId = track.id;
                        winningPart = part;
                    }
                }
            }

            let bestAudioId = '';
            if (winningPart.audioTracks?.length) {
                let lowestAudioPenalty = 9999;

                for (const track of winningPart.audioTracks) {
                    let penalty = 0;
                    const codec = track.codec?.toLowerCase() || '';
                    const channels = track.channels || 2;
                    const needsDownmix = channels > caps.maxAudioChannels;

                    if (!caps.audioCodecs.includes(codec) || needsDownmix) penalty += 1000;
                    penalty -= (channels * 10);
                    if (track.title?.toLowerCase().includes('commentary')) penalty += 500;

                    if (penalty < lowestAudioPenalty) {
                        lowestAudioPenalty = penalty;
                        bestAudioId = track.id;
                    }
                }
            }

            let bestSubId = 'none';
            if (winningPart.subtitleTracks?.length) {
                const bestSub = winningPart.subtitleTracks.find(s => s.isForced || s.isDefault);
                if (bestSub) bestSubId = bestSub.id;
            }

            setTimeout(() => {
                setSelectedVideoId(prev => prev !== bestVideoId ? bestVideoId : prev);
                setSelectedAudioId(prev => prev !== bestAudioId ? bestAudioId : prev);
                setSelectedSubtitleId(prev => prev !== bestSubId ? bestSubId : prev);
            }, 0);
        }
    }, [media, caps]);

    useEffect(() => {
        if (!isPlaying && id) {
            const timer = setTimeout(() => reloadMedia(), 500);
            return () => clearTimeout(timer);
        }
    }, [isPlaying, id, reloadMedia]);

    useSignalREvent('MediaItemUpdated', useCallback((updatedId: string) => {
        if (!id || !media) return;
        const targetId = updatedId.toLowerCase();
        if (targetId === id.toLowerCase() || media.episodes?.some(ep => ep.id.toLowerCase() === targetId) || media.seasons?.some(s => s.id.toLowerCase() === targetId)) {
            reloadMedia();
        }
    }, [id, media, reloadMedia]));

    useSignalREvent('MediaAnalysisUpdated', useCallback((updatedId: string) => {
        if (id && updatedId.toLowerCase() === id.toLowerCase()) reloadMedia();
    }, [id, reloadMedia]));

    const handlePlay = async (resume: boolean = true) => {
        if (!media) return;

        let subtitle = '';
        if (media.type === 'Episode') subtitle = `S${media.seasonNumber} E${media.episodeNumber} - ${media.tvShowTitle}`;
        else if (media.releaseDate) subtitle = new Date(media.releaseDate).getFullYear().toString();

        const deviceId = localStorage.getItem('device_id');
        if (!deviceId) return;

        try {
            const startPos = resume ? (media.resumePositionSeconds || 0) : 0;
            const subId = selectedSubtitleId === 'none' ? '00000000-0000-0000-0000-000000000000' : selectedSubtitleId;

            const sessionInfo = await streamingService.startSession(media.id, deviceId, startPos, selectedVideoId || undefined, selectedAudioId || undefined, subId, serverId);

            const activePart = media.mediaParts?.find(p => p.videoTracks?.some(v => v.id === selectedVideoId)) || media.mediaParts?.[0];
            const activeVideoTrack = activePart?.videoTracks?.find(v => v.id === selectedVideoId) || activePart?.videoTracks?.[0];
            const activeAudioTrack = activePart?.audioTracks?.find(a => a.id === selectedAudioId) || activePart?.audioTracks?.[0];

            playMedia({
                id: media.id, title: media.title, subtitle: subtitle,
                posterUrl: media.posterUrl, backgroundUrl: media.backgroundUrl,
                ...sessionInfo, startPosition: startPos,
                serverId: serverId ?? undefined,
                resolution: activePart?.resolution, hdrType: activeVideoTrack?.hdrType,
                audioCodec: activeAudioTrack?.codec, audioChannels: activeAudioTrack?.channels,
                playbackContextType: media.type === 'Episode' ? 'tvshow' : 'movie'
            });
        } catch {
            await dialog.alert('Failed to start playback session.');
        }
    };

    const handleScan = async () => {
        if (!media) return;
        try {
            await libraryAdminService.triggerMediaScan(media.id);
            await dialog.alert('Targeted folder scan started in the background!');
        } catch {
            await dialog.alert('Failed to start scan.');
        }
        setShowMenu(false);
    };

    const handleRefresh = async () => {
        if (!media) return;
        libraryAdminService.refreshItemMetadata(media.id, true);
        await dialog.alert('Metadata refresh started in the background!');
        setShowMenu(false);
    };

    const handleAnalyze = async () => {
        if (!media) return;
        libraryAdminService.analyzeMedia(media.id);
        await dialog.alert('Media analysis started in the background!');
        setShowMenu(false);
    };

    const handleDelete = async () => {
        if (!media) return;
        if (await dialog.confirm({
            title: 'Delete media item?',
            message: 'This permanently removes the item from Vora. Your physical files on disk will NOT be touched.',
            confirmText: 'Delete',
            tone: 'danger',
        })) {
            try {
                await libraryAdminService.deleteMediaItem(media.id);
                navigate(media.libraryId ? (serverId ? `/server/${serverId}/library/${media.libraryId}` : `/library/${media.libraryId}`) : '/');
            } catch {
                await dialog.alert('Failed to delete media item.');
            }
        }
        setShowMenu(false);
    };

    const handleTogglePlayed = async () => {
        if (!media) return;
        try {
            await mediaService.markAsPlayed(media.id, !isFullyPlayed, serverId);
            reloadMedia();
        } catch {
            await dialog.alert('Failed to update play state.');
        }
    };

    const handleVideoChange = (newVideoId: string) => {
        setSelectedVideoId(newVideoId);
        const newPart = media?.mediaParts?.find(p => p.videoTracks?.some(v => v.id === newVideoId));

        if (newPart) {
            const hasCurrentAudio = newPart.audioTracks?.some(a => a.id === selectedAudioId);
            if (!hasCurrentAudio && newPart.audioTracks?.length) setSelectedAudioId(newPart.audioTracks[0].id);

            const hasCurrentSub = selectedSubtitleId === 'none' || newPart.subtitleTracks?.some(s => s.id === selectedSubtitleId);
            if (!hasCurrentSub && newPart.subtitleTracks?.length) {
                const bestSub = newPart.subtitleTracks.find(s => s.isForced || s.isDefault);
                setSelectedSubtitleId(bestSub ? bestSub.id : 'none');
            }
        }
    };

    if (loading) {
        return (
            <div>
                <div className="vora-skeleton h-[70vh] min-h-[540px]" />
                <div className="px-12 pt-8">
                    <div className="vora-skeleton mb-3 h-10 w-2/3" />
                    <div className="vora-skeleton mb-6 h-6 w-1/2" />
                    <div className="vora-skeleton mb-4 h-14 w-44" />
                </div>
            </div>
        );
    }
    if (!media) {
        return (
            <EmptyState
                title="Media not found"
                description="This title doesn't exist or you don't have access to it."
            />
        );
    }

    const activePart = media.mediaParts?.find(p => p.videoTracks?.some(v => v.id === selectedVideoId)) || media.mediaParts?.[0];
    const resumePos = media.resumePositionSeconds || 0;
    const inProgress = resumePos > 0 && !media.isPlayed;
    const isFullyPlayed = media.type === 'Episode' || media.type === 'Movie' ? media.isPlayed : media.unplayedItemCount === 0 && (media.episodes?.length || 0) > 0;

    const sortedVideoTracks = media.mediaParts?.flatMap(p =>
        (p.videoTracks || []).map(v => ({ part: p, track: v }))
    ).sort((a, b) => {
        const aIs4k = a.part.resolution?.toLowerCase().includes('4k') || a.part.resolution?.includes('2160') ? 1 : 0;
        const bIs4k = b.part.resolution?.toLowerCase().includes('4k') || b.part.resolution?.includes('2160') ? 1 : 0;
        if (aIs4k !== bIs4k) return bIs4k - aIs4k;
        return a.track.isDefault === b.track.isDefault ? 0 : a.track.isDefault ? -1 : 1;
    }) || [];

    const sortedAudioTracks = activePart?.audioTracks
        ? [...activePart.audioTracks].sort((a, b) => (b.channels || 0) - (a.channels || 0))
        : [];

    const videoOptions: QualityOption<string>[] = sortedVideoTracks.map(v => {
        const displayRes = v.part.resolution === '2160p' ? '4K' : (v.part.resolution || 'Unknown');
        return {
            value: v.track.id,
            label: `${displayRes} · ${v.track.codec?.toUpperCase() ?? '—'}`,
            sublabel: [v.track.hdrType, v.track.isDefault ? 'Default' : null].filter(Boolean).join(' · ') || undefined,
        };
    });
    const audioOptions: QualityOption<string>[] = sortedAudioTracks.map(a => ({
        value: a.id,
        label: `${a.language || 'Unknown'} · ${a.codec?.toUpperCase() ?? '—'}${a.channels ? ` · ${a.channels}ch` : ''}`,
        sublabel: [a.title, a.isDefault ? 'Default' : null].filter(Boolean).join(' · ') || undefined,
    }));
    const subtitleOptions: QualityOption<string>[] = [
        { value: 'none', label: 'Off' },
        ...(activePart?.subtitleTracks ?? []).map(s => ({
            value: s.id,
            label: s.language || 'Unknown',
            sublabel: [s.title, s.isForced ? 'Forced' : null, s.isDefault ? 'Default' : null].filter(Boolean).join(' · ') || undefined,
        })),
    ];

    let nextEpisode: UpcomingEpisodeParsed | null = null;
    if (media.upcomingEpisodesJson && media.upcomingEpisodesJson !== '[]') {
        try {
            const upcoming: UpcomingEpisodeParsed[] = JSON.parse(media.upcomingEpisodesJson);
            const futureEps = upcoming.filter((ep) => new Date(ep.AirDate) >= new Date());
            if (futureEps.length > 0) {
                nextEpisode = futureEps.sort((a, b) => new Date(a.AirDate).getTime() - new Date(b.AirDate).getTime())[0];
            } else if (upcoming.length > 0) {
                nextEpisode = upcoming[0];
            }
        } catch (e: unknown) {
            console.error('Failed to parse upcoming episodes:', e);
        }
    }

    const isEpisode = media.type === 'Episode';
    const isSeason = media.type === 'Season';
    const heroTitle = (isSeason || isEpisode) && media.tvShowTitle ? media.tvShowTitle : media.title;
    const heroSubtitle = (isSeason || isEpisode) ? media.title : undefined;
    const showQualityButton = (media.type === 'Movie' || isEpisode) && (sortedVideoTracks.length > 1 || sortedAudioTracks.length > 1 || (activePart?.subtitleTracks?.length ?? 0) > 0);
    const playLabel = media.type === 'TvShow' ? 'Play next' : inProgress ? 'Resume' : 'Play';
    const playRuntime = formatRuntime(media.durationMinutes);

    return (
        <div className="relative min-h-full pb-20">
            <EditMetadataModal isOpen={isEditModalOpen} onClose={() => setIsEditModalOpen(false)} onSaved={reloadMedia} itemId={media.id} type="media" initialData={media} />
            <AddToCollectionModal isOpen={isCollectionModalOpen} onClose={() => setIsCollectionModalOpen(false)} mediaId={media.id} libraryId={media.libraryId} mediaType={media.type} initialCollectionIds={media.collectionIds || []} onSaved={reloadMedia} />
            <AddToPlaylistModal isOpen={isPlaylistModalOpen} onClose={() => setIsPlaylistModalOpen(false)} mediaId={media.id} />

            <QualityPanel open={isQualityPanelOpen} onClose={() => setIsQualityPanelOpen(false)}>
                {videoOptions.length > 0 && (
                    <QualityPanelSection title="Video" options={videoOptions} value={selectedVideoId} onChange={handleVideoChange} />
                )}
                {audioOptions.length > 0 && (
                    <QualityPanelSection title="Audio" options={audioOptions} value={selectedAudioId} onChange={setSelectedAudioId} />
                )}
                <QualityPanelSection title="Subtitles" options={subtitleOptions} value={selectedSubtitleId} onChange={setSelectedSubtitleId} />
            </QualityPanel>

            <div className="absolute inset-x-0 top-0 z-0">
                <CinematicBackdrop src={media.backgroundUrl || media.posterUrl} intensity="detail" parallax transitionKey={media.id} />
            </div>

            <div className="relative z-10 pt-8">
                <div className="px-12">
                    <button
                        type="button"
                        onClick={() => navigate(-1)}
                        className="inline-flex cursor-pointer items-center gap-2 rounded-full px-3 py-1.5 text-sm font-medium backdrop-blur-md transition-colors hover:bg-[rgba(20,20,28,0.85)]"
                        style={{ background: 'rgba(20, 20, 28, 0.65)', border: '1px solid rgba(255, 255, 255, 0.14)', color: '#fafafa' }}
                    >
                        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><polyline points="15 18 9 12 15 6" /></svg>
                        {media.tvShowTitle ? `Back to ${media.tvShowTitle}` : 'Back'}
                    </button>
                </div>

                <div className="mt-12 grid gap-10 px-12 md:grid-cols-[260px_1fr]">
                    <div className="shrink-0">
                        <div
                            className={`relative overflow-hidden ${isEpisode ? 'aspect-video' : 'aspect-[2/3]'}`}
                            style={{
                                borderRadius: 'var(--vora-radius-lg)',
                                boxShadow: 'var(--vora-shadow-lg)',
                                border: '1px solid var(--vora-border-subtle)',
                                background: 'var(--vora-bg-surface)',
                                maxWidth: isEpisode ? 400 : 260,
                            }}
                        >
                            {media.posterUrl ? (
                                <img src={media.posterUrl} alt={media.title} className={`h-full w-full ${isEpisode ? 'object-contain' : 'object-cover'}`} />
                            ) : (
                                <div className="flex h-full w-full items-center justify-center" style={{ color: 'var(--vora-text-muted)' }}>
                                    No image
                                </div>
                            )}
                        </div>
                    </div>

                    <div className="min-w-0">
                        <div className="text-xs font-semibold uppercase tracking-wider" style={{ color: 'var(--vora-accent-text)' }}>
                            {media.type === 'Movie' && 'Movie'}
                            {media.type === 'TvShow' && 'TV Series'}
                            {isSeason && `Season ${media.seasonNumber ?? ''}`}
                            {isEpisode && `Season ${media.seasonNumber} · Episode ${media.episodeNumber}`}
                            {media.releaseDate && ` · ${new Date(media.releaseDate).getFullYear()}`}
                        </div>

                        <h1 className="m-0 mt-2 font-semibold" style={{ color: 'var(--vora-text-primary)', fontSize: 'clamp(32px, 4vw, 44px)', lineHeight: 1.05, letterSpacing: '-0.02em' }}>
                            {heroTitle}
                        </h1>
                        {heroSubtitle && (
                            <div className="mt-1 text-xl" style={{ color: 'var(--vora-text-secondary)' }}>
                                {heroSubtitle}
                            </div>
                        )}

                        <div className="mt-4 flex flex-wrap items-center gap-2">
                            {playRuntime && <span className="rounded-md px-2.5 py-1 text-xs font-medium backdrop-blur-md" style={{ background: 'rgba(8, 8, 11, 0.6)', border: '1px solid rgba(255, 255, 255, 0.16)', color: '#fafafa' }}>{playRuntime}</span>}
                            {media.contentRating && <span className="rounded-md px-2.5 py-1 text-xs font-medium backdrop-blur-md" style={{ background: 'rgba(8, 8, 11, 0.6)', border: '1px solid rgba(255, 255, 255, 0.16)', color: '#fafafa' }}>{media.contentRating}</span>}
                            {activePart?.resolution && <span className="rounded-md px-2.5 py-1 text-xs font-semibold backdrop-blur-md" style={{ background: 'var(--vora-accent-500)', border: '1px solid var(--vora-accent-500)', color: 'var(--vora-accent-contrast)' }}>{activePart.resolution === '2160p' ? '4K' : activePart.resolution}</span>}
                            {sortedAudioTracks[0]?.codec && <span className="rounded-md px-2.5 py-1 text-xs font-medium backdrop-blur-md" style={{ background: 'rgba(8, 8, 11, 0.6)', border: '1px solid rgba(255, 255, 255, 0.16)', color: '#fafafa' }}>{sortedAudioTracks[0].codec.toUpperCase()}{sortedAudioTracks[0].channels ? ` ${sortedAudioTracks[0].channels}ch` : ''}</span>}
                        </div>

                        <div className="mt-6 flex flex-wrap items-center gap-3">
                            <button
                                type="button"
                                onClick={() => handlePlay(true)}
                                className="vora-button-primary cursor-pointer"
                                style={{ display: 'inline-flex', alignItems: 'center', gap: 10, padding: '14px 30px', fontSize: 15 }}
                            >
                                <svg width="18" height="18" viewBox="0 0 24 24" fill="currentColor"><polygon points="5 3 19 12 5 21 5 3" /></svg>
                                {playLabel}
                                {inProgress && resumePos > 0 && (
                                    <span className="ml-1 text-xs font-normal opacity-70">
                                        {Math.floor(resumePos / 60)}:{String(Math.floor(resumePos % 60)).padStart(2, '0')}
                                    </span>
                                )}
                            </button>

                            {inProgress && (
                                <button
                                    type="button"
                                    onClick={() => handlePlay(false)}
                                    title="Start over"
                                    aria-label="Start over"
                                    className="cursor-pointer inline-flex h-12 w-12 items-center justify-center rounded-md backdrop-blur-md transition-colors hover:bg-[rgba(20,20,28,0.85)]"
                                    style={{ background: 'rgba(20, 20, 28, 0.72)', border: '1px solid rgba(255, 255, 255, 0.18)', color: '#fafafa' }}
                                >
                                    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M11 19l-7-7 7-7m8 14l-7-7 7-7" /></svg>
                                </button>
                            )}

                            {showQualityButton && (
                                <button
                                    type="button"
                                    onClick={() => setIsQualityPanelOpen(true)}
                                    className="cursor-pointer inline-flex items-center gap-2 rounded-md px-5 py-3 text-sm font-medium backdrop-blur-md transition-colors hover:bg-[rgba(20,20,28,0.85)]"
                                    style={{ background: 'rgba(20, 20, 28, 0.72)', border: '1px solid rgba(255, 255, 255, 0.18)', color: '#fafafa' }}
                                >
                                    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><circle cx="12" cy="12" r="3" /><path d="M19.4 15a1.6 1.6 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.6 1.6 0 0 0-1.8-.3" /></svg>
                                    Quality &amp; tracks
                                </button>
                            )}

                            <button
                                type="button"
                                onClick={() => setIsPlaylistModalOpen(true)}
                                title="Add to playlist"
                                aria-label="Add to playlist"
                                className="cursor-pointer inline-flex h-12 w-12 items-center justify-center rounded-md backdrop-blur-md transition-colors hover:bg-[rgba(20,20,28,0.85)]"
                                style={{ background: 'rgba(20, 20, 28, 0.72)', border: '1px solid rgba(255, 255, 255, 0.18)', color: '#fafafa' }}
                            >
                                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><line x1="4" y1="6" x2="20" y2="6" /><line x1="4" y1="12" x2="20" y2="12" /><line x1="4" y1="18" x2="14" y2="18" /><polygon points="17 16 22 19 17 22 17 16" fill="currentColor" /></svg>
                            </button>

                            <button
                                type="button"
                                onClick={handleTogglePlayed}
                                title={isFullyPlayed ? 'Mark as unwatched' : 'Mark as watched'}
                                aria-label={isFullyPlayed ? 'Mark as unwatched' : 'Mark as watched'}
                                className="cursor-pointer inline-flex h-12 w-12 items-center justify-center rounded-md backdrop-blur-md transition-colors"
                                style={{
                                    background: isFullyPlayed ? 'var(--vora-accent-500)' : 'rgba(20, 20, 28, 0.72)',
                                    color: isFullyPlayed ? 'var(--vora-accent-contrast)' : '#fafafa',
                                    border: `1px solid ${isFullyPlayed ? 'var(--vora-accent-500)' : 'rgba(255, 255, 255, 0.18)'}`,
                                }}
                            >
                                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={isFullyPlayed ? 3 : 2}><polyline points="20 6 9 17 4 12" /></svg>
                            </button>

                            <div className="relative">
                                <button
                                    type="button"
                                    onClick={() => setShowMenu(s => !s)}
                                    aria-label="More actions"
                                    className="cursor-pointer inline-flex h-12 w-12 items-center justify-center rounded-md backdrop-blur-md transition-colors hover:bg-[rgba(20,20,28,0.85)]"
                                    style={{ background: 'rgba(20, 20, 28, 0.72)', border: '1px solid rgba(255, 255, 255, 0.18)', color: '#fafafa', padding: 0 }}
                                >
                                    <svg width="18" height="18" viewBox="0 0 24 24" fill="currentColor"><path d="M12 8c1.1 0 2-.9 2-2s-.9-2-2-2-2 .9-2 2 .9 2 2 2zm0 2c-1.1 0-2 .9-2 2s.9 2 2 2 2-.9 2-2-.9-2-2-2zm0 6c-1.1 0-2 .9-2 2s.9 2 2 2 2-.9 2-2-.9-2-2-2z" /></svg>
                                </button>
                                {showMenu && (
                                    <>
                                        <div className="fixed inset-0 z-40" onClick={() => setShowMenu(false)} />
                                        <div
                                            className="absolute right-0 mt-2 w-52 overflow-hidden rounded-xl z-50"
                                            style={{
                                                background: 'var(--vora-bg-raised)',
                                                border: '1px solid var(--vora-border-strong)',
                                                boxShadow: 'var(--vora-shadow-lg)',
                                            }}
                                        >
                                            <button type="button" onClick={() => { setIsEditModalOpen(true); setShowMenu(false); }} className="block w-full cursor-pointer px-4 py-2.5 text-left text-sm transition-colors hover:bg-white/5" style={{ color: 'var(--vora-text-primary)' }}>Edit metadata</button>
                                            <button type="button" onClick={() => { setIsCollectionModalOpen(true); setShowMenu(false); }} className="block w-full cursor-pointer px-4 py-2.5 text-left text-sm transition-colors hover:bg-white/5" style={{ color: 'var(--vora-text-primary)' }}>Add to collection</button>
                                            {isAdmin && (
                                                <>
                                                    <div className="border-t" style={{ borderColor: 'var(--vora-border-subtle)' }} />
                                                    <button type="button" onClick={handleRefresh} className="block w-full cursor-pointer px-4 py-2.5 text-left text-sm transition-colors hover:bg-white/5" style={{ color: 'var(--vora-text-primary)' }}>Refresh metadata</button>
                                                    <button type="button" onClick={handleScan} className="block w-full cursor-pointer px-4 py-2.5 text-left text-sm transition-colors hover:bg-white/5" style={{ color: 'var(--vora-text-primary)' }}>Scan files</button>
                                                    <button type="button" onClick={handleAnalyze} className="block w-full cursor-pointer px-4 py-2.5 text-left text-sm transition-colors hover:bg-white/5" style={{ color: 'var(--vora-text-primary)' }}>Analyze media</button>
                                                    <button type="button" onClick={handleDelete} className="block w-full cursor-pointer px-4 py-2.5 text-left text-sm font-medium transition-colors hover:bg-white/5" style={{ color: 'var(--vora-danger-text)' }}>Delete item</button>
                                                </>
                                            )}
                                        </div>
                                    </>
                                )}
                            </div>
                        </div>

                        {nextEpisode && (
                            <div
                                className="mt-7 flex items-center gap-4 rounded-xl p-4"
                                style={{
                                    background: 'var(--vora-info-soft)',
                                    border: '1px solid color-mix(in srgb, var(--vora-info-500) 35%, transparent)',
                                }}
                            >
                                <div className="shrink-0 rounded-md px-2.5 py-1.5 text-center text-[10px] font-bold uppercase tracking-wider" style={{ background: 'var(--vora-info-500)', color: 'var(--vora-text-inverse)' }}>
                                    <div className="opacity-80">Next</div>
                                    <div>episode</div>
                                </div>
                                <div>
                                    <p className="m-0 text-sm font-semibold" style={{ color: 'var(--vora-text-primary)' }}>
                                        Season {nextEpisode.SeasonNumber} · Episode {nextEpisode.EpisodeNumber} — {nextEpisode.Title}
                                    </p>
                                    <p className="m-0 mt-0.5 text-xs" style={{ color: 'var(--vora-info-text)' }}>
                                        Airs {new Date(nextEpisode.AirDate).toLocaleDateString(undefined, { weekday: 'long', year: 'numeric', month: 'long', day: 'numeric' })}
                                    </p>
                                </div>
                            </div>
                        )}

                        <p className="mt-7 max-w-3xl text-[15px] leading-relaxed" style={{ color: 'var(--vora-text-secondary)' }}>
                            {media.overview || 'No overview available.'}
                        </p>
                    </div>
                </div>

                {media.type === 'TvShow' && (media.seasons || []).length > 0 && (
                    <div className="mt-12">
                        <MediaRail title="Seasons">
                            {(media.seasons || []).map(season => {
                                const onOpen = () => navigate(serverId ? `/server/${serverId}/media/${season.id}` : `/media/${season.id}`);
                                const unplayedBadge = season.unplayedItemCount && season.unplayedItemCount > 0
                                    ? <span className="rounded-full px-2 py-0.5 text-[10px] font-bold" style={{ background: 'var(--vora-accent-500)', color: 'var(--vora-accent-contrast)' }}>{season.unplayedItemCount}</span>
                                    : undefined;
                                return (
                                    <div key={season.id} style={{ scrollSnapAlign: 'start', flex: 'none' }}>
                                        <MediaPoster
                                            imageUrl={season.posterUrl}
                                            title={season.title || `Season ${season.seasonNumber}`}
                                            subtitle={`${season.episodeCount || 0} episode${season.episodeCount === 1 ? '' : 's'}`}
                                            onClick={onOpen}
                                            badge={unplayedBadge}
                                        />
                                    </div>
                                );
                            })}
                        </MediaRail>
                    </div>
                )}

                <div className="mt-12 px-12">
                    <MediaCastRow cast={media.cast || []} serverId={serverId} />
                    <MediaExtrasRow videos={media.videos || []} />
                    <MediaEpisodesList episodes={media.episodes || []} serverId={serverId} />
                </div>
            </div>
        </div>
    );
}
