import { useEffect, useState, useCallback } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { discoveryService, type DiscoveryItemDetails, type Trailer, type Theater } from '../../../api/Discovery/discoveryService';
import { useDialog } from '../../../dialogs';
import MediaRow, { MediaRowItem } from '../../../components/Client/Primitives/MediaRow';
import CastRow from '../../../components/Client/Primitives/CastRow';
import VideoCard from '../../../components/Client/Primitives/VideoCard';
import DetailHero, { HeroChip, HeroCredits } from '../../../components/Client/Primitives/DetailHero';
import { watchlistService } from '../../../api/Watchlist/watchlistService';
import RatingBadge from '../../../components/Client/Primitives/RatingBadge';
import TrailerOverlay from '../../../components/Client/Primitives/TrailerOverlay';
import { BookmarkIcon } from '../../../components/Client/Primitives/ActionIcons';
import { directorsFrom } from '../../../utils/credits';
import { StorageKeys, getProfileIdFromToken } from '../../../utils/storageKeys';
import { formatRuntime } from '../../../utils/formatRuntime';

export default function DiscoveryDetailsPage() {
    const dialog = useDialog();
    const { serverId, providerId, type, externalId } = useParams<{ serverId?: string, providerId: string, type: string, externalId: string }>();
    const navigate = useNavigate();

    const [details, setDetails] = useState<DiscoveryItemDetails | null>(null);
    const [inWatchlist, setInWatchlist] = useState(false);
    const [isLoading, setIsLoading] = useState(true);

    const [theaters, setTheaters] = useState<Theater[]>([]);
    const [isLoadingTheaters, setIsLoadingTheaters] = useState(false);

    const [autoLoad, setAutoLoad] = useState<boolean | null>(null);
    const [showtimesFetched, setShowtimesFetched] = useState(false);

    const [playingTrailer, setPlayingTrailer] = useState<Trailer | null>(null);

    const profileToken = localStorage.getItem(StorageKeys.profileToken);
    const activeProfileId = getProfileIdFromToken(profileToken) ?? '';
    const [requestStatus, setRequestStatus] = useState<number>(-1);

    useEffect(() => {
        if (!providerId || !type || !externalId) return;

        const loadData = async () => {
            try {
                const item = await discoveryService.getItemDetails(providerId, type, externalId, serverId);
                setDetails(item);

                if (activeProfileId) {
                    const watchStatus = await watchlistService.check({ externalId, providerId }, serverId);
                    setInWatchlist(watchStatus);
                    const status = await discoveryService.getRequestStatus(externalId, type, serverId);
                    setRequestStatus(status);
                }
            } catch (error) {
                console.error("Failed to load discovery details", error);
            } finally {
                setIsLoading(false);
            }
        };

        loadData();
    }, [providerId, type, externalId, serverId, activeProfileId]);

    useEffect(() => {
        discoveryService.getTheaterAutoLoad(serverId)
            .then(setAutoLoad)
            .catch(() => setAutoLoad(true)); // Fallback to true if network error
    }, [serverId]);

    const fetchTheaters = useCallback(async () => {
        if (!details || details.type !== 'Movie') return;
        setIsLoadingTheaters(true);
        try {
            const savedZip = activeProfileId ? (localStorage.getItem(`client_zipcode_${activeProfileId}`) || '') : '';
            const savedMax = activeProfileId ? parseInt(localStorage.getItem(`client_max_theaters_${activeProfileId}`) || '6', 10) : 6;

            const showtimeData = await discoveryService.getShowtimes(details.title, savedZip, savedMax, serverId);

            showtimeData.forEach(theater => {
                theater.showtimes.sort((a, b) => {
                    const parseTime = (t: string) => {
                        const m = t.match(/(\d+):(\d+)\s*(am|pm)/i);
                        if (!m) return 9999; // Push unknown formats to the end
                        let h = parseInt(m[1], 10);
                        if (m[3].toLowerCase() === 'pm' && h < 12) h += 12;
                        if (m[3].toLowerCase() === 'am' && h === 12) h = 0;
                        return (h * 60) + parseInt(m[2], 10);
                    };
                    return parseTime(a.time) - parseTime(b.time);
                });
            });

            setTheaters(showtimeData);
        } catch {
            console.error("Failed to load showtimes");
        } finally {
            setIsLoadingTheaters(false);
            setShowtimesFetched(true);
        }
    }, [details, activeProfileId, serverId]);

    useEffect(() => {
        if (details && details.type === 'Movie' && autoLoad === true && !showtimesFetched && !isLoadingTheaters) {
            fetchTheaters();
        }
    }, [details, autoLoad, showtimesFetched, isLoadingTheaters, fetchTheaters]);

    const getShowtimeStatus = (timeStr: string) => {
        const match = timeStr.match(/(\d+):(\d+)\s*(am|pm)/i);
        if (!match) return 'future';

        let hours = parseInt(match[1], 10);
        const minutes = parseInt(match[2], 10);
        const period = match[3].toLowerCase();

        if (period === 'pm' && hours < 12) hours += 12;
        if (period === 'am' && hours === 12) hours = 0;

        const now = new Date();
        const showTime = new Date();
        showTime.setHours(hours, minutes, 0, 0);

        const diffMins = (showTime.getTime() - now.getTime()) / 1000 / 60;

        if (diffMins < 0) return 'past';
        if (diffMins <= 30) return 'soon';
        return 'future';
    };

    const handleToggleWatchlist = async () => {
        if (!details || !activeProfileId) return;

        const expectedDate = details.type === 'TvShow' && details.nextAirDate
            ? details.nextAirDate
            : details.releaseDate;

        try {
            const next = await watchlistService.toggle(
                { externalId: details.externalId, providerId: details.providerId },
                {
                    type: details.type,
                    title: details.title,
                    posterUrl: details.posterUrl,
                    // A show that is still airing bookmarks against its next
                    // episode rather than its original premiere.
                    expectedReleaseDate: expectedDate,
                },
                serverId
            );
            setInWatchlist(next);
        } catch {
            await dialog.alert("Failed to update watchlist.");
        }
    };

    if (isLoading) return <div className="p-12 text-center text-[var(--vora-text-muted)] mt-16">Loading details...</div>;
    if (!details) return <div className="p-12 text-center text-[var(--vora-danger-500)] mt-16">Media details not found.</div>;

    const runtime = formatRuntime(details.runtimeMinutes);
    const runtimeLabel = runtime && details.type === 'TvShow' ? `${runtime} episodes` : runtime;

    return (
        <>
            <div className="relative min-h-full pb-16">
                <DetailHero
                    backdropSrc={details.backgroundUrl || details.posterUrl}
                    transitionKey={details.externalId}
                    posterSrc={details.posterUrl}
                    onBack={() => navigate(-1)}
                    eyebrow={[details.type === 'TvShow' ? 'TV Series' : 'Movie', details.year].filter(Boolean).join(' · ')}
                    title={details.title}
                    chips={(
                        <>
                            {runtimeLabel && <HeroChip>{runtimeLabel}</HeroChip>}
                            {details.contentRating && <HeroChip>{details.contentRating}</HeroChip>}
                            {details.inLibrary && <HeroChip tone="accent">In your library</HeroChip>}
                            {details.nextAirDate && <HeroChip>Next air date {new Date(details.nextAirDate).toLocaleDateString()}</HeroChip>}
                            {requestStatus === 0 && <HeroChip>Request pending</HeroChip>}
                            {requestStatus === 3 && <HeroChip>Downloading</HeroChip>}
                            {requestStatus === 4 && <HeroChip tone="accent">Available in library</HeroChip>}
                        </>
                    )}
                    ratings={details.rating != null && details.rating > 0
                        ? <RatingBadge value={details.rating} name="TMDB" />
                        : undefined}
                    credits={<HeroCredits directors={directorsFrom(details.cast)} genres={details.genres} studios={details.studios} />}
                    actions={(
                        <button
                            type="button"
                            onClick={handleToggleWatchlist}
                            className={inWatchlist ? 'vora-button-secondary cursor-pointer' : 'vora-button-primary cursor-pointer'}
                            style={{ display: 'inline-flex', alignItems: 'center', gap: 10, padding: '0.875rem 1.75rem', fontSize: '0.9375rem' }}
                        >
                            {inWatchlist ? (
                                <>
                                    <BookmarkIcon filled />
                                    In Watchlist
                                </>
                            ) : (
                                <>
                                    <BookmarkIcon />
                                    Add to Watchlist
                                </>
                            )}
                        </button>
                    )}
                    overview={details.overview}
                />

                <div className="px-12 pb-16">
                    <div className="mt-16">
                        <CastRow
                            cast={details.cast.map((actor, idx) => {
                                const isCrew = actor.role === 'Director' || actor.role === 'Writer' || actor.role === 'Creator' || actor.role === 'Crew';
                                return {
                                    id: actor.externalId,
                                    name: actor.name,
                                    role: isCrew ? actor.role : 'Actor',
                                    characterName: isCrew ? null : actor.role,
                                    profileImageUrl: actor.profileImageUrl,
                                    order: idx,
                                };
                            })}
                            onSelect={member => navigate(serverId ? `/server/${serverId}/discovery/${details.providerId}/actor/${member.id}` : `/discovery/${details.providerId}/actor/${member.id}`)}
                        />
                    </div>

                    {details.trailers.length > 0 && (
                        <div className="mt-16">
                            <MediaRow title="Trailers" variant="section">
                                {details.trailers.map((trailer, idx) => {
                                    const videoId = trailer.url.split('v=')[1]?.split('&')[0];
                                    if (!videoId) return null;
                                    return (
                                        <MediaRowItem key={idx}>
                                            <VideoCard
                                                title={trailer.name}
                                                imageUrl={`https://img.youtube.com/vi/${videoId}/hqdefault.jpg`}
                                                onClick={() => setPlayingTrailer(trailer)}
                                            />
                                        </MediaRowItem>
                                    );
                                })}
                            </MediaRow>
                        </div>
                    )}

                    {details.type === 'Movie' && autoLoad !== null && (
                        <div className="mt-16">
                            <h2 className="text-2xl font-bold mb-6 text-[var(--vora-text-primary)] border-b border-[var(--vora-border-subtle)] pb-2">Local Showtimes</h2>

                            {autoLoad === false && !showtimesFetched && !isLoadingTheaters ? (
                                <button
                                    onClick={fetchTheaters}
                                    className="px-6 py-3 bg-[var(--vora-bg-sunken)] hover:bg-[var(--vora-bg-raised)] border border-[var(--vora-border-subtle)] text-[var(--vora-text-primary)] font-bold rounded shadow-lg transition-colors flex items-center gap-2 cursor-pointer"
                                >
                                    <svg className="w-5 h-5 text-[var(--vora-accent-500)]" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15 10l4.553-2.276A1 1 0 0121 8.618v6.764a1 1 0 01-1.447.894L15 14M5 18h8a2 2 0 002-2V8a2 2 0 00-2-2H5a2 2 0 00-2 2v8a2 2 0 002 2z" /></svg>
                                    Find Local Showtimes
                                </button>
                            ) : isLoadingTheaters ? (
                                <div className="text-[var(--vora-text-muted)] flex items-center gap-3">
                                    <div className="w-5 h-5 border-2 border-[var(--vora-accent-500)] border-t-transparent rounded-full animate-spin"></div>
                                    Searching local theaters...
                                </div>
                            ) : theaters.length > 0 ? (
                                <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-6">
                                    {theaters.map((theater, idx) => (
                                        <div key={idx} className="bg-[var(--vora-bg-sunken)]/50 border border-[var(--vora-border-subtle)] rounded-lg p-5">
                                            <h3 className="font-bold text-lg text-[var(--vora-text-primary)] mb-1">{theater.name}</h3>
                                            <p className="text-sm text-[var(--vora-text-muted)] mb-4">{theater.address}</p>
                                            <div className="flex flex-wrap gap-2">
                                                {theater.showtimes.map((st, i) => {
                                                    const status = getShowtimeStatus(st.time);

                                                    let containerClass = "px-3 py-1.5 border rounded flex flex-col items-center transition-colors ";
                                                    let timeClass = "font-bold ";
                                                    let formatClass = "text-[10px] uppercase tracking-wider ";

                                                    if (status === 'past') {
                                                        containerClass += "bg-[var(--vora-bg-raised)] border-[var(--vora-border-subtle)] opacity-50 grayscale";
                                                        timeClass += "text-[var(--vora-text-muted)] line-through";
                                                        formatClass += "text-[var(--vora-text-muted)]";
                                                    } else if (status === 'soon') {
                                                        containerClass += "bg-yellow-900/20 border-yellow-600/50";
                                                        timeClass += "text-yellow-400";
                                                        formatClass += "text-yellow-600";
                                                    } else {
                                                        containerClass += "bg-[var(--vora-bg-raised)] border-[var(--vora-border-subtle)]";
                                                        timeClass += "text-[var(--vora-text-secondary)]";
                                                        formatClass += "text-[var(--vora-accent-500)]";
                                                    }

                                                    return (
                                                        <div key={i} className={containerClass}>
                                                            <span className={timeClass}>{st.time}</span>
                                                            <span className={formatClass}>{st.format}</span>
                                                        </div>
                                                    );
                                                })}
                                            </div>
                                        </div>
                                    ))}
                                </div>
                            ) : showtimesFetched ? (
                                <div className="text-[var(--vora-text-muted)] bg-[var(--vora-bg-sunken)]/30 p-4 rounded-lg inline-block border border-[var(--vora-border-subtle)]">
                                    No local showtimes found for this location.
                                </div>
                            ) : null}
                        </div>
                    )}
                </div>
            </div>

            <TrailerOverlay
                trailer={playingTrailer ? { name: playingTrailer.name, url: playingTrailer.url } : null}
                onClose={() => setPlayingTrailer(null)}
            />
        </>
    );
}