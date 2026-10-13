import { useEffect, useState, useCallback, useMemo } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { mediaService, type MediaItem } from '../../../api/Media/mediaService';
import CastRow from '../../../components/Client/Primitives/CastRow';
import MediaExtrasRow from '../../../components/Media/MediaExtrasRow';
import MediaEpisodesList from '../../../components/Media/MediaEpisodesList';
import { useSignalREvent } from '../../../hooks/useSignalREvent';
import { usePlayer } from '../../../contexts/usePlayer';
import { streamingService } from '../../../api/Streaming/streamingService';
import { getEffectiveCapabilities, scanDeviceCapabilitiesSync } from '../../../utils/hardwareScanner';
import TitlePlaybackControls, { TitlePlaybackAdvanced } from '../../../components/Media/TitlePlaybackControls';
import { applyTitlePlaybackCapabilities, readTitlePlaybackPreferences, resolveTitlePlayback, saveAudioChoice, saveSubtitleChoice, saveTitlePlaybackPreferences, saveVideoChoice, titlePlaybackKey, type TitlePlaybackPreferences } from '../../../utils/titlePlaybackPreferences';
import { isVideoDirectPlayable, isAudioDirectPlayable, parseResolutionHeight } from '../../../utils/playbackDecision';
import { useDialog } from '../../../dialogs';
import MediaCard from '../../../components/Client/Primitives/MediaCard';
import DetailHero, { HeroChip, HeroCredits, HeroIconButton } from '../../../components/Client/Primitives/DetailHero';
import RatingBadge from '../../../components/Client/Primitives/RatingBadge';
import TrailerOverlay, { type TrailerSource } from '../../../components/Client/Primitives/TrailerOverlay';
import { PlayIcon, RestartIcon, FilmReelIcon, BookmarkIcon, CheckIcon, MoreIcon, StarIcon } from '../../../components/Client/Primitives/ActionIcons';
import { watchlistService } from '../../../api/Watchlist/watchlistService';
import MediaRow, { MediaRowItem } from '../../../components/Client/Primitives/MediaRow';
import EmptyState from '../../../components/Client/Primitives/EmptyState';
import QualityPanel, { QualityPanelSection, type QualityOption } from '../../../components/Client/Primitives/QualityPanel';
import StarRating from '../../../components/Client/Primitives/StarRating';
import RateDialog from '../../../components/Client/Primitives/RateDialog';
import { StorageKeys, getProfileIdFromToken } from '../../../utils/storageKeys';
import { formatRuntime } from '../../../utils/formatRuntime';
import { isFullyWatched } from '../../../utils/watchState';
import { isImageSubtitleCodec, isNoSubtitle } from '../../../utils/subtitleKind';
import type { MediaMatchResult } from '../../../api/Media/libraryAdminService';
import { heroFactsLine, pickChipAudioTrack } from '../../../utils/heroQualityChips';
import { formatDate, serverTimeMs, yearOf } from '../../../utils/serverTime';
import { seasonEpisodeLabel, seasonName } from '../../../utils/seasonLabel';
import { errorDetail, isMissingOrForbidden } from '../../../utils/apiError';
import { ActionMenuList } from '../../../components/Client/Primitives/ActionMenu';
import { menuAction, tidyMenu } from '../../../components/Client/Primitives/menuEntries';
import { mediaMenuTarget } from '../../../components/Media/ItemMenu/mediaItemMenu';
import { useMediaItemActions } from '../../../components/Media/ItemMenu/useMediaItemActions';
import { toggleWatched, toggleWatchlist } from '../../../components/Media/ItemMenu/watchActions';

interface UpcomingEpisodeParsed {
    SeasonNumber: number;
    EpisodeNumber: number;
    Title: string;
    AirDate: string;
}

function trackLanguageName(code?: string): string {
    if (!code) return 'Unknown';
    try { return new Intl.DisplayNames(['en'], { type: 'language' }).of(new Intl.Locale(code).language) ?? code; }
    catch { return code; }
}

function audioLayout(channels?: number): string {
    return channels === 6 ? '5.1' : channels === 8 ? '7.1' : channels === 2 ? '2.0' : channels ? `${channels}ch` : '';
}

export default function MediaDetailsPage() {
    const dialog = useDialog();
    const { serverId, id } = useParams<{ serverId?: string, id: string }>();
    const navigate = useNavigate();

    const [media, setMedia] = useState<MediaItem | null>(null);
    const [loading, setLoading] = useState(true);
    const [loadFailure, setLoadFailure] = useState<string | null>(null);

    const [showMenu, setShowMenu] = useState(false);
    const [playingTrailer, setPlayingTrailer] = useState<TrailerSource | null>(null);
    const [inWatchlist, setInWatchlist] = useState(false);
    const [isQualityPanelOpen, setIsQualityPanelOpen] = useState(false);
    const [isRateOpen, setIsRateOpen] = useState(false);

    const [savedPreferences, setSavedPreferences] = useState<{ key: string; value: TitlePlaybackPreferences }>({ key: '', value: {} });

    const [upNextQuality, setUpNextQuality] = useState<{ showId: string; episode: MediaItem | null } | null>(null);
    const qualityMedia = !media
        ? null
        : media.type !== 'TvShow'
            ? media
            : upNextQuality?.showId === media.id ? upNextQuality.episode : null;

    // Navigating between items (e.g. episode → season) reuses this component,
    // so close any open overlay/menu from the previous item. Adjusted during
    // render rather than in an effect so the stale overlay never paints.
    const [overlaysForId, setOverlaysForId] = useState(id);
    if (overlaysForId !== id) {
        setOverlaysForId(id);
        setIsQualityPanelOpen(false);
        setShowMenu(false);
        setInWatchlist(false);
    }

    const isAdmin = localStorage.getItem(StorageKeys.isServerAdmin) === 'true';
    const baseCaps = useMemo(() => {
        const profileToken = localStorage.getItem(StorageKeys.profileToken);
        const profileId = (profileToken ? getProfileIdFromToken(profileToken) : null) || 'unknown';
        const deviceId = localStorage.getItem(StorageKeys.deviceId) || 'unknown';
        return getEffectiveCapabilities(profileId, deviceId);
    }, []);
    const preferenceKey = media ? titlePlaybackKey(media, serverId) : '';
    const preferences = useMemo(() => savedPreferences.key === preferenceKey ? savedPreferences.value
        : media ? readTitlePlaybackPreferences(media, serverId) : {}, [savedPreferences, preferenceKey, media, serverId]);
    const caps = useMemo(() => applyTitlePlaybackCapabilities(baseCaps, scanDeviceCapabilitiesSync(), preferences), [baseCaps, preferences]);
    const selected = qualityMedia ? resolveTitlePlayback(qualityMedia, preferences, caps) : null;
    const selectedVideoId = selected?.video?.id ?? '';
    const selectedAudioId = selected?.audio?.id ?? '';
    const selectedSubtitleId = selected?.subtitleId ?? 'none';
    const { playMedia, isPlaying } = usePlayer();
    useEffect(() => {
        const refresh = () => {
            if (media) setSavedPreferences({ key: preferenceKey, value: readTitlePlaybackPreferences(media, serverId) });
        };
        window.addEventListener('title-playback-preferences-changed', refresh);
        window.addEventListener('storage', refresh);
        return () => {
            window.removeEventListener('title-playback-preferences-changed', refresh);
            window.removeEventListener('storage', refresh);
        };
    }, [media, preferenceKey, serverId]);

    const reloadMedia = useCallback(async () => {
        if (!id) return;
        try {
            const data = await mediaService.getMediaItem(id, serverId);
            setMedia(data);
        } catch {
            console.error('Failed to reload media details.');
        }
    }, [id, serverId]);

    const itemActions = useMediaItemActions(media, {
        serverId,
        onChanged: reloadMedia,
        onDeleted: deleted => navigate(deleted.libraryId
            ? (serverId ? `/server/${serverId}/library/${deleted.libraryId}` : `/library/${deleted.libraryId}`)
            : '/'),
        onMatched: async (result: MediaMatchResult) => {
            if (result.mediaItemId !== id) {
                navigate(serverId ? `/server/${serverId}/media/${result.mediaItemId}` : `/media/${result.mediaItemId}`, { replace: true });
                await dialog.alert({
                    title: 'Match applied',
                    message: 'This was a second copy of a show already in your library, so the two have been merged. Its metadata is refreshing now.',
                });
                return;
            }
            await dialog.alert({
                title: 'Match applied',
                message: result.mergedDuplicate
                    ? 'A duplicate copy of this show was merged into it. Metadata is refreshing now, and this page will update when it finishes.'
                    : 'Metadata is refreshing now, and this page will update when it finishes.',
            });
        },
    });

    useEffect(() => {
        let isMounted = true;
        if (!id) return;

        watchlistService.check({ mediaItemId: id }, serverId)
            .then(saved => { if (isMounted) setInWatchlist(saved); })
            .catch(() => { /* a watchlist read failing shouldn't block the page */ });

        mediaService.getMediaItem(id, serverId)
            .then(data => {
                if (isMounted) {
                    setMedia(data);
                    setLoadFailure(null);
                    setLoading(false);
                }
            })
            .catch((error: unknown) => {
                if (!isMounted) return;
                setLoadFailure(isMissingOrForbidden(error) ? null : errorDetail(error, "The server couldn't load this title"));
                setLoading(false);
            });

        return () => { isMounted = false; };
    }, [id, serverId]);

    useEffect(() => {
        if (!media || media.type !== 'TvShow') return;

        const showId = media.id;
        let cancelled = false;
        (async () => {
            try {
                const upNext = await mediaService.getUpNext(showId, undefined, undefined, serverId);
                if (cancelled) return;
                if (!upNext.nextItem) {
                    setUpNextQuality({ showId, episode: null });
                    return;
                }
                const episode = await mediaService.getMediaItem(upNext.nextItem.id, serverId);
                if (!cancelled) setUpNextQuality({ showId, episode });
            } catch {
                if (!cancelled) setUpNextQuality({ showId, episode: null });
            }
        })();
        return () => { cancelled = true; };
    }, [media, serverId]);

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
        const playableMedia = qualityMedia ?? media;

        let subtitle = '';
        if (playableMedia.type === 'Episode' && playableMedia.seasonNumber != null && playableMedia.episodeNumber != null) {
            subtitle = `${seasonEpisodeLabel(playableMedia.seasonNumber, playableMedia.episodeNumber, { endEpisodeNumber: playableMedia.endEpisodeNumber })} - ${playableMedia.tvShowTitle}`;
        }
        else if (media.releaseDate) subtitle = String(yearOf(media.releaseDate) ?? '');

        const deviceId = localStorage.getItem(StorageKeys.deviceId);
        if (!deviceId) return;

        try {
            const startPos = resume ? (playableMedia.resumePositionSeconds || 0) : 0;

            const activePart = qualityMedia?.mediaParts?.find(p => p.videoTracks?.some(v => v.id === selectedVideoId)) || qualityMedia?.mediaParts?.[0];

            // Only a burn-in (image) subtitle is the server's business — it has
            // to re-encode for one. A text subtitle is sideloaded by the player
            // after playback starts, so sending it here would only make the play
            // request wait on an extraction it doesn't need.
            const chosenSubtitle = activePart?.subtitleTracks?.find(st => st.id === selectedSubtitleId);
            const wantsBurnIn = !isNoSubtitle(selectedSubtitleId) && !!chosenSubtitle && isImageSubtitleCodec(chosenSubtitle.codec);
            const textSubtitleTrackId = !isNoSubtitle(selectedSubtitleId) && !wantsBurnIn ? selectedSubtitleId : null;
            const subId = wantsBurnIn ? selectedSubtitleId : '00000000-0000-0000-0000-000000000000';

            const sessionInfo = await streamingService.startSession(playableMedia.id, deviceId, startPos, selectedVideoId || undefined, selected?.manualAudio ? selectedAudioId || undefined : undefined, subId, serverId, activePart?.id, playableMedia);

            const activeVideoTrack = activePart?.videoTracks?.find(v => v.id === selectedVideoId) || activePart?.videoTracks?.[0];
            const activeAudioTrack = pickChipAudioTrack(activePart?.audioTracks, sessionInfo.audioTrackId);

            playMedia({
                id: playableMedia.id, title: playableMedia.title, subtitle: subtitle,
                posterUrl: media.posterUrl, backgroundUrl: media.backgroundUrl,
                ...sessionInfo, startPosition: startPos, textSubtitleTrackId,
                serverId: serverId ?? undefined,
                resolution: activePart?.resolution, hdrType: activeVideoTrack?.hdrType,
                outputResolution: sessionInfo.outputResolution ?? undefined,
                outputHdrType: sessionInfo.outputHdrType ?? undefined,
                audioCodec: activeAudioTrack?.codec, audioChannels: activeAudioTrack?.channels,
                playbackContextType: playableMedia.type === 'Episode' ? 'tvshow' : 'movie'
            });
        } catch {
            await dialog.alert('Failed to start playback session.');
        }
    };

    const handleToggleWatchlist = async () => {
        if (!media) return;
        const next = await toggleWatchlist(media, dialog, serverId);
        if (next !== null) setInWatchlist(next);
    };

    const handleTogglePlayed = async () => {
        if (media && await toggleWatched(media, dialog, serverId)) reloadMedia();
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

    const handlePreferencesChange = (value: TitlePlaybackPreferences) => {
        if (!media) return;
        try {
            saveTitlePlaybackPreferences(media, value, serverId);
            setSavedPreferences({ key: preferenceKey, value });
        } catch {
            void dialog.alert('Your browser could not save these playback preferences.');
        }
    };

    const handleAudioChange = (audioId: string) => {
        const track = selected?.part?.audioTracks?.find(a => a.id === audioId);
        if (track) handlePreferencesChange({ ...preferences, audio: saveAudioChoice(track) });
    };

    const handleSubtitleChange = (subtitleId: string) => {
        handlePreferencesChange({ ...preferences, subtitles: saveSubtitleChoice(selected?.part?.subtitleTracks?.find(s => s.id === subtitleId)) });
    };

    const handleVideoChange = (videoId: string) => {
        const part = qualityMedia?.mediaParts?.find(p => p.videoTracks?.some(v => v.id === videoId));
        const track = part?.videoTracks?.find(v => v.id === videoId);
        if (part && track) handlePreferencesChange({ ...preferences, video: saveVideoChoice(part, track) });
    };

    if (loading) {
        return (
            <div>
                <div className="vora-skeleton h-[70vh] min-h-[540px]" />
                <div className="px-12 max-md:px-4 pt-8">
                    <div className="vora-skeleton mb-3 h-10 w-2/3" />
                    <div className="vora-skeleton mb-6 h-6 w-1/2" />
                    <div className="vora-skeleton mb-4 h-14 w-44" />
                </div>
            </div>
        );
    }
    if (!media) {
        return loadFailure ? (
            <EmptyState title="Couldn't load this title" description={loadFailure} />
        ) : (
            <EmptyState
                title="Media not found"
                description="This title doesn't exist or you don't have access to it."
            />
        );
    }

    const activePart = qualityMedia?.mediaParts?.find(p => p.videoTracks?.some(v => v.id === selectedVideoId)) || qualityMedia?.mediaParts?.[0];
    const resumePos = media.resumePositionSeconds || 0;
    const inProgress = resumePos > 0 && !media.isPlayed;
    const isFullyPlayed = isFullyWatched(media);

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
            label: [v.part.edition, displayRes, v.track.codec?.toUpperCase() ?? '—'].filter(Boolean).join(' · '),
            sublabel: [v.track.hdrType, v.track.isDefault ? 'Default' : null, playbackBadge].filter(Boolean).join(' · ') || undefined,
        };
    });
    const audioOptions: QualityOption<string>[] = sortedAudioTracks.map(a => ({
        value: a.id,
        label: [trackLanguageName(a.language), a.codec?.toUpperCase(), audioLayout(a.channels), a.isDefault ? '(Default)' : null, a.title?.toLowerCase().includes('commentary') ? a.title : null].filter(Boolean).join(' '),
        sublabel: [a.title, a.isDefault ? 'Default' : null, isAudioDirectPlayable(a, caps) ? 'Direct Play' : 'Transcode'].filter(Boolean).join(' · ') || undefined,
    }));
    const subtitleOptions: QualityOption<string>[] = [
        { value: 'none', label: 'Off' },
        ...(activePart?.subtitleTracks ?? []).map(s => ({
            value: s.id,
            label: [trackLanguageName(s.language), s.isDefault ? '(Default)' : null, s.codec?.toUpperCase(), s.isForced ? 'Forced' : null, s.isHearingImpaired ? 'SDH' : null].filter(Boolean).join(' '),
            sublabel: [s.title, s.isForced ? 'Forced' : null, s.isHearingImpaired ? 'SDH' : null, s.isDefault ? 'Default' : null].filter(Boolean).join(' · ') || undefined,
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
                label: [displayRes, partVideo?.codec?.toUpperCase(), playbackBadge].filter(Boolean).join(' · '),
                sublabel: [p.edition, p.bitrateKbps ? `${Math.round(p.bitrateKbps / 1000)} Mbps` : null].filter(Boolean).join(' · ') || undefined,
            };
        });

    let nextEpisode: UpcomingEpisodeParsed | null = null;
    if (media.upcomingEpisodesJson && media.upcomingEpisodesJson !== '[]') {
        try {
            const upcoming: UpcomingEpisodeParsed[] = JSON.parse(media.upcomingEpisodesJson);
            const futureEps = upcoming.filter((ep) => serverTimeMs(ep.AirDate) >= new Date().getTime());
            if (futureEps.length > 0) {
                nextEpisode = futureEps.sort((a, b) => serverTimeMs(a.AirDate) - serverTimeMs(b.AirDate))[0];
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
    const heroSubtitle = isSeason ? seasonName(media.seasonNumber, media.title) : isEpisode ? media.title : undefined;
    const showQualityButton = media.type === 'Movie' || isEpisode || media.type === 'TvShow';
    const moreEntries = tidyMenu([
        ...(showQualityButton ? [menuAction('quality', 'Quality & tracks', () => setIsQualityPanelOpen(true))] : []),
        ...itemActions.entries,
    ]);
    const playLabel = media.type === 'TvShow' ? 'Play next' : inProgress ? 'Resume' : 'Play';
    const playRuntime = formatRuntime(media.durationMinutes);

    const videos = media.videos ?? [];
    const trailerVideo = videos.find(v => v.type === 'Trailer') ?? videos[0];
    const trailer: TrailerSource | null = trailerVideo
        ? { name: trailerVideo.name ?? 'Trailer', videoKey: trailerVideo.videoKey, site: trailerVideo.site }
        : null;

    const heroFacts = heroFactsLine({ year: yearOf(media.releaseDate), runtime: playRuntime, genres: media.genres });
    const heroChips = (heroFacts || media.contentRating) ? (
        <>
            {heroFacts && <span className="text-sm" style={{ color: 'var(--vora-text-secondary)' }}>{heroFacts}</span>}
            {media.contentRating && <HeroChip>{media.contentRating}</HeroChip>}
        </>
    ) : null;


    const heroRatings = (
        <>
            {media.myRating != null && (
                <div className="flex items-center gap-2" title="Your rating">
                    <StarRating value={media.myRating} readOnly showNumeric ariaLabel="Your rating" />
                </div>
            )}
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
            {showQualityButton && versionOptions.length > 0 && (
                <div className="w-full pb-0.5">
                    <TitlePlaybackControls
                        videoOptions={versionOptions} audioOptions={audioOptions} subtitleOptions={subtitleOptions}
                        videoId={activePart?.id ?? ''} audioId={selectedAudioId} subtitleId={selectedSubtitleId}
                        onVideoChange={handleVersionChange} onAudioChange={handleAudioChange} onSubtitleChange={handleSubtitleChange}
                    />
                </div>
            )}
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

            <HeroIconButton
                label={inWatchlist ? 'Remove from watchlist' : 'Add to watchlist'}
                active={inWatchlist}
                onClick={handleToggleWatchlist}
            >
                <BookmarkIcon filled={inWatchlist} />
            </HeroIconButton>

            <HeroIconButton label={media.myRating != null ? 'Change your rating' : 'Rate'} active={media.myRating != null} onClick={() => setIsRateOpen(true)}>
                <StarIcon filled={media.myRating != null} />
            </HeroIconButton>

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

            <div className="relative">
                <HeroIconButton label="More actions" onClick={() => setShowMenu(v => !v)}>
                    <MoreIcon />
                </HeroIconButton>
                {showMenu && (
                    <>
                        <div className="fixed inset-0 z-40" onClick={() => setShowMenu(false)} />
                        <div
                            role="menu"
                            className="absolute right-0 mt-2 w-52 overflow-hidden rounded-xl z-50"
                            style={{
                                background: 'var(--vora-bg-raised)',
                                border: '1px solid var(--vora-border-strong)',
                                boxShadow: 'var(--vora-shadow-lg)',
                            }}
                        >
                            <ActionMenuList entries={moreEntries} onSelected={() => setShowMenu(false)} />
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
                            Airs {formatDate(nextEpisode.AirDate, { weekday: 'long', year: 'numeric', month: 'long', day: 'numeric' })}
                        </p>
                    </div>
                </div>
            )}
        </>
    );

    return (
        <div className="relative min-h-full pb-20">
            {isRateOpen && (
                <RateDialog title={heroTitle} value={media.myRating ?? null} onSave={handleSetRating} onClose={() => setIsRateOpen(false)} />
            )}
            {itemActions.dialogs}
            <TrailerOverlay trailer={playingTrailer} onClose={() => setPlayingTrailer(null)} />

            <QualityPanel open={isQualityPanelOpen} onClose={() => setIsQualityPanelOpen(false)}>
                {versionOptions.length > 1 && (
                    <QualityPanelSection title="Version" options={versionOptions} value={activePart?.id ?? ''} onChange={handleVersionChange} />
                )}
                {videoOptions.length > 0 && (
                    <QualityPanelSection title="Video" options={videoOptions} value={selectedVideoId} onChange={handleVideoChange} />
                )}
                {audioOptions.length > 0 && (
                    <QualityPanelSection title="Audio" options={audioOptions} value={selectedAudioId} onChange={handleAudioChange} />
                )}
                <QualityPanelSection title="Subtitles" options={subtitleOptions} value={selectedSubtitleId} onChange={handleSubtitleChange} />
                <TitlePlaybackAdvanced preferences={preferences} onPreferencesChange={handlePreferencesChange} onReset={() => handlePreferencesChange({})} />
            </QualityPanel>

            <DetailHero
                backdropSrc={media.backgroundUrl || media.posterUrl}
                transitionKey={media.id}
                posterSrc={media.posterUrl}
                posterShape={isEpisode ? 'still' : 'poster'}
                title={heroTitle}
                logoUrl={media.logoUrl}
                titleSuffix={isEpisode && media.seasonNumber != null && media.episodeNumber != null
                    ? seasonEpisodeLabel(media.seasonNumber, media.episodeNumber, { endEpisodeNumber: media.endEpisodeNumber })
                    : undefined}
                subtitle={heroSubtitle}
                chips={heroChips}
                ratings={heroRatings}
                credits={<HeroCredits directors={media.directors} studios={media.studios} />}
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
                                    title={seasonName(season.seasonNumber, season.title)}
                                    captionLines={[`${season.episodeCount || 0} episode${season.episodeCount === 1 ? '' : 's'}`]}
                                    unplayedCount={season.unplayedItemCount}
                                    onClick={() => navigate(serverId ? `/server/${serverId}/media/${season.id}` : `/media/${season.id}`)}
                                    menu={mediaMenuTarget({ id: season.id, type: 'Season' }, serverId, { onChanged: reloadMedia })}
                                />
                            </MediaRowItem>
                        ))}
                    </MediaRow>
                </div>
            )}

            <div className="mt-8 space-y-12 px-12 max-md:px-4">
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
                <MediaEpisodesList episodes={media.episodes || []} serverId={serverId} onChanged={reloadMedia} />
            </div>
        </div>
    );
}
