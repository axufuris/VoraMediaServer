import { useEffect, useState, useCallback } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { createPortal } from 'react-dom';
import { discoveryService, type DiscoveryItemDetails, type Trailer, type Theater } from '../../../api/Discovery/discoveryService';
import { useDialog } from '../../../dialogs';
import MediaRow from '../../../components/Common/MediaRow';
import CinematicBackdrop from '../../../components/Client/Primitives/CinematicBackdrop';
import { StorageKeys, getProfileIdFromToken } from '../../../utils/storageKeys';

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
                    const watchStatus = await discoveryService.checkWatchlist(activeProfileId, externalId, providerId, serverId);
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
            await discoveryService.toggleWatchlist(
                activeProfileId,
                details.externalId,
                details.providerId,
                details.type,
                details.title,
                details.posterUrl,
                expectedDate, // <-- PASSED SMART DATE
                serverId
            );
            setInWatchlist(!inWatchlist);
        } catch {
            await dialog.alert("Failed to update watchlist.");
        }
    };

    if (isLoading) return <div className="p-12 text-center text-[var(--vora-text-muted)] mt-16">Loading details...</div>;
    if (!details) return <div className="p-12 text-center text-[var(--vora-danger-500)] mt-16">Media details not found.</div>;

    const modalContent = playingTrailer ? (
        <div className="fixed inset-0 z-[99999] bg-black flex items-center justify-center">
            <button
                onClick={() => setPlayingTrailer(null)}
                className="absolute top-6 left-1/2 -translate-x-1/2 px-6 py-2.5 flex items-center gap-2 rounded-full bg-black/70 hover:bg-[var(--vora-bg-sunken)] text-[var(--vora-text-primary)] transition-colors backdrop-blur-md cursor-pointer z-10 border border-white/20 shadow-2xl font-bold tracking-wider text-sm"
                title="Close Video"
            >
                <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2.5} d="M6 18L18 6M6 6l12 12" /></svg>
                CLOSE
            </button>
            <div className="w-full h-full relative flex items-center justify-center">
                <iframe
                    className="w-full h-full border-none bg-black"
                    src={`https://www.youtube.com/embed/${playingTrailer.url.split('v=')[1]?.split('&')[0]}?autoplay=1`}
                    title={playingTrailer.name}
                    allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture; web-share"
                    allowFullScreen
                ></iframe>
            </div>
        </div>
    ) : null;

    return (
        <>
            <div className="relative min-h-full pb-16">

                <div className="absolute inset-x-0 top-0 z-0">
                    <CinematicBackdrop src={details.backgroundUrl} intensity="detail" parallax transitionKey={details.externalId} />
                </div>

                <div className="relative z-10 px-12 pt-8">
                    <button
                        type="button"
                        onClick={() => navigate(-1)}
                        className="mb-8 inline-flex cursor-pointer items-center gap-2 rounded-full px-3 py-1.5 text-sm font-medium backdrop-blur-md transition-colors hover:bg-[rgba(20,20,28,0.85)]"
                        style={{ background: 'rgba(20, 20, 28, 0.65)', border: '1px solid rgba(255, 255, 255, 0.14)', color: '#fafafa' }}
                    >
                        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><polyline points="15 18 9 12 15 6" /></svg>
                        Back to Discover
                    </button>

                    <div className="flex flex-col md:flex-row gap-10">
                        <div className="w-64 shrink-0 relative">
                            <div className="aspect-[2/3] rounded-lg overflow-hidden shadow-2xl border border-[var(--vora-border-subtle)] bg-[var(--vora-bg-sunken)] relative">
                                {details.posterUrl ? <img src={details.posterUrl} className="w-full h-full object-cover" /> : <div className="w-full h-full flex items-center justify-center text-[var(--vora-text-muted)]">No Image</div>}
                            </div>
                        </div>

                        <div className="flex-1 pt-0">
                            <h1 className="text-[2.75rem] leading-tight font-bold text-[var(--vora-text-primary)] drop-shadow-lg mb-1">
                                {details.title}
                            </h1>

                            <div className="flex items-center gap-3 text-sm font-medium text-[var(--vora-text-muted)] mb-8">
                                <span>{details.type === 'TvShow' ? 'TV Series' : 'Movie'}</span>
                                {details.year && <span>• {details.year}</span>}
                            </div>

                            <div className="flex items-center gap-4 mb-8">
                                <div className="flex items-center gap-4 mb-8">
                                    <button
                                        onClick={handleToggleWatchlist}
                                        className={`px-6 py-2.5 font-bold rounded shadow-lg transition-colors flex items-center gap-2 cursor-pointer ${inWatchlist ? 'bg-[var(--vora-bg-sunken)] hover:bg-[var(--vora-bg-raised)] text-[var(--vora-text-primary)] border border-[var(--vora-border-subtle)]' : 'bg-[var(--vora-accent-500)] hover:bg-[var(--vora-accent-hover)] text-[var(--vora-text-primary)]'}`}
                                    >
                                        {inWatchlist ? (
                                            <><svg className="w-5 h-5 text-[var(--vora-accent-500)]" fill="currentColor" viewBox="0 0 20 20"><path fillRule="evenodd" d="M16.707 5.293a1 1 0 010 1.414l-8 8a1 1 0 01-1.414 0l-4-4a1 1 0 011.414-1.414L8 12.586l7.293-7.293a1 1 0 011.414 0z" clipRule="evenodd" /></svg> In Watchlist</>
                                        ) : (
                                            <><svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M5 5a2 2 0 012-2h10a2 2 0 012 2v16l-7-3.5L5 21V5z" /></svg> Add to Watchlist</>
                                        )}
                                    </button>

                                    {requestStatus === 0 && <span className="px-3 py-1 bg-[var(--vora-bg-sunken)]/80 text-[var(--vora-text-secondary)] text-xs font-bold uppercase tracking-wider rounded border border-[var(--vora-border-subtle)]">Request Pending</span>}
                                    {requestStatus === 3 && <span className="px-3 py-1 bg-blue-900/30 text-blue-400 text-xs font-bold uppercase tracking-wider rounded border border-blue-900/50">Downloading...</span>}
                                    {requestStatus === 4 && <span className="px-3 py-1 bg-green-900/30 text-green-400 text-xs font-bold uppercase tracking-wider rounded border border-green-900/50">Available in Library</span>}
                                </div>
                            </div>

                            <p className="text-[15px] text-[var(--vora-text-secondary)] leading-relaxed max-w-4xl shadow-sm mb-10">
                                {details.overview || "No overview available."}
                            </p>
                        </div>
                    </div>

                    {details.cast.length > 0 && (
                        <MediaRow title="Cast & Crew" variant="detail" gap="5">
                                {details.cast.map((actor, idx) => {
                                    const isCrew = actor.role === 'Director' || actor.role === 'Writer' || actor.role === 'Creator' || actor.role === 'Crew';
                                    const displayRole = isCrew ? actor.role : 'Actor';
                                    const characterName = isCrew ? '---' : actor.role;

                                    return (
                                        <div key={idx} onClick={() => navigate(serverId ? `/server/${serverId}/discovery/${details.providerId}/actor/${actor.externalId}` : `/discovery/${details.providerId}/actor/${actor.externalId}`)} className="w-32 shrink-0 flex flex-col items-center text-center group cursor-pointer">
                                            <div className="w-32 h-40 rounded-lg overflow-hidden bg-[var(--vora-bg-sunken)] mb-3 border border-[var(--vora-border-subtle)] shadow-lg relative group-hover:border-[var(--vora-accent-500)] transition-colors">
                                                {actor.profileImageUrl ? (
                                                    <img src={actor.profileImageUrl} alt={actor.name} className="w-full h-full object-cover" />
                                                ) : (
                                                    <div className="w-full h-full flex items-center justify-center bg-[var(--vora-bg-sunken)]">
                                                        <svg className="w-16 h-16 text-[var(--vora-text-muted)]" fill="currentColor" viewBox="0 0 24 24"><path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm0 3c1.66 0 3 1.34 3 3s-1.34 3-3 3-3-1.34-3-3 1.34-3 3-3zm0 14.2c-2.5 0-4.71-1.28-6-3.22.03-1.99 4-3.08 6-3.08 1.99 0 5.97 1.09 6 3.08-1.29 1.94-3.5 3.22-6 3.22z" /></svg>
                                                    </div>
                                                )}
                                                <div className="absolute inset-0 bg-black/40 opacity-0 group-hover:opacity-100 transition-opacity flex items-center justify-center"></div>
                                            </div>
                                            <h3 className="font-bold text-[var(--vora-text-secondary)] text-sm leading-tight max-w-full truncate group-hover:text-[var(--vora-text-primary)] transition-colors">
                                                {actor.name}
                                            </h3>
                                            <p className="text-xs text-[var(--vora-accent-500)] font-bold uppercase tracking-wider mt-1 line-clamp-1">
                                                {displayRole}
                                            </p>
                                            <p className="text-xs text-[var(--vora-text-muted)] font-medium leading-tight mt-1 line-clamp-2 max-w-full">
                                                {characterName}
                                            </p>
                                        </div>
                                    );
                                })}
                        </MediaRow>
                    )}

                    {details.trailers.length > 0 && (
                        <MediaRow title="Trailers" variant="detail" gap="6">
                                {details.trailers.map((trailer, idx) => {
                                    const videoId = trailer.url.split('v=')[1]?.split('&')[0];
                                    if (!videoId) return null;

                                    return (
                                        <div key={idx} onClick={() => setPlayingTrailer(trailer)} className="w-72 shrink-0 flex flex-col group cursor-pointer text-left">
                                            <div className="aspect-video rounded-md overflow-hidden bg-[var(--vora-bg-canvas)] mb-3 border border-[var(--vora-border-subtle)] group-hover:border-[var(--vora-accent-500)] transition-colors shadow-md relative">
                                                <img
                                                    src={`https://img.youtube.com/vi/${videoId}/hqdefault.jpg`}
                                                    alt={trailer.name}
                                                    className="w-full h-full object-cover opacity-80 group-hover:opacity-100 transition-opacity"
                                                />
                                                <div className="absolute inset-0 flex items-center justify-center">
                                                    <div className="w-12 h-12 rounded-full bg-black/60 group-hover:bg-[var(--vora-accent-500)]/90 flex items-center justify-center pl-1 shadow-lg transition-colors">
                                                        <svg className="w-6 h-6 text-[var(--vora-text-primary)]" fill="currentColor" viewBox="0 0 20 20"><path d="M4 4l12 6-12 6z" /></svg>
                                                    </div>
                                                </div>
                                            </div>
                                            <h3 className="font-bold text-[var(--vora-text-secondary)] text-sm line-clamp-2 group-hover:text-[var(--vora-text-primary)] transition-colors">
                                                {trailer.name}
                                            </h3>
                                        </div>
                                    );
                                })}
                        </MediaRow>
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

            {playingTrailer && createPortal(modalContent, document.body)}
        </>
    );
}