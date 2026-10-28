import { useEffect, useState, useCallback } from 'react';
import { useParams, useNavigate, useLocation } from 'react-router-dom';
import { mediaService, type Episode, type MediaItem } from '../../../api/Media/mediaService';
import CastRow from '../../../components/Client/Primitives/CastRow';
import MediaExtrasRow from '../../../components/Media/MediaExtrasRow';
import EpisodeRail from '../../../components/Media/EpisodeRail';
import EpisodeList from '../../../components/Media/EpisodeList';
import { useSignalREvent } from '../../../hooks/useSignalREvent';
import { usePlayer } from '../../../contexts/usePlayer';
import { streamingService } from '../../../api/Streaming/streamingService';
import { usePlaybackCapabilities } from '../../../hooks/usePlaybackCapabilities';
import { useDialog } from '../../../dialogs';
import MediaCard from '../../../components/Client/Primitives/MediaCard';
import DetailHero, { HeroChip, HeroCredits, HeroIconButton } from '../../../components/Client/Primitives/DetailHero';
import RatingBadge, { VoraRatingBadge } from '../../../components/Client/Primitives/RatingBadge';
import TrailerOverlay, { type TrailerSource } from '../../../components/Client/Primitives/TrailerOverlay';
import { PlayIcon, RestartIcon, FilmReelIcon, BookmarkIcon, CheckIcon, MoreIcon, StarIcon } from '../../../components/Client/Primitives/ActionIcons';
import { watchlistService } from '../../../api/Watchlist/watchlistService';
import MediaRow, { MediaRowItem } from '../../../components/Client/Primitives/MediaRow';
import EmptyState from '../../../components/Client/Primitives/EmptyState';
import RateDialog from '../../../components/Client/Primitives/RateDialog';
import { StorageKeys } from '../../../utils/storageKeys';
import { formatRuntime } from '../../../utils/formatRuntime';
import { isFullyWatched } from '../../../utils/watchState';
import { burnInSubtitleId, isNoSubtitle } from '../../../utils/subtitleKind';
import { useTitleTrackChoices } from '../../../hooks/useTitleTrackChoices';
import TrackChoiceChips from '../../../components/Media/TrackChoiceChips';
import type { MediaMatchResult } from '../../../api/Media/libraryAdminService';
import { heroFactsLine, pickChipAudioTrack } from '../../../utils/heroQualityChips';
import { playButtonFor, playTargetFor } from '../../../utils/playTarget';
import { formatDate, serverTimeMs, yearOf } from '../../../utils/serverTime';
import { seasonEpisodeLabel, seasonName } from '../../../utils/seasonLabel';
import { errorDetail, isMissingOrForbidden } from '../../../utils/apiError';
import { ActionMenuList } from '../../../components/Client/Primitives/ActionMenu';
import { menuAction, menuHeading, menuSeparator, tidyMenu, withKeyPrefix } from '../../../components/Client/Primitives/menuEntries';
import { mediaPath, ShowPageState, type MediaPageState } from '../../../utils/mediaPath';
import SeasonPills from '../../../components/Media/SeasonPills';
import { useSeasonEpisodeSelection, useSiblingSeasons } from './useSeasonEpisodeSelection';
import { useEpisodeLayout } from './useEpisodeLayout';
import { mediaMenuTarget } from '../../../components/Media/ItemMenu/mediaItemMenu';
import { useMediaItemActions } from '../../../components/Media/ItemMenu/useMediaItemActions';
import { toggleWatched, toggleWatchlist } from '../../../components/Media/ItemMenu/watchActions';

interface UpcomingEpisodeParsed {
    SeasonNumber: number;
    EpisodeNumber: number;
    Title: string;
    AirDate: string;
}

const prefersReducedMotion = () => window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false;

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
    const [rating, setRating] = useState<'title' | 'episode' | null>(null);

    // A show plays its up-next episode, so that episode's file is what the
    // chips describe and the track picks apply to. A season page describes the
    // episode selected on it, which starts as the season's up-next episode.
    const [upNextQuality, setUpNextQuality] = useState<{ titleId: string; episode: MediaItem | null } | null>(null);
    const playsUpNext = media?.type === 'TvShow' || media?.type === 'Season';
    const upNextEpisode = media && upNextQuality?.titleId === media.id ? upNextQuality.episode : null;
    const upNextResolved = !!media && upNextQuality?.titleId === media.id;
    const isSeasonPage = media?.type === 'Season';
    const selection = useSeasonEpisodeSelection(media, upNextEpisode, upNextResolved, serverId);
    const siblingSeasons = useSiblingSeasons(media, serverId);
    const episodeLayout = useEpisodeLayout(serverId);
    const qualityMedia = !media
        ? null
        : isSeasonPage
            ? selection.details
            : !playsUpNext ? media : upNextEpisode;
    const playEpisode = isSeasonPage ? selection.selected : qualityMedia;

    // Navigating between items (e.g. episode → season) reuses this component,
    // so close any open overlay/menu from the previous item. Adjusted during
    // render rather than in an effect so the stale overlay never paints.
    const [overlaysForId, setOverlaysForId] = useState(id);
    if (overlaysForId !== id) {
        setOverlaysForId(id);
        setShowMenu(false);
        setInWatchlist(false);
    }

    const isAdmin = localStorage.getItem(StorageKeys.isServerAdmin) === 'true';
    const caps = usePlaybackCapabilities();
    const { playMedia, isPlaying } = usePlayer();
    const tracks = useTitleTrackChoices(qualityMedia, caps, serverId);

    // An episode has no page of its own: its address moves to its season with
    // the episode selected, so a link from anywhere lands where the TV apps do.
    // A show with one season moves to that season too, unless the address
    // asks for the show's own page.
    const location = useLocation();
    const asksForShowPage = (location.state as MediaPageState | null)?.showPage === true;
    const openOnSeason = useCallback((data: MediaItem) => {
        const onlySeason = data.type === 'TvShow' && !asksForShowPage && data.seasons?.length === 1 ? data.seasons[0] : undefined;
        const seasonPath = data.type === 'Episode' && data.seasonId
            ? mediaPath(data, serverId)
            : onlySeason ? mediaPath({ id: onlySeason.id, type: 'Season' }, serverId) : null;
        if (!seasonPath) return false;
        navigate(seasonPath, { replace: true });
        return true;
    }, [navigate, serverId, asksForShowPage]);

    const reloadMedia = useCallback(async () => {
        if (!id) return;
        try {
            const data = await mediaService.getMediaItem(id, serverId);
            if (!openOnSeason(data)) setMedia(data);
        } catch {
            console.error('Failed to reload media details.');
        }
    }, [id, serverId, openOnSeason]);

    const episodeActions = useMediaItemActions(isSeasonPage ? selection.details : null, {
        serverId,
        onChanged: reloadMedia,
        onDeleted: () => { void reloadMedia(); },
    });

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
                if (!isMounted || openOnSeason(data)) return;
                setMedia(data);
                setLoadFailure(null);
                setLoading(false);
            })
            .catch((error: unknown) => {
                if (!isMounted) return;
                setLoadFailure(isMissingOrForbidden(error) ? null : errorDetail(error, "The server couldn't load this title"));
                setLoading(false);
            });

        return () => { isMounted = false; };
    }, [id, serverId, openOnSeason]);

    useEffect(() => {
        if (!media || (media.type !== 'TvShow' && media.type !== 'Season')) return;

        const titleId = media.id;
        let cancelled = false;
        (async () => {
            try {
                const upNext = await mediaService.getUpNext(titleId, undefined, undefined, serverId);
                if (cancelled) return;
                if (!upNext.nextItem) {
                    setUpNextQuality({ titleId, episode: null });
                    return;
                }
                const episode = await mediaService.getMediaItem(upNext.nextItem.id, serverId);
                if (!cancelled) setUpNextQuality({ titleId, episode });
            } catch {
                if (!cancelled) setUpNextQuality({ titleId, episode: null });
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
        const { item, startSeconds: startPos, contextType } = playTargetFor(media, playEpisode, resume);

        let subtitle = '';
        if (item.type === 'Episode' && item.seasonNumber != null && item.episodeNumber != null) {
            subtitle = `${seasonEpisodeLabel(item.seasonNumber, item.episodeNumber, { endEpisodeNumber: item.endEpisodeNumber })} - ${item.tvShowTitle}`;
        }
        else if (item.releaseDate) subtitle = String(yearOf(item.releaseDate) ?? '');

        const deviceId = localStorage.getItem(StorageKeys.deviceId);
        if (!deviceId) return;

        try {

            const activePart = qualityMedia?.mediaParts?.find(p => p.videoTracks?.some(v => v.id === tracks.videoId)) || qualityMedia?.mediaParts?.[0];

            // Only a burn-in (image) subtitle is the server's business — it has
            // to re-encode for one. A text subtitle is sideloaded by the player
            // after playback starts, so sending it here would only make the play
            // request wait on an extraction it doesn't need.
            const wantsBurnIn = burnInSubtitleId(activePart?.subtitleTracks, tracks.subtitleId) !== undefined;
            const textSubtitleTrackId = !isNoSubtitle(tracks.subtitleId) && !wantsBurnIn ? tracks.subtitleId : null;
            const subId = wantsBurnIn ? tracks.subtitleId : '00000000-0000-0000-0000-000000000000';

            const sessionInfo = await streamingService.startSession(item.id, deviceId, startPos, tracks.videoId || undefined,
                tracks.audioPickedHere ? tracks.audioId || undefined : undefined, subId, serverId, activePart?.id);

            const activeVideoTrack = activePart?.videoTracks?.find(v => v.id === tracks.videoId) || activePart?.videoTracks?.[0];
            const activeAudioTrack = pickChipAudioTrack(activePart?.audioTracks, sessionInfo.audioTrackId);

            playMedia({
                id: item.id, title: item.title, subtitle: subtitle,
                posterUrl: item.posterUrl || media.posterUrl, backgroundUrl: item.backgroundUrl || media.backgroundUrl,
                ...sessionInfo, startPosition: startPos, textSubtitleTrackId,
                serverId: serverId ?? undefined,
                resolution: activePart?.resolution, hdrType: activeVideoTrack?.hdrType,
                outputResolution: sessionInfo.outputResolution ?? undefined,
                outputHdrType: sessionInfo.outputHdrType ?? undefined,
                audioCodec: activeAudioTrack?.codec, audioChannels: activeAudioTrack?.channels,
                playbackContextType: contextType
            });
        } catch {
            await dialog.alert('Failed to start playback session.');
        }
    };

    // An episode picked from the list plays from where it was left. Its tracks
    // are the server's to choose (the profile's languages and the picks it
    // remembers for the show), as when Up Next starts one.
    const playListedEpisode = async (episode: Episode, seasonNumber: number | undefined, showTitle: string | undefined) => {
        if (!media) return;
        const deviceId = localStorage.getItem(StorageKeys.deviceId);
        if (!deviceId) return;

        const startPos = !episode.isPlayed ? episode.resumePositionSeconds || 0 : 0;
        const label = seasonNumber != null ? seasonEpisodeLabel(seasonNumber, episode.episodeNumber, { endEpisodeNumber: episode.endEpisodeNumber }) : `E${episode.episodeNumber}`;
        try {
            const sessionInfo = await streamingService.startSession(episode.id, deviceId, startPos, undefined, undefined, undefined, serverId);
            playMedia({
                id: episode.id, title: episode.title,
                subtitle: showTitle ? `${label} - ${showTitle}` : label,
                posterUrl: episode.posterUrl || media.posterUrl, backgroundUrl: media.backgroundUrl,
                ...sessionInfo, startPosition: startPos,
                serverId: serverId ?? undefined,
                outputResolution: sessionInfo.outputResolution ?? undefined,
                outputHdrType: sessionInfo.outputHdrType ?? undefined,
                playbackContextType: 'tvshow'
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

    const handleToggleEpisodePlayed = async () => {
        if (selection.selected && await toggleWatched(selection.selected, dialog, serverId)) reloadMedia();
    };

    const handleSetEpisodeRating = async (next: number | null) => {
        const episodeId = selection.selectedId;
        if (!episodeId) return;
        try {
            await mediaService.setRating(episodeId, next, serverId);
        } catch {
            await dialog.alert('Failed to update rating.');
        }
        reloadMedia();
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

    const activePart = qualityMedia?.mediaParts?.find(p => p.videoTracks?.some(v => v.id === tracks.videoId)) || qualityMedia?.mediaParts?.[0];
    // On a season page the hero is the selected episode's page, as the TV apps'
    // focused episode: its facts, ratings, overview and file, and Play, Watched
    // and Rate act on it. The season's own actions are in the menu.
    const heroEpisode = isSeasonPage ? selection.selected : null;
    const described = heroEpisode ?? media;
    const playButton = playButtonFor(media, playEpisode);
    const resumePos = playButton.resumeSeconds;
    const inProgress = resumePos > 0;
    const resumeClock = `${Math.floor(resumePos / 60)}:${String(Math.floor(resumePos % 60)).padStart(2, '0')}`;
    const isFullyPlayed = isFullyWatched(described);

    // The server has already left out every episode the library holds, so this
    // is the next one still to come: the soonest yet to air, else one that has
    // aired but isn't here yet.
    let nextEpisode: UpcomingEpisodeParsed | null = null;
    let nextEpisodeAired = false;
    if (media.upcomingEpisodesJson && media.upcomingEpisodesJson !== '[]') {
        try {
            const upcoming: UpcomingEpisodeParsed[] = JSON.parse(media.upcomingEpisodesJson);
            const futureEps = upcoming.filter((ep) => serverTimeMs(ep.AirDate) >= new Date().getTime());
            if (futureEps.length > 0) {
                nextEpisode = futureEps.sort((a, b) => serverTimeMs(a.AirDate) - serverTimeMs(b.AirDate))[0];
            } else if (upcoming.length > 0) {
                nextEpisode = upcoming[0];
                nextEpisodeAired = true;
            }
        } catch (e: unknown) {
            console.error('Failed to parse upcoming episodes:', e);
        }
    }

    const isEpisode = media.type === 'Episode';
    const isSeason = media.type === 'Season';
    const heroTitle = (isSeason || isEpisode) && media.tvShowTitle ? media.tvShowTitle : media.title;
    const heroSubtitle = heroEpisode ? heroEpisode.title : isSeason ? seasonName(media.seasonNumber, media.title) : isEpisode ? media.title : undefined;
    const heroSuffix = heroEpisode && heroEpisode.seasonNumber != null && heroEpisode.episodeNumber != null
        ? seasonEpisodeLabel(heroEpisode.seasonNumber, heroEpisode.episodeNumber, { endEpisodeNumber: heroEpisode.endEpisodeNumber })
        : isEpisode && media.seasonNumber != null && media.episodeNumber != null
            ? seasonEpisodeLabel(media.seasonNumber, media.episodeNumber, { endEpisodeNumber: media.endEpisodeNumber })
            : undefined;
    const thisSeason = seasonName(media.seasonNumber, media.title);
    const moreEntries = heroEpisode ? tidyMenu([
        menuHeading('episode-heading', heroSuffix ? `${heroSuffix} · ${heroEpisode.title}` : heroEpisode.title),
        ...withKeyPrefix('episode', episodeActions.entries),
        menuSeparator('season-separator'),
        menuHeading('season-heading', thisSeason),
        menuAction('season-watched', isFullyWatched(media) ? `Mark ${thisSeason} as unwatched` : `Mark ${thisSeason} as watched`, () => { void handleTogglePlayed(); }),
        menuAction('season-rate', media.myRating != null ? `Change your rating of ${thisSeason}` : `Rate ${thisSeason}`, () => setRating('title')),
        ...withKeyPrefix('season', itemActions.entries),
    ]) : tidyMenu(itemActions.entries);
    const playLabel = playButton.label;
    const playRuntime = formatRuntime(described.durationMinutes);
    const listedEpisodes = isSeason ? media.episodes ?? [] : [];
    const showSeasonsRow = media.type === 'TvShow' && (media.seasons || []).length > 0;

    const videos = media.videos ?? [];
    const trailerVideo = videos.find(v => v.type === 'Trailer') ?? videos[0];
    const trailer: TrailerSource | null = trailerVideo
        ? { name: trailerVideo.name ?? 'Trailer', videoKey: trailerVideo.videoKey, site: trailerVideo.site }
        : null;

    const heroFacts = heroFactsLine({ year: yearOf(described.releaseDate ?? media.releaseDate), runtime: playRuntime, genres: described.genres ?? media.genres });
    const contentRating = described.contentRating ?? media.contentRating;
    const heroChips = (heroFacts || contentRating) ? (
        <>
            {heroFacts && <span className="text-sm" style={{ color: 'var(--vora-text-secondary)' }}>{heroFacts}</span>}
            {contentRating && <HeroChip>{contentRating}</HeroChip>}
        </>
    ) : null;

    const heroTechChips = qualityMedia ? (
        <TrackChoiceChips
            item={qualityMedia} activePart={activePart} caps={caps}
            videoId={tracks.videoId} audioId={tracks.audioId} subtitleId={tracks.subtitleId}
            remembered={tracks.remembered} pickReplaced={tracks.pickReplaced}
            onPickVersion={partId => { void tracks.pickVersion(partId); }}
            onPickAudio={audioId => { void tracks.pickAudio(audioId); }}
            onPickSubtitle={subtitleId => { void tracks.pickSubtitle(subtitleId); }}
            onChooseAutomatically={kind => { void tracks.chooseAutomatically(kind); }}
        />
    ) : null;

    const heroRatings = (
        <>
            {described.myRating != null && <VoraRatingBadge value={described.myRating} label="Your rating" />}
            {described.serverAdminRating != null && !isAdmin && (
                <div className="flex items-center gap-2">
                    <span className="text-[11px] font-semibold uppercase tracking-wider" style={{ color: 'var(--vora-text-muted)' }}>Server admin</span>
                    <VoraRatingBadge value={described.serverAdminRating} label="Server admin's rating" />
                </div>
            )}
            {described.thirdPartyRating1 != null && <RatingBadge value={described.thirdPartyRating1} name={described.thirdPartyRating1Name} />}
            {described.thirdPartyRating2 != null && <RatingBadge value={described.thirdPartyRating2} name={described.thirdPartyRating2Name} />}
        </>
    );

    // On every season page, a one-season show's too: the Show pill is the way
    // to the show's own page, which the title menu no longer offers.
    const showId = isSeason ? media.tvShowId : undefined;
    const seasonPills = showId && siblingSeasons.length > 0 ? (
        <SeasonPills
            seasons={siblingSeasons}
            currentId={media.id}
            onPick={season => navigate(mediaPath({ id: season.id, type: 'Season' }, serverId), { replace: true })}
            showTitle={media.tvShowTitle}
            onPickShow={() => navigate(mediaPath({ id: showId, type: 'TvShow' }, serverId), { state: ShowPageState })}
        />
    ) : null;

    const heroActions = (
        <>
            <button
                type="button"
                onClick={() => handlePlay(true)}
                aria-label={[playLabel, playButton.episode, inProgress ? resumeClock : null].filter(Boolean).join(' ')}
                className="vora-button-primary cursor-pointer"
                style={{ display: 'inline-flex', alignItems: 'center', gap: 10, padding: '14px 30px', fontSize: 15 }}
            >
                <PlayIcon />
                {playLabel}
                {playButton.episode && <span>{playButton.episode}</span>}
                {inProgress && (
                    <span className="ml-1 text-xs font-normal opacity-70">{resumeClock}</span>
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

            <HeroIconButton label={described.myRating != null ? 'Change your rating' : 'Rate'} active={described.myRating != null} onClick={() => setRating(heroEpisode ? 'episode' : 'title')}>
                <StarIcon filled={described.myRating != null} />
            </HeroIconButton>

            {trailer && (
                <HeroIconButton label="Play trailer" onClick={() => setPlayingTrailer(trailer)}>
                    <FilmReelIcon />
                </HeroIconButton>
            )}

            <HeroIconButton
                label={isFullyPlayed ? 'Mark as unwatched' : 'Mark as watched'}
                active={isFullyPlayed}
                onClick={heroEpisode ? handleToggleEpisodePlayed : handleTogglePlayed}
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
                            className="absolute right-0 mt-2 w-64 overflow-hidden rounded-xl z-50"
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
                            {nextEpisodeAired ? 'Aired' : 'Airs'} {formatDate(nextEpisode.AirDate, { weekday: 'long', year: 'numeric', month: 'long', day: 'numeric' })}
                        </p>
                    </div>
                </div>
            )}
        </>
    );

    return (
        <div className="relative min-h-full pb-20">
            {rating === 'title' && (
                <RateDialog title={isSeason ? `${heroTitle} · ${thisSeason}` : heroTitle} value={media.myRating ?? null} onSave={handleSetRating} onClose={() => setRating(null)} />
            )}
            {rating === 'episode' && heroEpisode && (
                <RateDialog title={heroEpisode.title} value={heroEpisode.myRating ?? null} onSave={handleSetEpisodeRating} onClose={() => setRating(null)} />
            )}
            {itemActions.dialogs}
            {episodeActions.dialogs}
            <TrailerOverlay trailer={playingTrailer} onClose={() => setPlayingTrailer(null)} />

            <DetailHero
                backdropSrc={media.backgroundUrl || media.posterUrl}
                transitionKey={media.id}
                posterSrc={media.posterUrl}
                posterShape={isEpisode ? 'still' : 'poster'}
                title={heroTitle}
                logoUrl={media.logoUrl}
                titleSuffix={heroSuffix}
                subtitle={heroSubtitle}
                chips={heroChips}
                ratings={heroRatings}
                credits={isSeason
                    ? <HeroCredits directors={media.directors} studios={media.studios} />
                    : <HeroCredits directors={described.directors ?? media.directors} studios={described.studios ?? media.studios} />}
                actions={heroActions}
                notice={heroNotice}
                overview={heroEpisode?.overview || media.overview || (isSeason ? media.tvShowOverview : undefined)}
                overviewLines={isSeason ? 3 : undefined}
                steady={isSeason}
                topContent={seasonPills}
                techChips={heroTechChips}
            />
            {showSeasonsRow && (
                <div className="mt-12">
                    <MediaRow title="Seasons">
                        {(media.seasons || []).map(season => (
                            <MediaRowItem key={season.id}>
                                <MediaCard
                                    imageUrl={season.posterUrl || media.posterUrl}
                                    title={seasonName(season.seasonNumber, season.title)}
                                    captionLines={[`${season.episodeCount || 0} episode${season.episodeCount === 1 ? '' : 's'}`]}
                                    unplayedCount={season.unplayedItemCount}
                                    onClick={() => navigate(mediaPath({ id: season.id, type: 'Season' }, serverId))}
                                    menu={mediaMenuTarget({ id: season.id, type: 'Season' }, serverId, { onChanged: reloadMedia })}
                                />
                            </MediaRowItem>
                        ))}
                    </MediaRow>
                </div>
            )}

            <div className="mt-8 space-y-12 px-12 max-md:px-4">
                {episodeLayout === 'List' ? (
                    <EpisodeList
                        episodes={listedEpisodes}
                        seasonNumber={media.seasonNumber}
                        serverId={serverId}
                        selectedId={selection.selectedId}
                        onSelect={episode => {
                            selection.select(episode);
                            window.scrollTo({ top: 0, behavior: prefersReducedMotion() ? 'auto' : 'smooth' });
                        }}
                        onPlay={episode => { void playListedEpisode(episode, media.seasonNumber, media.tvShowTitle); }}
                        onChanged={reloadMedia}
                    />
                ) : episodeLayout === 'Row' ? (
                    <EpisodeRail
                        episodes={listedEpisodes}
                        seasonNumber={media.seasonNumber}
                        serverId={serverId}
                        selectedId={selection.selectedId}
                        selectOnFocus
                        onSelect={selection.select}
                        onPlay={episode => { void playListedEpisode(episode, media.seasonNumber, media.tvShowTitle); }}
                        onChanged={reloadMedia}
                    />
                ) : null}
                <CastRow
                    cast={((isSeason ? selection.details?.cast : undefined) ?? media.cast ?? []).map(member => ({
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
            </div>
        </div>
    );
}
