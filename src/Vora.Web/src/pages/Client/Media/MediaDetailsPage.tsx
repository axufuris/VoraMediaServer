import { useEffect, useState, useCallback, useMemo } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { mediaService, type MediaItem, type MediaPart } from '../../../api/Media/mediaService';
import { libraryAdminService } from '../../../api/Media/libraryAdminService';
import EditMetadataModal from '../../../components/Media/EditMetadataModal';
import MarkerEditorModal from '../../../components/Admin/MarkerEditorModal';
import AddToCollectionModal from '../../../components/Collections/AddToCollectionModal';
import AddToPlaylistModal from '../../../components/Collections/AddToPlaylistModal';
import CastRow from '../../../components/Client/Primitives/CastRow';
import MediaExtrasRow from '../../../components/Media/MediaExtrasRow';
import MediaEpisodesList from '../../../components/Media/MediaEpisodesList';
import { useSignalREvent } from '../../../hooks/useSignalREvent';
import { usePlayer } from '../../../contexts/usePlayer';
import { streamingService } from '../../../api/Streaming/streamingService';
import { getEffectiveCapabilities } from '../../../utils/hardwareScanner';
import { isVideoDirectPlayable, isAudioDirectPlayable, parseResolutionHeight } from '../../../utils/playbackDecision';
import { useDialog } from '../../../dialogs';
import MediaCard from '../../../components/Client/Primitives/MediaCard';
import DetailHero, { HeroChip, HeroCredits, HeroIconButton } from '../../../components/Client/Primitives/DetailHero';
import RatingBadge from '../../../components/Client/Primitives/RatingBadge';
import TrailerOverlay, { type TrailerSource } from '../../../components/Client/Primitives/TrailerOverlay';
import { PlayIcon, RestartIcon, GearIcon, FilmReelIcon, PencilIcon, CheckIcon, MoreIcon } from '../../../components/Client/Primitives/ActionIcons';
import { directorsFrom } from '../../../utils/credits';
import MediaRow, { MediaRowItem } from '../../../components/Client/Primitives/MediaRow';
import EmptyState from '../../../components/Client/Primitives/EmptyState';
import QualityPanel, { QualityPanelSection, type QualityOption } from '../../../components/Client/Primitives/QualityPanel';
import StarRating from '../../../components/Client/Primitives/StarRating';
import { StorageKeys, getProfileIdFromToken } from '../../../utils/storageKeys';
import { formatRuntime } from '../../../utils/formatRuntime';

interface UpcomingEpisodeParsed {
    SeasonNumber: number;
    EpisodeNumber: number;
    Title: string;
    AirDate: string;
}

const audioCodecRank = (codec?: string): number => {
    switch ((codec ?? '').toLowerCase()) {
        case 'truehd': return 6;
        case 'dts':
        case 'flac': return 5;
        case 'eac3': return 4;
        case 'ac3': return 3;
        case 'aac':
        case 'opus': return 2;
        case 'mp3': return 1;
        default: return 0;
    }
};

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
    const [playingTrailer, setPlayingTrailer] = useState<TrailerSource | null>(null);
    const [isMarkerEditorOpen, setIsMarkerEditorOpen] = useState(false);
    const [isQualityPanelOpen, setIsQualityPanelOpen] = useState(false);

    const [selectedVideoId, setSelectedVideoId] = useState<string>('');
    const [selectedAudioId, setSelectedAudioId] = useState<string>('');
    const [selectedSubtitleId, setSelectedSubtitleId] = useState<string>('none');

    const [qualityMedia, setQualityMedia] = useState<MediaItem | null>(null);

    const [thumbnailsLocked, setThumbnailsLocked] = useState<boolean | null>(null);

    const isAdmin = localStorage.getItem(StorageKeys.isServerAdmin) === 'true';
    const caps = useMemo(() => {
        const profileToken = localStorage.getItem(StorageKeys.profileToken);
        const profileId = (profileToken ? getProfileIdFromToken(profileToken) : null) || 'unknown';
        const deviceId = localStorage.getItem(StorageKeys.deviceId) || 'unknown';
        return getEffectiveCapabilities(profileId, deviceId);
    }, []);
    const { playMedia, isPlaying } = usePlayer();

    // Pick the audio track the client can play directly under the effective
    // caps: prefer a directly-playable track (codec + channels within the
    // Max Audio Channels cap), then prefer more channels among the playable
    // ones (commentary tracks deprioritized). Never picks on IsDefault alone.
    const pickBestAudioId = useCallback((part?: MediaPart): string => {
        if (!part?.audioTracks?.length) return '';
        let bestId = '';
        let lowest = Number.POSITIVE_INFINITY;
        for (const track of part.audioTracks) {
            let penalty = 0;
            const channels = track.channels || 2;
            if (!isAudioDirectPlayable(track, caps)) penalty += 1000;
            penalty -= channels * 10;
            penalty -= audioCodecRank(track.codec);
            if (track.title?.toLowerCase().includes('commentary')) penalty += 500;
            if (penalty < lowest) { lowest = penalty; bestId = track.id; }
        }
        return bestId;
    }, [caps]);

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

        // Navigating between items (e.g. episode → season) reuses this component,
        // so close any open overlay/menu from the previous item.
        setIsQualityPanelOpen(false);
        setShowMenu(false);

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
        if (!media) { setQualityMedia(null); return; }
        if (media.type !== 'TvShow') { setQualityMedia(media); return; }

        let cancelled = false;
        setQualityMedia(null);
        (async () => {
            try {
                const upNext = await mediaService.getUpNext(media.id, undefined, undefined, serverId);
                if (cancelled || !upNext.nextItem) return;
                const episode = await mediaService.getMediaItem(upNext.nextItem.id, serverId);
                if (!cancelled) setQualityMedia(episode);
            } catch {
                if (!cancelled) setQualityMedia(null);
            }
        })();
        return () => { cancelled = true; };
    }, [media, serverId]);

    useEffect(() => {
        if (qualityMedia?.mediaParts?.length) {
            const parts = qualityMedia.mediaParts;
            const partHeight = (p: MediaPart) => parseResolutionHeight(p.resolution);

            // Prefer the highest-resolution part that fits under the effective
            // Max Resolution cap (unknown height counts as fitting). If every
            // part exceeds the cap, still pick the highest — the server will
            // transcode it down.
            const withinCap = parts.filter(p => partHeight(p) === 0 || partHeight(p) <= caps.requestedMaxResolution);
            const pool = withinCap.length ? withinCap : parts;
            let winningPart = pool[0];
            for (const p of pool) {
                if (partHeight(p) > partHeight(winningPart)) winningPart = p;
            }

            const videoTracks = winningPart.videoTracks || [];
            const directVideo = videoTracks.find(t => isVideoDirectPlayable(t, winningPart, caps));
            const defaultVideo = videoTracks.find(t => t.isDefault) || videoTracks[0];
            const bestVideoId = (directVideo || defaultVideo)?.id || '';

            const bestAudioId = pickBestAudioId(winningPart);

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
    }, [qualityMedia, caps]);

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

    useEffect(() => {
        if (!media || !isAdmin) return;
        if (media.type !== 'Movie' && media.type !== 'Episode') return;
        libraryAdminService.getThumbnailsLock(media.id, serverId)
            .then(r => setThumbnailsLocked(r.locked))
            .catch(() => setThumbnailsLocked(null));
    }, [media, isAdmin, serverId]);

    const handlePlay = async (resume: boolean = true) => {
        if (!media) return;

        let subtitle = '';
        if (media.type === 'Episode') {
            const epLabel = media.endEpisodeNumber && media.endEpisodeNumber > (media.episodeNumber ?? 0)
                ? `E${media.episodeNumber}-E${media.endEpisodeNumber}`
                : `E${media.episodeNumber}`;
            subtitle = `S${media.seasonNumber} ${epLabel} - ${media.tvShowTitle}`;
        }
        else if (media.releaseDate) subtitle = new Date(media.releaseDate).getFullYear().toString();

        const deviceId = localStorage.getItem(StorageKeys.deviceId);
        if (!deviceId) return;

        try {
            const startPos = resume ? (media.resumePositionSeconds || 0) : 0;
            const subId = selectedSubtitleId === 'none' ? '00000000-0000-0000-0000-000000000000' : selectedSubtitleId;

            const activePart = qualityMedia?.mediaParts?.find(p => p.videoTracks?.some(v => v.id === selectedVideoId)) || qualityMedia?.mediaParts?.[0];

            const sessionInfo = await streamingService.startSession(media.id, deviceId, startPos, selectedVideoId || undefined, selectedAudioId || undefined, subId, serverId, activePart?.id);

            const activeVideoTrack = activePart?.videoTracks?.find(v => v.id === selectedVideoId) || activePart?.videoTracks?.[0];
            const activeAudioTrack = activePart?.audioTracks?.find(a => a.id === selectedAudioId) || activePart?.audioTracks?.[0];

            playMedia({
                id: media.id, title: media.title, subtitle: subtitle,
                posterUrl: media.posterUrl, backgroundUrl: media.backgroundUrl,
                ...sessionInfo, startPosition: startPos,
                serverId: serverId ?? undefined,
                resolution: activePart?.resolution, hdrType: activeVideoTrack?.hdrType,
                outputResolution: sessionInfo.outputResolution ?? undefined,
                outputHdrType: sessionInfo.outputHdrType ?? undefined,
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

    const handleRegenerateThumbnails = async () => {
        if (!media) return;
        try {
            await libraryAdminService.regenerateMediaItemThumbnails(media.id, serverId);
            await dialog.alert('Thumbnail regeneration started in the background!');
        } catch {
            await dialog.alert('Failed to queue thumbnail regeneration.');
        }
        setShowMenu(false);
    };

    const handleToggleThumbnailsLock = async () => {
        if (!media) return;
        try {
            const next = !(thumbnailsLocked ?? false);
            const result = await libraryAdminService.setThumbnailsLock(media.id, next, serverId);
            setThumbnailsLocked(result.locked);
        } catch {
            await dialog.alert('Failed to update thumbnails lock.');
        }
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

    const handleSetRating = useCallback(async (next: number | null) => {
        if (!media) return;
        setMedia(prev => prev ? {
            ...prev,
            myRating: next ?? undefined,
            serverAdminRating: isAdmin ? (next ?? undefined) : prev.serverAdminRating,
        } : prev);
        try {
            await mediaService.setRating(media.id, next, serverId);
        } catch {
            await dialog.alert('Failed to update rating.');
            reloadMedia();
        }
    }, [media, isAdmin, serverId, dialog, reloadMedia]);

    const handleVersionChange = (partId: string) => {
        const part = qualityMedia?.mediaParts?.find(p => p.id === partId);
        if (!part) return;
        const bestVideo = part.videoTracks?.find(v => v.isDefault) || part.videoTracks?.[0];
        if (bestVideo) handleVideoChange(bestVideo.id);
    };

    const handleVideoChange = (newVideoId: string) => {
        setSelectedVideoId(newVideoId);
        const newPart = qualityMedia?.mediaParts?.find(p => p.videoTracks?.some(v => v.id === newVideoId));

        if (newPart) {
            // Switching version/video always re-selects the audio the client can
            // play; only a manual audio pick keeps a track the client can't.
            if (newPart.audioTracks?.length) setSelectedAudioId(pickBestAudioId(newPart));

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

    const activePart = qualityMedia?.mediaParts?.find(p => p.videoTracks?.some(v => v.id === selectedVideoId)) || qualityMedia?.mediaParts?.[0];
    const resumePos = media.resumePositionSeconds || 0;
    const inProgress = resumePos > 0 && !media.isPlayed;
    const isFullyPlayed = media.type === 'Episode' || media.type === 'Movie' ? media.isPlayed : media.unplayedItemCount === 0 && (media.episodes?.length || 0) > 0;

    const sortedVideoTracks = qualityMedia?.mediaParts?.flatMap(p =>
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
        const playbackBadge = isVideoDirectPlayable(v.track, v.part, caps) ? 'Direct Play' : 'Transcode';
        return {
            value: v.track.id,
            label: `${displayRes} · ${v.track.codec?.toUpperCase() ?? '—'}`,
            sublabel: [v.track.hdrType, v.track.isDefault ? 'Default' : null, playbackBadge].filter(Boolean).join(' · ') || undefined,
        };
    });
    const audioOptions: QualityOption<string>[] = sortedAudioTracks.map(a => ({
        value: a.id,
        label: `${a.language || 'Unknown'} · ${a.codec?.toUpperCase() ?? '—'}${a.channels ? ` · ${a.channels}ch` : ''}`,
        sublabel: [a.title, a.isDefault ? 'Default' : null, isAudioDirectPlayable(a, caps) ? 'Direct Play' : 'Transcode'].filter(Boolean).join(' · ') || undefined,
    }));
    const subtitleOptions: QualityOption<string>[] = [
        { value: 'none', label: 'Off' },
        ...(activePart?.subtitleTracks ?? []).map(s => ({
            value: s.id,
            label: s.language || 'Unknown',
            sublabel: [s.title, s.isForced ? 'Forced' : null, s.isDefault ? 'Default' : null].filter(Boolean).join(' · ') || undefined,
        })),
    ];
    const versionOptions: QualityOption<string>[] = [...(qualityMedia?.mediaParts ?? [])]
        .sort((a, b) => parseResolutionHeight(b.resolution) - parseResolutionHeight(a.resolution) || (b.bitrateKbps ?? 0) - (a.bitrateKbps ?? 0))
        .map((p, i) => {
            const displayRes = p.resolution === '2160p' ? '4K' : (p.resolution || `Version ${i + 1}`);
            const partVideo = (p.videoTracks || []).find(t => t.isDefault) || (p.videoTracks || [])[0];
            const playbackBadge = partVideo && isVideoDirectPlayable(partVideo, p, caps) ? 'Direct Play' : 'Transcode';
            return {
                value: p.id,
                label: p.edition || displayRes,
                sublabel: [p.edition ? displayRes : null, p.bitrateKbps ? `${Math.round(p.bitrateKbps / 1000)} Mbps` : null, playbackBadge].filter(Boolean).join(' · ') || undefined,
            };
        });

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
    const showParentNav = (isEpisode && !!media.seasonId) || (isSeason && !!media.tvShowId);
    // Return to wherever the user came from. When there is no in-app history to
    // go back to (a direct/deep link opened the page), fall back to the parent
    // show so Back never drops the user out of the app.
    const goBack = () => {
        const hasHistory = ((window.history.state as { idx?: number } | null)?.idx ?? 0) > 0;
        if (!hasHistory && media.tvShowId) {
            navigate(serverId ? `/server/${serverId}/media/${media.tvShowId}` : `/media/${media.tvShowId}`);
            return;
        }
        navigate(-1);
    };
    const heroTitle = (isSeason || isEpisode) && media.tvShowTitle ? media.tvShowTitle : media.title;
    const heroSubtitle = (isSeason || isEpisode) ? media.title : undefined;
    const showQualityButton = (media.type === 'Movie' || isEpisode || media.type === 'TvShow') && (versionOptions.length > 1 || sortedVideoTracks.length > 1 || sortedAudioTracks.length > 1 || (activePart?.subtitleTracks?.length ?? 0) > 0);
    const playLabel = media.type === 'TvShow' ? 'Play next' : inProgress ? 'Resume' : 'Play';
    const playRuntime = formatRuntime(media.durationMinutes);

    const videos = media.videos ?? [];
    const trailerVideo = videos.find(v => v.type === 'Trailer') ?? videos[0];
    const trailer: TrailerSource | null = trailerVideo
        ? { name: trailerVideo.name ?? 'Trailer', videoKey: trailerVideo.videoKey, site: trailerVideo.site }
        : null;

    const heroEyebrow = [
        isEpisode ? 'Episode' : isSeason ? `Season ${media.seasonNumber ?? ''}`.trim() : media.type === 'TvShow' ? 'TV Series' : 'Movie',
        media.releaseDate ? String(new Date(media.releaseDate).getFullYear()) : null,
    ].filter(Boolean).join(' · ');

    const heroChips = (
        <>
            {playRuntime && <HeroChip>{playRuntime}</HeroChip>}
            {media.contentRating && <HeroChip>{media.contentRating}</HeroChip>}
            {activePart?.resolution && <HeroChip tone="accent">{activePart.resolution === '2160p' ? '4K' : activePart.resolution}</HeroChip>}
            {sortedAudioTracks[0]?.codec && <HeroChip>{sortedAudioTracks[0].codec.toUpperCase()}{sortedAudioTracks[0].channels ? ` ${sortedAudioTracks[0].channels}ch` : ''}</HeroChip>}
        </>
    );

    const heroRatings = (
        <>
            <div className="flex items-center gap-2">
                <StarRating
                    value={media.myRating ?? null}
                    onChange={handleSetRating}
                    showNumeric
                    title={media.myRating != null ? 'Click a star to change, or click the same star to clear' : 'Rate this title'}
                />
            </div>
            {media.serverAdminRating != null && !isAdmin && (
                <div className="flex items-center gap-2">
                    <span className="text-[11px] font-semibold uppercase tracking-wider" style={{ color: 'var(--vora-text-muted)' }}>Server admin</span>
                    <StarRating value={media.serverAdminRating} readOnly showNumeric color="var(--vora-accent-text)" />
                </div>
            )}
            {media.thirdPartyRating1 != null && <RatingBadge value={media.thirdPartyRating1} name={media.thirdPartyRating1Name} />}
            {media.thirdPartyRating2 != null && <RatingBadge value={media.thirdPartyRating2} name={media.thirdPartyRating2Name} />}
        </>
    );

    const heroActions = (
        <>
            <button
                type="button"
                onClick={() => handlePlay(true)}
                className="vora-button-primary cursor-pointer"
                style={{ display: 'inline-flex', alignItems: 'center', gap: 10, padding: '14px 30px', fontSize: 15 }}
            >
                <PlayIcon />
                {playLabel}
                {inProgress && resumePos > 0 && (
                    <span className="ml-1 text-xs font-normal opacity-70">
                        {Math.floor(resumePos / 60)}:{String(Math.floor(resumePos % 60)).padStart(2, '0')}
                    </span>
                )}
            </button>

            {inProgress && (
                <HeroIconButton label="Start over" onClick={() => handlePlay(false)}>
                    <RestartIcon />
                </HeroIconButton>
            )}

            {trailer && (
                <HeroIconButton label="Play trailer" onClick={() => setPlayingTrailer(trailer)}>
                    <FilmReelIcon />
                </HeroIconButton>
            )}

            <HeroIconButton
                label={isFullyPlayed ? 'Mark as unwatched' : 'Mark as watched'}
                active={isFullyPlayed}
                onClick={handleTogglePlayed}
            >
                <CheckIcon bold={isFullyPlayed} />
            </HeroIconButton>

            {showQualityButton && (
                <HeroIconButton label="Quality & tracks" onClick={() => setIsQualityPanelOpen(true)}>
                    <GearIcon />
                </HeroIconButton>
            )}

            <HeroIconButton label="Edit metadata" onClick={() => setIsEditModalOpen(true)}>
                <PencilIcon />
            </HeroIconButton>

            <div className="relative">
                <HeroIconButton label="More actions" onClick={() => setShowMenu(v => !v)}>
                    <MoreIcon />
                </HeroIconButton>
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
                            {isEpisode && media.seasonId && (
                                <button type="button" onClick={() => { navigate(serverId ? `/server/${serverId}/media/${media.seasonId}` : `/media/${media.seasonId}`); setShowMenu(false); }} className="block w-full cursor-pointer px-4 py-2.5 text-left text-sm transition-colors hover:bg-white/5" style={{ color: 'var(--vora-text-primary)' }}>Go to season</button>
                            )}
                            {isSeason && media.tvShowId && (
                                <button type="button" onClick={() => { navigate(serverId ? `/server/${serverId}/media/${media.tvShowId}` : `/media/${media.tvShowId}`); setShowMenu(false); }} className="block w-full cursor-pointer px-4 py-2.5 text-left text-sm transition-colors hover:bg-white/5" style={{ color: 'var(--vora-text-primary)' }}>Go to show</button>
                            )}
                            {showParentNav && <div className="border-t" style={{ borderColor: 'var(--vora-border-subtle)' }} />}
                            <button type="button" onClick={() => { setIsPlaylistModalOpen(true); setShowMenu(false); }} className="block w-full cursor-pointer px-4 py-2.5 text-left text-sm transition-colors hover:bg-white/5" style={{ color: 'var(--vora-text-primary)' }}>Add to playlist</button>
                            <button type="button" onClick={() => { setIsCollectionModalOpen(true); setShowMenu(false); }} className="block w-full cursor-pointer px-4 py-2.5 text-left text-sm transition-colors hover:bg-white/5" style={{ color: 'var(--vora-text-primary)' }}>Add to collection</button>
                            {isAdmin && (
                                <>
                                    <div className="border-t" style={{ borderColor: 'var(--vora-border-subtle)' }} />
                                    <button type="button" onClick={handleRefresh} className="block w-full cursor-pointer px-4 py-2.5 text-left text-sm transition-colors hover:bg-white/5" style={{ color: 'var(--vora-text-primary)' }}>Refresh metadata</button>
                                    <button type="button" onClick={handleScan} className="block w-full cursor-pointer px-4 py-2.5 text-left text-sm transition-colors hover:bg-white/5" style={{ color: 'var(--vora-text-primary)' }}>Scan files</button>
                                    <button type="button" onClick={handleAnalyze} className="block w-full cursor-pointer px-4 py-2.5 text-left text-sm transition-colors hover:bg-white/5" style={{ color: 'var(--vora-text-primary)' }}>Analyze media</button>
                                    {(media.type === 'Movie' || media.type === 'Episode') && (
                                        <>
                                            <button type="button" onClick={handleRegenerateThumbnails} className="block w-full cursor-pointer px-4 py-2.5 text-left text-sm transition-colors hover:bg-white/5" style={{ color: 'var(--vora-text-primary)' }}>Regenerate thumbnails</button>
                                            <button type="button" onClick={handleToggleThumbnailsLock} className="block w-full cursor-pointer px-4 py-2.5 text-left text-sm transition-colors hover:bg-white/5" style={{ color: 'var(--vora-text-primary)' }}>
                                                {thumbnailsLocked ? 'Unlock thumbnails' : 'Lock thumbnails'}
                                            </button>
                                        </>
                                    )}
                                    <button type="button" onClick={() => { setIsMarkerEditorOpen(true); setShowMenu(false); }} className="block w-full cursor-pointer px-4 py-2.5 text-left text-sm transition-colors hover:bg-white/5" style={{ color: 'var(--vora-text-primary)' }}>Edit markers</button>
                                    <button type="button" onClick={handleDelete} className="block w-full cursor-pointer px-4 py-2.5 text-left text-sm font-medium transition-colors hover:bg-white/5" style={{ color: 'var(--vora-danger-text)' }}>Delete item</button>
                                </>
                            )}
                        </div>
                    </>
                )}
            </div>
        </>
    );

    const heroNotice = (
        <>
            {nextEpisode && (
                <div
                    className="mt-7 flex w-fit max-w-3xl items-center gap-4 rounded-xl p-4"
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
        </>
    );

    return (
        <div className="relative min-h-full pb-20">
            <EditMetadataModal isOpen={isEditModalOpen} onClose={() => setIsEditModalOpen(false)} onSaved={reloadMedia} itemId={media.id} type="media" initialData={{ ...media, lockedFields: media.lockedFields ?? [] }} />
            <AddToCollectionModal isOpen={isCollectionModalOpen} onClose={() => setIsCollectionModalOpen(false)} mediaId={media.id} libraryId={media.libraryId} mediaType={media.type} initialCollectionIds={media.collectionIds || []} onSaved={reloadMedia} />
            <AddToPlaylistModal isOpen={isPlaylistModalOpen} onClose={() => setIsPlaylistModalOpen(false)} mediaId={media.id} />
            <TrailerOverlay trailer={playingTrailer} onClose={() => setPlayingTrailer(null)} />
            <MarkerEditorModal isOpen={isMarkerEditorOpen} onClose={() => setIsMarkerEditorOpen(false)} mediaItemId={media.id} mediaItemTitle={media.title} durationSeconds={media.durationMinutes ? media.durationMinutes * 60 : undefined} serverId={serverId} onSaved={reloadMedia} />

            <QualityPanel open={isQualityPanelOpen} onClose={() => setIsQualityPanelOpen(false)}>
                {versionOptions.length > 1 && (
                    <QualityPanelSection title="Version" options={versionOptions} value={activePart?.id ?? ''} onChange={handleVersionChange} />
                )}
                {videoOptions.length > 0 && (
                    <QualityPanelSection title="Video" options={videoOptions} value={selectedVideoId} onChange={handleVideoChange} />
                )}
                {audioOptions.length > 0 && (
                    <QualityPanelSection title="Audio" options={audioOptions} value={selectedAudioId} onChange={setSelectedAudioId} />
                )}
                <QualityPanelSection title="Subtitles" options={subtitleOptions} value={selectedSubtitleId} onChange={setSelectedSubtitleId} />
            </QualityPanel>

            <DetailHero
                backdropSrc={media.backgroundUrl || media.posterUrl}
                transitionKey={media.id}
                posterSrc={media.posterUrl}
                posterShape={isEpisode ? 'still' : 'poster'}
                onBack={goBack}
                eyebrow={heroEyebrow}
                title={heroTitle}
                titleSuffix={isEpisode
                    ? (media.endEpisodeNumber && media.endEpisodeNumber > (media.episodeNumber ?? 0)
                        ? `S${media.seasonNumber} E${media.episodeNumber}-E${media.endEpisodeNumber}`
                        : `S${media.seasonNumber} E${media.episodeNumber}`)
                    : undefined}
                subtitle={heroSubtitle}
                chips={heroChips}
                ratings={heroRatings}
                credits={<HeroCredits directors={directorsFrom(media.cast)} genres={media.genres} studios={media.studios} />}
                actions={heroActions}
                notice={heroNotice}
                overview={media.overview}
            />
            {media.type === 'TvShow' && (media.seasons || []).length > 0 && (
                <div className="mt-12">
                    <MediaRow title="Seasons">
                        {(media.seasons || []).map(season => (
                            <MediaRowItem key={season.id}>
                                <MediaCard
                                    imageUrl={season.posterUrl || media.posterUrl}
                                    title={season.title || `Season ${season.seasonNumber}`}
                                    captionLines={[`${season.episodeCount || 0} episode${season.episodeCount === 1 ? '' : 's'}`]}
                                    unplayedCount={season.unplayedItemCount}
                                    onClick={() => navigate(serverId ? `/server/${serverId}/media/${season.id}` : `/media/${season.id}`)}
                                />
                            </MediaRowItem>
                        ))}
                    </MediaRow>
                </div>
            )}

            <div className="mt-12 space-y-16 px-12">
                <CastRow
                    cast={(media.cast || []).map(member => ({
                        id: member.actorId,
                        name: member.name,
                        role: member.role,
                        characterName: member.characterName,
                        profileImageUrl: member.profileImageUrl,
                        order: member.order,
                    }))}
                    onSelect={member => navigate(serverId ? `/server/${serverId}/actor/${member.id}` : `/actor/${member.id}`)}
                />
                <MediaExtrasRow videos={media.videos || []} extras={media.extras || []} serverId={serverId} />
                <MediaEpisodesList episodes={media.episodes || []} serverId={serverId} />
            </div>
        </div>
    );
}
