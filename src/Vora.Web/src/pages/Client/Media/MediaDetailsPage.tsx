import { useEffect, useState, useCallback, useMemo } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { mediaService, type Episode, type MediaItem } from '../../../api/Media/mediaService';
import CastRow from '../../../components/Client/Primitives/CastRow';
import MediaExtrasRow from '../../../components/Media/MediaExtrasRow';
import MediaEpisodesList from '../../../components/Media/MediaEpisodesList';
import { useSignalREvent } from '../../../hooks/useSignalREvent';
import { usePlayer } from '../../../contexts/usePlayer';
import { streamingService } from '../../../api/Streaming/streamingService';
import { getEffectiveCapabilities } from '../../../utils/hardwareScanner';
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
import { StorageKeys, getProfileIdFromToken } from '../../../utils/storageKeys';
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
import { tidyMenu } from '../../../components/Client/Primitives/menuEntries';
import { mediaMenuTarget } from '../../../components/Media/ItemMenu/mediaItemMenu';
import { useMediaItemActions } from '../../../components/Media/ItemMenu/useMediaItemActions';
import { toggleWatched, toggleWatchlist } from '../../../components/Media/ItemMenu/watchActions';

interface UpcomingEpisodeParsed {
    SeasonNumber: number;
    EpisodeNumber: number;
    Title: string;
    AirDate: string;
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
    const [isRateOpen, setIsRateOpen] = useState(false);

    // A show and a season play their up-next episode, so its file is what the
    // chips describe and the track picks apply to.
    const [upNextQuality, setUpNextQuality] = useState<{ titleId: string; episode: MediaItem | null } | null>(null);
    const playsUpNext = media?.type === 'TvShow' || media?.type === 'Season';
    const qualityMedia = !media
        ? null
        : !playsUpNext
            ? media
            : upNextQuality?.titleId === media.id ? upNextQuality.episode : null;

    // A show with one season lists that season's episodes on its own page, so
    // there is no extra click through a Seasons row of one. The show page stays,
    // for its own menu (Fix match, Edit metadata), trailers, rating and watchlist.
    const [singleSeason, setSingleSeason] = useState<{ showId: string; season: MediaItem | null } | null>(null);
    const onlySeasonId = media?.type === 'TvShow' && media.seasons?.length === 1 ? media.seasons[0].id : null;
    const inlineSeason = media && singleSeason?.showId === media.id ? singleSeason.season : null;
    const inlineSeasonFailed = !!media && singleSeason?.showId === media.id && singleSeason.season === null;

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
    const caps = useMemo(() => {
        const profileToken = localStorage.getItem(StorageKeys.profileToken);
        const profileId = (profileToken ? getProfileIdFromToken(profileToken) : null) || 'unknown';
        const deviceId = localStorage.getItem(StorageKeys.deviceId) || 'unknown';
        return getEffectiveCapabilities(profileId, deviceId);
    }, []);
    const { playMedia, isPlaying } = usePlayer();
    const tracks = useTitleTrackChoices(qualityMedia, caps, serverId);

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
        if (!media || !onlySeasonId) return;

        const showId = media.id;
        let cancelled = false;
        mediaService.getMediaItem(onlySeasonId, serverId)
            .then(season => { if (!cancelled) setSingleSeason({ showId, season }); })
            .catch(() => { if (!cancelled) setSingleSeason({ showId, season: null }); });
        return () => { cancelled = true; };
    }, [media, onlySeasonId, serverId]);

    useEffect(() => {
        if (!isPlaying && id) {
            const timer = setTimeout(() => reloadMedia(), 500);
            return () => clearTimeout(timer);
        }
    }, [isPlaying, id, reloadMedia]);

    useSignalREvent('MediaItemUpdated', useCallback((updatedId: string) => {
        if (!id || !media) return;
        const targetId = updatedId.toLowerCase();
        const listed = [...(media.episodes ?? []), ...(inlineSeason?.episodes ?? [])];
        if (targetId === id.toLowerCase() || listed.some(ep => ep.id.toLowerCase() === targetId) || media.seasons?.some(s => s.id.toLowerCase() === targetId)) {
            reloadMedia();
        }
    }, [id, media, inlineSeason, reloadMedia]));

    useSignalREvent('MediaAnalysisUpdated', useCallback((updatedId: string) => {
        if (id && updatedId.toLowerCase() === id.toLowerCase()) reloadMedia();
    }, [id, reloadMedia]));

    const handlePlay = async (resume: boolean = true) => {
        if (!media) return;
        const { item, startSeconds: startPos, contextType } = playTargetFor(media, qualityMedia, resume);

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
    const playButton = playButtonFor(media, qualityMedia);
    const resumePos = playButton.resumeSeconds;
    const inProgress = resumePos > 0;
    const resumeClock = `${Math.floor(resumePos / 60)}:${String(Math.floor(resumePos % 60)).padStart(2, '0')}`;
    const isFullyPlayed = isFullyWatched(media);

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
    const moreEntries = tidyMenu(itemActions.entries);
    const playLabel = playButton.label;
    const playRuntime = formatRuntime(media.durationMinutes);
    const listedEpisodes = isSeason ? media.episodes ?? [] : inlineSeason?.episodes ?? [];
    const listedSeasonNumber = isSeason ? media.seasonNumber : inlineSeason?.seasonNumber;
    const listedShowTitle = isSeason ? media.tvShowTitle : media.title;
    const showSeasonsRow = media.type === 'TvShow' && (media.seasons || []).length > 0 && ((media.seasons || []).length > 1 || inlineSeasonFailed);

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
            {media.myRating != null && <VoraRatingBadge value={media.myRating} label="Your rating" />}
            {media.serverAdminRating != null && !isAdmin && (
                <div className="flex items-center gap-2">
                    <span className="text-[11px] font-semibold uppercase tracking-wider" style={{ color: 'var(--vora-text-muted)' }}>Server admin</span>
                    <VoraRatingBadge value={media.serverAdminRating} label="Server admin's rating" />
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
                overview={media.overview || (isSeason ? media.tvShowOverview : undefined)}
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
                                    onClick={() => navigate(serverId ? `/server/${serverId}/media/${season.id}` : `/media/${season.id}`)}
                                    menu={mediaMenuTarget({ id: season.id, type: 'Season' }, serverId, { onChanged: reloadMedia })}
                                />
                            </MediaRowItem>
                        ))}
                    </MediaRow>
                </div>
            )}

            <div className="mt-8 space-y-12 px-12 max-md:px-4">
                <MediaEpisodesList
                    episodes={listedEpisodes}
                    serverId={serverId}
                    onChanged={reloadMedia}
                    onPlay={episode => { void playListedEpisode(episode, listedSeasonNumber, listedShowTitle); }}
                />
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
            </div>
        </div>
    );
}
