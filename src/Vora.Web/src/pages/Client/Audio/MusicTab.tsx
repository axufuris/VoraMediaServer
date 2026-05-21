import { useEffect, useState, useCallback, useMemo } from 'react';
import { useParams, useSearchParams } from 'react-router-dom';
import { musicService, type ArtistVM, type AlbumVM, type TrackVM, type ArtistTrackVM, type MusicSearchResultVM, type GeneratedMixSummaryVM, type GeneratedMixDetailVM, type BecauseYouPlayedRowVM, type RadioSeed, type StationVM, type YearRecapVM, type GenreSummaryVM, type GenreContentVM, type ServerPlaybackSessionVM } from '../../../api/Music/musicService';
import { usePlayer, type PlayableMedia } from '../../../contexts/usePlayer';
import { serverVault } from '../../../utils/serverVault';
import { useSignalREvent } from '../../../hooks/useSignalREvent';
import MusicMetadataEditModal, { type MusicEntityKind } from '../../../components/Media/MusicMetadataEditModal';
import AddToPlaylistModal from '../../../components/Collections/AddToPlaylistModal';
import MusicServerSwitcher from '../../../components/Audio/MusicServerSwitcher';
import { useDialog } from '../../../dialogs';
import { audioQualityStore } from '../../../utils/audioQuality';

type MusicView = 'artists' | 'artist' | 'album' | 'likes' | 'top' | 'mix' | 'recap' | 'genres' | 'genre';

interface MusicNavState {
    view: MusicView;
    artistId?: string;
    albumId?: string;
    mixId?: string;
    year?: number;
    genre?: string;
}

const NAV_STORAGE_KEY = 'music_nav_state';
const NAV_PROFILE_KEY = 'music_nav_profile';

const readActiveProfileId = (): string => {
    try {
        const token = localStorage.getItem('profile_token');
        if (!token) return '';
        return JSON.parse(atob(token.split('.')[1])).sub || '';
    } catch {
        return '';
    }
};

export default function MusicTab() {
    const { serverId } = useParams<{ serverId?: string }>();
    const [searchParams, setSearchParams] = useSearchParams();
    const dialog = useDialog();
    const { playQueue, addToQueue, playNext, isShuffled, toggleShuffle, startRadio } = usePlayer();

    const [nav, setNav] = useState<MusicNavState>(() => {
        const stored = sessionStorage.getItem(NAV_STORAGE_KEY);
        const storedProfile = sessionStorage.getItem(NAV_PROFILE_KEY) || '';
        const currentProfile = readActiveProfileId();
        if (stored && storedProfile && storedProfile === currentProfile) {
            try { return JSON.parse(stored) as MusicNavState; } catch { /* ignore */ }
        }
        if (storedProfile !== currentProfile) {
            sessionStorage.removeItem(NAV_STORAGE_KEY);
            sessionStorage.removeItem(NAV_PROFILE_KEY);
        }
        return { view: 'artists' };
    });

    const [artists, setArtists] = useState<ArtistVM[]>([]);
    const [currentArtist, setCurrentArtist] = useState<ArtistVM | null>(null);
    const [albums, setAlbums] = useState<AlbumVM[]>([]);
    const [currentAlbum, setCurrentAlbum] = useState<AlbumVM | null>(null);
    const [tracks, setTracks] = useState<TrackVM[]>([]);
    const [isLoading, setIsLoading] = useState(false);

    const [editing, setEditing] = useState<{ kind: MusicEntityKind; artist?: ArtistVM; album?: AlbumVM; track?: TrackVM } | null>(null);
    const [refreshSeq, setRefreshSeq] = useState(0);
    const [addToPlaylistTrackId, setAddToPlaylistTrackId] = useState<string | null>(null);

    const [trackContextMenu, setTrackContextMenu] = useState<{ x: number; y: number; track: TrackVM; index: number } | null>(null);

    const [searchQuery, setSearchQuery] = useState('');
    const [searchResults, setSearchResults] = useState<MusicSearchResultVM[]>([]);
    const [isSearching, setIsSearching] = useState(false);
    const searchActive = searchQuery.trim().length >= 2;

    const [likedTracks, setLikedTracks] = useState<ArtistTrackVM[]>([]);
    const [likedCount, setLikedCount] = useState(0);

    const [recentlyPlayed, setRecentlyPlayed] = useState<ArtistTrackVM[]>([]);
    const [topTracks, setTopTracks] = useState<ArtistTrackVM[]>([]);
    const [topArtists, setTopArtists] = useState<ArtistVM[]>([]);
    const [recentlyAddedAlbums, setRecentlyAddedAlbums] = useState<AlbumVM[]>([]);
    const [dailyMixes, setDailyMixes] = useState<GeneratedMixSummaryVM[]>([]);
    const [becauseYouPlayed, setBecauseYouPlayed] = useState<BecauseYouPlayedRowVM[]>([]);
    const [currentMix, setCurrentMix] = useState<GeneratedMixDetailVM | null>(null);
    const [stations, setStations] = useState<StationVM[]>([]);
    const [currentRecap, setCurrentRecap] = useState<YearRecapVM | null>(null);
    const [availableYears, setAvailableYears] = useState<number[]>([]);
    const [hasAnyHistory, setHasAnyHistory] = useState(false);
    const [similarArtists, setSimilarArtists] = useState<ArtistVM[]>([]);
    const [genres, setGenres] = useState<GenreSummaryVM[]>([]);
    const [currentGenre, setCurrentGenre] = useState<GenreContentVM | null>(null);
    const [serverPlayback, setServerPlayback] = useState<ServerPlaybackSessionVM[]>([]);

    useEffect(() => {
        const close = () => setTrackContextMenu(null);
        document.addEventListener('click', close);
        return () => document.removeEventListener('click', close);
    }, []);

    const isServerAdmin = useMemo(() =>
        localStorage.getItem('is_server_admin') === 'true'
        && localStorage.getItem('is_profile_admin') === 'true'
    , []);

    const updateNav = useCallback((next: MusicNavState) => {
        setNav(next);
        sessionStorage.setItem(NAV_STORAGE_KEY, JSON.stringify(next));
        sessionStorage.setItem(NAV_PROFILE_KEY, readActiveProfileId());
    }, []);

    useEffect(() => {
        const mixParam = searchParams.get('mix');
        if (mixParam) {
            queueMicrotask(() => updateNav({ view: 'mix', mixId: mixParam }));
            searchParams.delete('mix');
            setSearchParams(searchParams, { replace: true });
        }
    }, [searchParams, setSearchParams, updateNav]);

    useEffect(() => {
        if (!searchActive) {
            queueMicrotask(() => {
                setSearchResults([]);
                setIsSearching(false);
            });
            return;
        }
        queueMicrotask(() => setIsSearching(true));
        const handle = setTimeout(() => {
            musicService.search(searchQuery, 30, serverId)
                .then(setSearchResults)
                .catch(err => {
                    console.error('Music search failed', err);
                    setSearchResults([]);
                })
                .finally(() => setIsSearching(false));
        }, 300);
        return () => clearTimeout(handle);
    }, [searchQuery, searchActive, serverId]);

    const bumpRefresh = useCallback(() => setRefreshSeq(s => s + 1), []);

    const loadLikedTracks = useCallback(async () => {
        try {
            const data = await musicService.getLikedTracks(serverId);
            setLikedTracks(data.tracks);
            setLikedCount(data.count);
        } catch (err) {
            console.error('Failed to load liked tracks', err);
        }
    }, [serverId]);

    const loadHomeRows = useCallback(async () => {
        try {
            const [recent, top, artistsTop, recentAlbums, mixes, byp, st, years] = await Promise.all([
                musicService.getRecentlyPlayed(12, serverId),
                musicService.getTopTracks(12, serverId),
                musicService.getTopArtists(8, serverId),
                musicService.getRecentlyAddedAlbums(12, serverId),
                musicService.getMixes(serverId).catch(() => [] as GeneratedMixSummaryVM[]),
                musicService.getBecauseYouPlayed(serverId).catch(() => [] as BecauseYouPlayedRowVM[]),
                musicService.listStations(serverId).catch(() => [] as StationVM[]),
                musicService.getYearsWithHistory(serverId).catch(() => [] as number[])
            ]);
            setRecentlyPlayed(recent);
            setTopTracks(top);
            setTopArtists(artistsTop);
            setRecentlyAddedAlbums(recentAlbums);
            setDailyMixes(mixes);
            setBecauseYouPlayed(byp);
            setStations(st);
            setAvailableYears(years);
            setHasAnyHistory(years.length > 0);
        } catch (err) {
            console.error('Failed to load music home rows', err);
        }
    }, [serverId]);

    const startRadioFromSeed = useCallback(async (seed: RadioSeed) => {
        try {
            const queue = await musicService.startRadio(seed, 50, serverId);
            if (queue.tracks.length === 0) return;
            const server = serverId ? serverVault.getServer(serverId) : serverVault.getActiveServer();
            const baseUrl = server?.url || (import.meta.env.VITE_API_BASE_URL as string | undefined)?.replace(/\/api\/?$/, '') || '';
            const items: PlayableMedia[] = queue.tracks.map(t => ({
                id: t.id,
                title: t.title,
                subtitle: t.artist ?? '',
                streamUrl: musicService.getTrackStreamUrl(t.id, baseUrl, audioQualityStore.get()),
                serverId: server?.id,
                container: 'audio',
                playbackContextType: 'Music'
            }));
            startRadio(seed, queue.seedLabel, items);
        } catch (err) {
            console.error('Failed to start radio', err);
        }
    }, [serverId, startRadio]);

    const startStationRadio = useCallback(async (station: StationVM) => {
        await startRadioFromSeed({
            seedKind: station.seedKind,
            seedArtistId: station.seedArtistId,
            seedTrackId: station.seedTrackId,
            seedGenre: station.seedGenre
        });
        musicService.touchStation(station.id, serverId).then(() => {
            setStations(prev => {
                const updated = prev.map(s => s.id === station.id ? { ...s, lastPlayedAt: new Date().toISOString() } : s);
                return [...updated].sort((a, b) => {
                    const av = a.lastPlayedAt ?? a.createdAt;
                    const bv = b.lastPlayedAt ?? b.createdAt;
                    return bv.localeCompare(av);
                });
            });
        });
    }, [startRadioFromSeed, serverId]);

    const deleteStation = useCallback(async (stationId: string) => {
        try {
            await musicService.deleteStation(stationId, serverId);
            setStations(prev => prev.filter(s => s.id !== stationId));
        } catch (err) {
            console.error('Failed to delete station', err);
        }
    }, [serverId]);

    useEffect(() => {
        queueMicrotask(() => { void loadLikedTracks(); });
    }, [loadLikedTracks, refreshSeq]);

    useEffect(() => {
        queueMicrotask(() => { void loadHomeRows(); });
    }, [loadHomeRows, refreshSeq]);

    useEffect(() => {
        const onChange = () => loadLikedTracks();
        window.addEventListener('music-likes-changed', onChange);
        return () => window.removeEventListener('music-likes-changed', onChange);
    }, [loadLikedTracks]);

    const reloadStations = useCallback(async () => {
        try {
            const s = await musicService.listStations(serverId);
            setStations(s);
        } catch (err) {
            console.error('Failed to reload stations', err);
        }
    }, [serverId]);

    useEffect(() => {
        const onChange = () => reloadStations();
        window.addEventListener('music-stations-changed', onChange);
        return () => window.removeEventListener('music-stations-changed', onChange);
    }, [reloadStations]);

    const toggleTrackLike = useCallback(async (trackId: string, currentlyLiked: boolean) => {
        try {
            if (currentlyLiked) {
                await musicService.unlikeTrack(trackId, serverId);
            } else {
                await musicService.likeTrack(trackId, serverId);
            }
            setTracks(prev => prev.map(t => t.id === trackId ? { ...t, isLiked: !currentlyLiked } : t));
            window.dispatchEvent(new CustomEvent('music-likes-changed'));
        } catch (err) {
            console.error('Failed to toggle track like', err);
        }
    }, [serverId]);
    useSignalREvent<string>("MusicArtistUpdated", bumpRefresh);
    useSignalREvent<string>("MusicAlbumUpdated", bumpRefresh);
    useSignalREvent<string>("MusicMixesUpdated", bumpRefresh);

    const loadServerPlayback = useCallback(async () => {
        const list = await musicService.getActiveServerPlayback(serverId);
        setServerPlayback(list);
    }, [serverId]);

    useEffect(() => { queueMicrotask(() => { void loadServerPlayback(); }); }, [loadServerPlayback]);
    useSignalREvent<unknown>("ServerPlaybackUpdated", useCallback(() => { loadServerPlayback(); }, [loadServerPlayback]));
    useSignalREvent<string>("LibraryUpdated", useCallback(() => {
        sessionStorage.removeItem(NAV_STORAGE_KEY);
        setCurrentArtist(null);
        setCurrentAlbum(null);
        setAlbums([]);
        setTracks([]);
        setNav({ view: 'artists' });
        setRefreshSeq(s => s + 1);
    }, []));

    const resetToArtistsView = useCallback(() => {
        sessionStorage.removeItem(NAV_STORAGE_KEY);
        setCurrentArtist(null);
        setCurrentAlbum(null);
        setAlbums([]);
        setTracks([]);
        setNav({ view: 'artists' });
    }, []);

    useEffect(() => {
        if (nav.view !== 'artists') return;
        queueMicrotask(() => setIsLoading(true));
        musicService.getArtists(undefined, serverId)
            .then(setArtists)
            .catch(err => console.error('Failed to load artists', err))
            .finally(() => setIsLoading(false));
    }, [nav.view, serverId, refreshSeq]);

    useEffect(() => {
        if (nav.view !== 'artist' || !nav.artistId) return;
        queueMicrotask(() => {
            setIsLoading(true);
            setSimilarArtists([]);
        });
        musicService.getArtistDetail(nav.artistId, serverId)
            .then(detail => {
                setCurrentArtist(detail.artist);
                setAlbums(detail.albums);
            })
            .catch(err => {
                console.error('Failed to load artist detail', err);
                resetToArtistsView();
            })
            .finally(() => setIsLoading(false));
        musicService.getSimilarArtists(nav.artistId, serverId)
            .then(setSimilarArtists)
            .catch(() => setSimilarArtists([]));
    }, [nav.view, nav.artistId, serverId, refreshSeq, resetToArtistsView]);

    useEffect(() => {
        if (nav.view !== 'album' || !nav.albumId) return;
        queueMicrotask(() => setIsLoading(true));
        musicService.getAlbumDetail(nav.albumId, serverId)
            .then(detail => {
                setCurrentAlbum(detail.album);
                setTracks(detail.tracks);
            })
            .catch(err => {
                console.error('Failed to load album detail', err);
                resetToArtistsView();
            })
            .finally(() => setIsLoading(false));
    }, [nav.view, nav.albumId, serverId, refreshSeq, resetToArtistsView]);

    useEffect(() => {
        if (nav.view !== 'mix' || !nav.mixId) return;
        queueMicrotask(() => setIsLoading(true));
        musicService.getMixDetail(nav.mixId, serverId)
            .then(detail => {
                if (!detail) {
                    resetToArtistsView();
                    return;
                }
                setCurrentMix(detail);
            })
            .catch(err => {
                console.error('Failed to load mix detail', err);
                resetToArtistsView();
            })
            .finally(() => setIsLoading(false));
    }, [nav.view, nav.mixId, serverId, refreshSeq, resetToArtistsView]);

    useEffect(() => {
        if (nav.view !== 'recap') return;
        const targetYear = nav.year ?? new Date().getFullYear();
        queueMicrotask(() => setIsLoading(true));
        musicService.getYearRecap(targetYear, serverId)
            .then(setCurrentRecap)
            .catch(err => {
                console.error('Failed to load year recap', err);
                resetToArtistsView();
            })
            .finally(() => setIsLoading(false));
    }, [nav.view, nav.year, serverId, refreshSeq, resetToArtistsView]);

    useEffect(() => {
        if (nav.view !== 'genres') return;
        queueMicrotask(() => setIsLoading(true));
        musicService.getGenres(serverId)
            .then(setGenres)
            .catch(err => console.error('Failed to load genres', err))
            .finally(() => setIsLoading(false));
    }, [nav.view, serverId, refreshSeq]);

    useEffect(() => {
        if (nav.view !== 'genre' || !nav.genre) return;
        queueMicrotask(() => setIsLoading(true));
        musicService.getGenreContent(nav.genre, serverId)
            .then(content => {
                if (!content) { resetToArtistsView(); return; }
                setCurrentGenre(content);
            })
            .catch(err => {
                console.error('Failed to load genre content', err);
                resetToArtistsView();
            })
            .finally(() => setIsLoading(false));
    }, [nav.view, nav.genre, serverId, refreshSeq, resetToArtistsView]);

    const formatDuration = (seconds?: number): string => {
        if (!seconds || seconds <= 0) return '';
        const m = Math.floor(seconds / 60);
        const s = Math.floor(seconds % 60);
        return `${m}:${s.toString().padStart(2, '0')}`;
    };

    const buildPlayableForTrack = useCallback((track: TrackVM, album: AlbumVM): PlayableMedia => {
        const server = serverId ? serverVault.getServer(serverId) : serverVault.getActiveServer();
        const baseUrl = server?.url || (import.meta.env.VITE_API_BASE_URL as string | undefined)?.replace(/\/api\/?$/, '') || '';
        return {
            id: track.id,
            title: track.title,
            subtitle: `${album.artistName} — ${album.title}`,
            posterUrl: album.artworkUrl,
            streamUrl: musicService.getTrackStreamUrl(track.id, baseUrl, audioQualityStore.get()),
            serverId: server?.id,
            container: 'audio',
            playbackContextType: 'Music'
        };
    }, [serverId]);

    const playFromIndex = (startIndex: number) => {
        if (!currentAlbum) return;
        const items = tracks.map(t => buildPlayableForTrack(t, currentAlbum));
        playQueue(items, startIndex);
    };

    const playWholeAlbum = () => playFromIndex(0);

    const buildPlayableForMixTrack = useCallback((track: TrackVM): PlayableMedia => {
        const server = serverId ? serverVault.getServer(serverId) : serverVault.getActiveServer();
        const baseUrl = server?.url || (import.meta.env.VITE_API_BASE_URL as string | undefined)?.replace(/\/api\/?$/, '') || '';
        return {
            id: track.id,
            title: track.title,
            subtitle: track.artist ?? '',
            posterUrl: currentMix?.artworkUrl,
            streamUrl: musicService.getTrackStreamUrl(track.id, baseUrl, audioQualityStore.get()),
            serverId: server?.id,
            container: 'audio',
            playbackContextType: 'Music'
        };
    }, [serverId, currentMix?.artworkUrl]);

    const playMixFromIndex = (startIndex: number) => {
        if (!currentMix) return;
        const items = currentMix.tracks.map(buildPlayableForMixTrack);
        playQueue(items, startIndex);
    };

    const buildPlayableForArtistTrack = useCallback((t: ArtistTrackVM, artistName: string): PlayableMedia => {
        const server = serverId ? serverVault.getServer(serverId) : serverVault.getActiveServer();
        const baseUrl = server?.url || (import.meta.env.VITE_API_BASE_URL as string | undefined)?.replace(/\/api\/?$/, '') || '';
        return {
            id: t.id,
            title: t.title,
            subtitle: `${artistName} — ${t.albumTitle ?? ''}`.replace(/ — $/, ''),
            posterUrl: t.albumArtworkUrl,
            streamUrl: musicService.getTrackStreamUrl(t.id, baseUrl, audioQualityStore.get()),
            serverId: server?.id,
            container: 'audio',
            playbackContextType: 'Music'
        };
    }, [serverId]);

    const playArtistTrackList = useCallback((tracks: ArtistTrackVM[], startIndex: number) => {
        if (tracks.length === 0) return;
        const items = tracks.map(t => buildPlayableForArtistTrack(t, t.albumTitle ? '' : ''));
        playQueue(items, Math.max(0, Math.min(startIndex, items.length - 1)));
    }, [buildPlayableForArtistTrack, playQueue]);

    const playArtist = async (shuffle: boolean) => {
        if (!currentArtist) return;
        try {
            const artistTracks = await musicService.getArtistTracks(currentArtist.id, serverId);
            if (artistTracks.length === 0) return;
            const items = artistTracks.map(t => buildPlayableForArtistTrack(t, currentArtist.name));
            if (shuffle && !isShuffled) {
                toggleShuffle();
            } else if (!shuffle && isShuffled) {
                toggleShuffle();
            }
            playQueue(items, 0);
        } catch (err) {
            console.error('Failed to load artist tracks', err);
        }
    };

    const breadcrumbs = (
        <div className="flex items-center gap-2 text-sm mb-6">
            <button onClick={() => updateNav({ view: 'artists' })} className={`hover:text-[var(--vora-accent-text)] transition-colors cursor-pointer ${nav.view === 'artists' ? 'text-[var(--vora-accent-text)] font-bold' : 'text-[var(--vora-text-muted)]'}`}>
                Artists
            </button>
            {nav.view !== 'artists' && currentArtist && (
                <>
                    <span className="text-[var(--vora-text-disabled)]">/</span>
                    <button
                        onClick={() => currentArtist && updateNav({ view: 'artist', artistId: currentArtist.id })}
                        className={`hover:text-[var(--vora-accent-text)] transition-colors cursor-pointer ${nav.view === 'artist' ? 'text-[var(--vora-accent-text)] font-bold' : 'text-[var(--vora-text-muted)]'}`}
                    >
                        {currentArtist.name}
                    </button>
                </>
            )}
            {nav.view === 'album' && currentAlbum && (
                <>
                    <span className="text-[var(--vora-text-disabled)]">/</span>
                    <span className="text-[var(--vora-accent-text)] font-bold">{currentAlbum.title}</span>
                </>
            )}
            {nav.view === 'likes' && (
                <>
                    <span className="text-[var(--vora-text-disabled)]">/</span>
                    <span className="text-[var(--vora-accent-text)] font-bold">Liked Songs</span>
                </>
            )}
            {nav.view === 'top' && (
                <>
                    <span className="text-[var(--vora-text-disabled)]">/</span>
                    <span className="text-[var(--vora-accent-text)] font-bold">Your Top Tracks</span>
                </>
            )}
            {nav.view === 'mix' && currentMix && (
                <>
                    <span className="text-[var(--vora-text-disabled)]">/</span>
                    <span className="text-[var(--vora-accent-text)] font-bold truncate max-w-xs" title={currentMix.name}>{currentMix.name}</span>
                </>
            )}
            {nav.view === 'recap' && (
                <>
                    <span className="text-[var(--vora-text-disabled)]">/</span>
                    <span className="text-[var(--vora-accent-text)] font-bold">{nav.year ?? new Date().getFullYear()} in music</span>
                </>
            )}
            {nav.view === 'genres' && (
                <>
                    <span className="text-[var(--vora-text-disabled)]">/</span>
                    <span className="text-[var(--vora-accent-text)] font-bold">Genres</span>
                </>
            )}
            {nav.view === 'genre' && (
                <>
                    <span className="text-[var(--vora-text-disabled)]">/</span>
                    <button onClick={() => updateNav({ view: 'genres' })} className="text-[var(--vora-text-muted)] hover:text-[var(--vora-accent-text)] transition-colors cursor-pointer">Genres</button>
                    <span className="text-[var(--vora-text-disabled)]">/</span>
                    <span className="text-[var(--vora-accent-text)] font-bold">{nav.genre}</span>
                </>
            )}
        </div>
    );

    const handleSearchResultClick = (r: MusicSearchResultVM) => {
        setSearchQuery('');
        setSearchResults([]);
        if (r.type === 'Artist' && r.artistId) {
            updateNav({ view: 'artist', artistId: r.artistId });
        } else if (r.type === 'Album' && r.albumId) {
            updateNav({ view: 'album', albumId: r.albumId, artistId: r.artistId });
        } else if (r.type === 'Track' && r.albumId) {
            updateNav({ view: 'album', albumId: r.albumId, artistId: r.artistId });
        }
    };

    const searchBar = (
        <div className="mb-6 relative">
            <svg className="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-[var(--vora-text-muted)]" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" /></svg>
            <input
                type="text"
                value={searchQuery}
                onChange={e => setSearchQuery(e.target.value)}
                placeholder="Search artists, albums, tracks..."
                className="w-full bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] rounded-lg pl-9 pr-9 py-2 text-sm text-[var(--vora-text-primary)] outline-none focus:border-[var(--vora-accent-500)] placeholder-gray-600"
            />
            {searchQuery && (
                <button
                    type="button"
                    onClick={() => setSearchQuery('')}
                    className="absolute right-2 top-1/2 -translate-y-1/2 p-1 text-[var(--vora-text-muted)] hover:text-[var(--vora-accent-text)] transition-colors cursor-pointer"
                    title="Clear"
                >
                    <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" /></svg>
                </button>
            )}
        </div>
    );

    const activeBackgroundUrl = searchActive ? null
        : nav.view === 'album' ? currentAlbum?.backgroundUrl
        : null;

    return (
        <div className="relative">
            {activeBackgroundUrl && (
                <div className="absolute -inset-x-6 -top-6 z-0 pointer-events-none">
                    <img src={activeBackgroundUrl} className="w-full h-[40vh] object-cover opacity-25" alt="" />
                    <div className="absolute inset-0 bg-gradient-to-b from-transparent via-gray-950/70 to-gray-950" />
                </div>
            )}
            <div className="relative z-10">
            <MusicServerSwitcher />
            {breadcrumbs}
            {searchBar}

            {searchActive ? (
                <div>
                    {isSearching ? (
                        <div className="text-[var(--vora-text-muted)] py-12 text-center">Searching...</div>
                    ) : searchResults.length === 0 ? (
                        <div className="text-[var(--vora-text-muted)] py-12 text-center bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] rounded-lg">
                            No music matches “{searchQuery}”.
                        </div>
                    ) : (
                        <div className="space-y-1.5">
                            {searchResults.map(r => (
                                <button
                                    key={`${r.type}-${r.id}`}
                                    type="button"
                                    onClick={() => handleSearchResultClick(r)}
                                    className="w-full flex items-center gap-3 bg-[var(--vora-bg-sunken)] hover:bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] hover:border-[var(--vora-accent-500)] rounded-lg p-2.5 transition-all cursor-pointer text-left"
                                >
                                    <div className={`w-12 h-12 bg-[var(--vora-bg-canvas)] border border-[var(--vora-border-subtle)] flex items-center justify-center overflow-hidden shrink-0 ${r.type === 'Artist' ? 'rounded-full' : 'rounded'}`}>
                                        {r.artworkUrl
                                            ? <img src={r.artworkUrl} alt="" className="w-full h-full object-cover" />
                                            : <svg className="w-5 h-5 text-[var(--vora-text-disabled)]" fill="currentColor" viewBox="0 0 24 24"><path d="M12 3v10.55c-.59-.34-1.27-.55-2-.55-2.21 0-4 1.79-4 4s1.79 4 4 4 4-1.79 4-4V7h4V3h-6z" /></svg>}
                                    </div>
                                    <div className="flex-1 min-w-0">
                                        <div className="text-sm font-bold text-[var(--vora-text-primary)] truncate">{r.title}</div>
                                        {r.subtitle && <div className="text-xs text-[var(--vora-text-muted)] truncate">{r.subtitle}</div>}
                                    </div>
                                    <span className="text-[10px] font-bold uppercase tracking-wide text-[var(--vora-text-muted)] bg-[var(--vora-bg-surface)] rounded px-2 py-0.5 shrink-0">{r.type}</span>
                                </button>
                            ))}
                        </div>
                    )}
                </div>
            ) : (
            <>
            {nav.view === 'artists' && (
                isLoading ? (
                    <div className="text-[var(--vora-text-muted)] py-12 text-center">Loading artists...</div>
                ) : artists.length === 0 ? (
                    <div className="text-[var(--vora-text-muted)] py-12 text-center bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] rounded-lg">
                        <p className="mb-2">No music in your library yet.</p>
                        <p className="text-xs">Create a Music library in Server Settings, point it at a folder of audio files, then trigger a scan.</p>
                    </div>
                ) : (
                    <>
                    {serverPlayback.length > 0 && (
                        <div className="mb-8">
                            <h2 className="text-lg font-bold text-[var(--vora-text-primary)] mb-3 flex items-center gap-2">
                                <span className="inline-block w-2 h-2 rounded-full bg-emerald-400 animate-pulse" />
                                Listening Now
                            </h2>
                            <div className="flex gap-3 overflow-x-auto pb-2 -mx-2 px-2">
                                {serverPlayback.map(s => (
                                    <div
                                        key={s.profileId}
                                        className="shrink-0 flex items-center gap-3 bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] rounded-lg p-3 min-w-[280px] max-w-[320px]"
                                        title={`${s.profileName} is listening to ${s.trackTitle}${s.artist ? ` by ${s.artist}` : ''}`}
                                    >
                                        <div className="w-12 h-12 rounded bg-[var(--vora-bg-canvas)] border border-[var(--vora-border-subtle)] overflow-hidden shrink-0">
                                            {s.albumArtworkUrl
                                                ? <img src={s.albumArtworkUrl} alt="" className="w-full h-full object-cover" />
                                                : <div className="w-full h-full flex items-center justify-center text-[var(--vora-text-disabled)]"><svg className="w-6 h-6" fill="currentColor" viewBox="0 0 24 24"><path d="M12 3v10.55c-.59-.34-1.27-.55-2-.55-2.21 0-4 1.79-4 4s1.79 4 4 4 4-1.79 4-4V7h4V3h-6z" /></svg></div>}
                                        </div>
                                        <div className="flex-1 min-w-0">
                                            <div className="flex items-center gap-2 min-w-0">
                                                {s.profileImageUrl
                                                    ? <img src={s.profileImageUrl} alt="" className="w-5 h-5 rounded-full shrink-0 object-cover" />
                                                    : <div className="w-5 h-5 rounded-full bg-[var(--vora-accent-500)] text-[var(--vora-text-primary)] text-[10px] font-bold flex items-center justify-center shrink-0">{s.profileName.charAt(0).toUpperCase()}</div>}
                                                <span className="text-xs text-emerald-300 font-bold truncate">{s.profileName}</span>
                                            </div>
                                            <div className="text-sm text-[var(--vora-text-primary)] truncate mt-0.5" title={s.trackTitle}>{s.trackTitle}</div>
                                            <div className="text-xs text-[var(--vora-text-muted)] truncate">{s.artist ?? s.albumTitle ?? ''}</div>
                                        </div>
                                    </div>
                                ))}
                            </div>
                        </div>
                    )}

                    {dailyMixes.filter(m => m.kind === 'DailyMix' || m.kind === 'DiscoverMix').length > 0 && (
                        <div className="mb-8">
                            <h2 className="text-lg font-bold text-[var(--vora-text-primary)] mb-3">Made for You</h2>
                            <div className="flex gap-4 overflow-x-auto pb-2 -mx-2 px-2">
                                {dailyMixes.filter(m => m.kind === 'DailyMix' || m.kind === 'DiscoverMix').map(mix => (
                                    <button
                                        key={mix.id}
                                        type="button"
                                        onClick={() => updateNav({ view: 'mix', mixId: mix.id })}
                                        className="w-36 sm:w-40 shrink-0 group text-left cursor-pointer"
                                        title={mix.name}
                                    >
                                        <div className={`w-full aspect-square rounded border border-[var(--vora-border-subtle)] group-hover:border-[var(--vora-accent-500)] transition-all overflow-hidden mb-2 relative ${mix.kind === 'DiscoverMix' ? 'bg-gradient-to-br from-emerald-700 via-teal-900 to-cyan-900' : 'bg-gradient-to-br from-orange-700 via-purple-900 to-indigo-900'}`}>
                                            {mix.artworkUrl
                                                ? <img src={mix.artworkUrl} alt="" className="w-full h-full object-cover opacity-70" />
                                                : null}
                                            <div className="absolute inset-0 flex flex-col justify-end p-3 bg-gradient-to-t from-black/80 via-black/30 to-transparent">
                                                <div className={`text-xs uppercase tracking-widest font-bold ${mix.kind === 'DiscoverMix' ? 'text-emerald-300/90' : 'text-orange-300/90'}`}>
                                                    {mix.kind === 'DiscoverMix' ? 'Discover' : `Daily Mix ${mix.slot}`}
                                                </div>
                                                <div className="text-sm font-bold text-[var(--vora-text-primary)] drop-shadow-md truncate">{mix.descriptionTag ?? 'Mix'}</div>
                                            </div>
                                        </div>
                                        <div className="text-sm font-bold text-[var(--vora-text-primary)] truncate" title={mix.name}>{mix.name}</div>
                                        <div className="text-xs text-[var(--vora-text-muted)]">{mix.trackCount} tracks</div>
                                    </button>
                                ))}
                            </div>
                        </div>
                    )}

                    {dailyMixes.filter(m => m.kind === 'ReleaseRadar').length > 0 && (
                        <div className="mb-8">
                            <h2 className="text-lg font-bold text-[var(--vora-text-primary)] mb-3 flex items-center gap-2">
                                Release Radar
                                <span className="px-2 py-0.5 text-[10px] uppercase tracking-widest font-bold rounded bg-rose-500/20 text-rose-300 border border-rose-500/30">New</span>
                            </h2>
                            <div className="flex gap-4 overflow-x-auto pb-2 -mx-2 px-2">
                                {dailyMixes.filter(m => m.kind === 'ReleaseRadar').map(mix => (
                                    <button
                                        key={mix.id}
                                        type="button"
                                        onClick={() => updateNav({ view: 'mix', mixId: mix.id })}
                                        className="w-36 sm:w-40 shrink-0 group text-left cursor-pointer"
                                        title={mix.name}
                                    >
                                        <div className="w-full aspect-square rounded border border-[var(--vora-border-subtle)] group-hover:border-rose-400 transition-all overflow-hidden mb-2 relative bg-gradient-to-br from-rose-700 via-fuchsia-900 to-violet-900">
                                            {mix.artworkUrl
                                                ? <img src={mix.artworkUrl} alt="" className="w-full h-full object-cover opacity-70" />
                                                : null}
                                            <div className="absolute inset-0 flex flex-col justify-end p-3 bg-gradient-to-t from-black/80 via-black/30 to-transparent">
                                                <div className="text-xs uppercase tracking-widest font-bold text-rose-200/90">Release Radar</div>
                                                <div className="text-sm font-bold text-[var(--vora-text-primary)] drop-shadow-md truncate">{mix.descriptionTag ?? 'New releases'}</div>
                                            </div>
                                        </div>
                                        <div className="text-sm font-bold text-[var(--vora-text-primary)] truncate" title={mix.name}>{mix.name}</div>
                                        <div className="text-xs text-[var(--vora-text-muted)]">{mix.trackCount} tracks</div>
                                    </button>
                                ))}
                            </div>
                        </div>
                    )}

                    {dailyMixes.filter(m => m.kind === 'MoodMix').length > 0 && (
                        <div className="mb-8">
                            <h2 className="text-lg font-bold text-[var(--vora-text-primary)] mb-3">Moods</h2>
                            <div className="flex gap-4 overflow-x-auto pb-2 -mx-2 px-2">
                                {dailyMixes.filter(m => m.kind === 'MoodMix').map(mix => (
                                    <button
                                        key={mix.id}
                                        type="button"
                                        onClick={() => updateNav({ view: 'mix', mixId: mix.id })}
                                        className="w-36 sm:w-40 shrink-0 group text-left cursor-pointer"
                                        title={mix.name}
                                    >
                                        <div className="w-full aspect-square rounded bg-gradient-to-br from-blue-700 via-purple-800 to-pink-800 border border-[var(--vora-border-subtle)] group-hover:border-pink-400 transition-all overflow-hidden mb-2 relative">
                                            {mix.artworkUrl
                                                ? <img src={mix.artworkUrl} alt="" className="w-full h-full object-cover opacity-70" />
                                                : null}
                                            <div className="absolute inset-0 flex items-center justify-center bg-gradient-to-t from-black/70 to-transparent">
                                                <div className="text-xl sm:text-2xl font-bold text-[var(--vora-text-primary)] drop-shadow-lg text-center px-2">{mix.name}</div>
                                            </div>
                                        </div>
                                        <div className="text-xs text-[var(--vora-text-muted)] text-center">{mix.trackCount} tracks</div>
                                    </button>
                                ))}
                            </div>
                        </div>
                    )}

                    {stations.length > 0 && (
                        <div className="mb-8">
                            <h2 className="text-lg font-bold text-[var(--vora-text-primary)] mb-3">Your Stations</h2>
                            <div className="flex gap-4 overflow-x-auto pb-2 -mx-2 px-2">
                                {stations.map(station => (
                                    <div
                                        key={station.id}
                                        className="w-32 sm:w-36 shrink-0 group cursor-pointer relative"
                                        title={station.name}
                                    >
                                        <button
                                            type="button"
                                            onClick={() => startStationRadio(station)}
                                            className="block w-full text-left"
                                        >
                                            <div className="w-full aspect-square rounded-full bg-gradient-to-br from-indigo-700 via-purple-900 to-orange-700 border border-[var(--vora-border-subtle)] group-hover:border-[var(--vora-accent-500)] transition-all overflow-hidden mb-2 relative">
                                                {station.artworkUrl
                                                    ? <img src={station.artworkUrl} alt="" className="w-full h-full object-cover opacity-70" />
                                                    : null}
                                                <div className="absolute inset-0 flex items-center justify-center">
                                                    <svg className="w-10 h-10 text-[var(--vora-text-primary)]/90 drop-shadow-lg" fill="none" stroke="currentColor" strokeWidth={1.5} viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" d="M9.348 14.652a3.75 3.75 0 010-5.304m5.304 0a3.75 3.75 0 010 5.304m-7.425 2.121a6.75 6.75 0 010-9.546m9.546 0a6.75 6.75 0 010 9.546M5.106 18.894c-3.808-3.807-3.808-9.98 0-13.788m13.788 0c3.808 3.807 3.808 9.98 0 13.788M12 12h.01" /></svg>
                                                </div>
                                            </div>
                                            <div className="text-sm font-bold text-[var(--vora-text-primary)] truncate text-center" title={station.name}>{station.name}</div>
                                            {station.subtitleHint && <div className="text-xs text-[var(--vora-text-muted)] truncate text-center">{station.subtitleHint}</div>}
                                        </button>
                                        <button
                                            type="button"
                                            onClick={async (e) => {
                                                e.stopPropagation();
                                                const ok = await dialog.confirm(`Delete station "${station.name}"?`);
                                                if (ok) deleteStation(station.id);
                                            }}
                                            className="absolute top-2 right-2 p-1 rounded-full bg-[var(--vora-bg-canvas)]/80 text-[var(--vora-text-secondary)] hover:text-[var(--vora-danger-text)] opacity-0 group-hover:opacity-100 transition-opacity cursor-pointer"
                                            title="Delete station"
                                        >
                                            <svg className="w-3 h-3" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" /></svg>
                                        </button>
                                    </div>
                                ))}
                            </div>
                        </div>
                    )}

                    {becauseYouPlayed.map(row => (
                        <div key={row.seedArtistId} className="mb-8">
                            <h2 className="text-lg font-bold text-[var(--vora-text-primary)] mb-3 truncate">{row.heading}</h2>
                            <div className="flex gap-4 overflow-x-auto pb-2 -mx-2 px-2">
                                {row.tracks.map((t, idx) => (
                                    <button
                                        key={t.id}
                                        type="button"
                                        onClick={() => playArtistTrackList(row.tracks, idx)}
                                        className="w-32 sm:w-36 shrink-0 group text-left cursor-pointer"
                                        title={t.title}
                                    >
                                        <div className="w-full aspect-square rounded bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] group-hover:border-[var(--vora-accent-500)] transition-all overflow-hidden mb-2">
                                            {t.albumArtworkUrl
                                                ? <img src={t.albumArtworkUrl} alt="" className="w-full h-full object-cover" />
                                                : <div className="w-full h-full flex items-center justify-center text-[var(--vora-text-disabled)]"><svg className="w-8 h-8" fill="currentColor" viewBox="0 0 24 24"><path d="M12 3v10.55c-.59-.34-1.27-.55-2-.55-2.21 0-4 1.79-4 4s1.79 4 4 4 4-1.79 4-4V7h4V3h-6z" /></svg></div>}
                                        </div>
                                        <div className="text-sm font-bold text-[var(--vora-text-primary)] truncate" title={t.title}>{t.title}</div>
                                        <div className="text-xs text-[var(--vora-text-muted)] truncate">{t.artist ?? t.albumTitle ?? ''}</div>
                                    </button>
                                ))}
                            </div>
                        </div>
                    ))}

                    {recentlyPlayed.length > 0 && (
                        <div className="mb-8">
                            <h2 className="text-lg font-bold text-[var(--vora-text-primary)] mb-3">Recently Played</h2>
                            <div className="flex gap-4 overflow-x-auto pb-2 -mx-2 px-2">
                                {recentlyPlayed.map((t, idx) => (
                                    <button
                                        key={t.id}
                                        type="button"
                                        onClick={() => playArtistTrackList(recentlyPlayed, idx)}
                                        className="w-32 sm:w-36 shrink-0 group text-left cursor-pointer"
                                        title="Play"
                                    >
                                        <div className="w-full aspect-square rounded bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] group-hover:border-[var(--vora-accent-500)] transition-all overflow-hidden mb-2">
                                            {t.albumArtworkUrl
                                                ? <img src={t.albumArtworkUrl} alt="" className="w-full h-full object-cover" />
                                                : <div className="w-full h-full flex items-center justify-center text-[var(--vora-text-disabled)]"><svg className="w-8 h-8" fill="currentColor" viewBox="0 0 24 24"><path d="M12 3v10.55c-.59-.34-1.27-.55-2-.55-2.21 0-4 1.79-4 4s1.79 4 4 4 4-1.79 4-4V7h4V3h-6z" /></svg></div>}
                                        </div>
                                        <div className="text-sm font-bold text-[var(--vora-text-primary)] truncate" title={t.title}>{t.title}</div>
                                        <div className="text-xs text-[var(--vora-text-muted)] truncate">{t.albumTitle ?? ''}</div>
                                    </button>
                                ))}
                            </div>
                        </div>
                    )}

                    {recentlyAddedAlbums.length > 0 && (
                        <div className="mb-8">
                            <h2 className="text-lg font-bold text-[var(--vora-text-primary)] mb-3">Recently Added</h2>
                            <div className="flex gap-4 overflow-x-auto pb-2 -mx-2 px-2">
                                {recentlyAddedAlbums.map(album => (
                                    <button
                                        key={album.id}
                                        type="button"
                                        onClick={() => updateNav({ view: 'album', artistId: album.artistId, albumId: album.id })}
                                        className="w-32 sm:w-36 shrink-0 group text-left cursor-pointer"
                                        title={`${album.title} — ${album.artistName}`}
                                    >
                                        <div className="w-full aspect-square rounded bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] group-hover:border-[var(--vora-accent-500)] transition-all overflow-hidden mb-2">
                                            {album.artworkUrl
                                                ? <img src={album.artworkUrl} alt={album.title} className="w-full h-full object-cover" />
                                                : <div className="w-full h-full flex items-center justify-center text-[var(--vora-text-disabled)]"><svg className="w-8 h-8" fill="currentColor" viewBox="0 0 24 24"><path d="M12 3v10.55c-.59-.34-1.27-.55-2-.55-2.21 0-4 1.79-4 4s1.79 4 4 4 4-1.79 4-4V7h4V3h-6z" /></svg></div>}
                                        </div>
                                        <div className="text-sm font-bold text-[var(--vora-text-primary)] truncate" title={album.title}>{album.title}</div>
                                        <div className="text-xs text-[var(--vora-text-muted)] truncate">{album.artistName}</div>
                                    </button>
                                ))}
                            </div>
                        </div>
                    )}

                    {topArtists.length > 0 && (
                        <div className="mb-8">
                            <h2 className="text-lg font-bold text-[var(--vora-text-primary)] mb-3">Top Artists</h2>
                            <div className="flex gap-4 overflow-x-auto pb-2 -mx-2 px-2">
                                {topArtists.map(artist => (
                                    <button
                                        key={artist.id}
                                        type="button"
                                        onClick={() => updateNav({ view: 'artist', artistId: artist.id })}
                                        className="w-28 sm:w-32 shrink-0 group text-left cursor-pointer"
                                        title={artist.name}
                                    >
                                        <div className="w-full aspect-square rounded-full bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] group-hover:border-[var(--vora-accent-500)] transition-all overflow-hidden mb-2">
                                            {artist.artworkUrl
                                                ? <img src={artist.artworkUrl} alt={artist.name} className="w-full h-full object-cover" />
                                                : <div className="w-full h-full flex items-center justify-center text-[var(--vora-text-disabled)]"><svg className="w-10 h-10" fill="currentColor" viewBox="0 0 24 24"><path d="M12 12c2.21 0 4-1.79 4-4s-1.79-4-4-4-4 1.79-4 4 1.79 4 4 4zm0 2c-2.67 0-8 1.34-8 4v2h16v-2c0-2.66-5.33-4-8-4z" /></svg></div>}
                                        </div>
                                        <div className="text-sm font-bold text-[var(--vora-text-primary)] truncate text-center" title={artist.name}>{artist.name}</div>
                                    </button>
                                ))}
                            </div>
                        </div>
                    )}

                    <h2 className="text-lg font-bold text-[var(--vora-text-primary)] mb-3">All Artists</h2>
                    <div className="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-6 gap-4">
                        <div
                            onClick={() => updateNav({ view: 'likes' })}
                            className="relative flex flex-col items-center bg-gradient-to-br from-orange-700 to-purple-900 hover:from-orange-600 hover:to-purple-800 border border-[var(--vora-border-subtle)] hover:border-orange-400 rounded-lg p-4 transition-all cursor-pointer text-left"
                            title="Liked Songs"
                        >
                            <div className="w-24 h-24 rounded bg-gradient-to-br from-orange-500/40 to-purple-500/40 border border-orange-400/30 flex items-center justify-center overflow-hidden mb-3">
                                <svg className="w-12 h-12 text-[var(--vora-text-primary)]" fill="currentColor" viewBox="0 0 24 24"><path d="M12 21.35l-1.45-1.32C5.4 15.36 2 12.28 2 8.5 2 5.42 4.42 3 7.5 3c1.74 0 3.41.81 4.5 2.09C13.09 3.81 14.76 3 16.5 3 19.58 3 22 5.42 22 8.5c0 3.78-3.4 6.86-8.55 11.54L12 21.35z" /></svg>
                            </div>
                            <div className="font-bold text-sm text-[var(--vora-text-primary)] text-center w-full">Liked Songs</div>
                            <div className="text-xs text-orange-100/80 text-center">{likedCount} {likedCount === 1 ? 'track' : 'tracks'}</div>
                        </div>
                        <div
                            onClick={() => updateNav({ view: 'top' })}
                            className="relative flex flex-col items-center bg-gradient-to-br from-indigo-700 to-cyan-900 hover:from-indigo-600 hover:to-cyan-800 border border-[var(--vora-border-subtle)] hover:border-cyan-400 rounded-lg p-4 transition-all cursor-pointer text-left"
                            title="Top Tracks"
                        >
                            <div className="w-24 h-24 rounded bg-gradient-to-br from-indigo-500/40 to-cyan-500/40 border border-cyan-400/30 flex items-center justify-center overflow-hidden mb-3">
                                <svg className="w-12 h-12 text-[var(--vora-text-primary)]" fill="currentColor" viewBox="0 0 24 24"><path d="M12 1l3 6h6l-5 4 2 7-6-4-6 4 2-7-5-4h6z" /></svg>
                            </div>
                            <div className="font-bold text-sm text-[var(--vora-text-primary)] text-center w-full">Your Top Tracks</div>
                            <div className="text-xs text-cyan-100/80 text-center">{topTracks.length > 0 ? `${topTracks.length} tracks` : 'Build a history first'}</div>
                        </div>
                        <div
                            onClick={() => updateNav({ view: 'genres' })}
                            className="relative flex flex-col items-center bg-gradient-to-br from-emerald-700 to-teal-900 hover:from-emerald-600 hover:to-teal-800 border border-[var(--vora-border-subtle)] hover:border-emerald-400 rounded-lg p-4 transition-all cursor-pointer text-left"
                            title="Browse by genre"
                        >
                            <div className="w-24 h-24 rounded bg-gradient-to-br from-emerald-500/40 to-teal-500/40 border border-emerald-400/30 flex items-center justify-center overflow-hidden mb-3">
                                <svg className="w-12 h-12 text-[var(--vora-text-primary)]" fill="none" stroke="currentColor" strokeWidth={1.5} viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" d="M3.75 6A2.25 2.25 0 016 3.75h2.25A2.25 2.25 0 0110.5 6v2.25a2.25 2.25 0 01-2.25 2.25H6a2.25 2.25 0 01-2.25-2.25V6zM3.75 15.75A2.25 2.25 0 016 13.5h2.25a2.25 2.25 0 012.25 2.25V18a2.25 2.25 0 01-2.25 2.25H6A2.25 2.25 0 013.75 18v-2.25zM13.5 6a2.25 2.25 0 012.25-2.25H18A2.25 2.25 0 0120.25 6v2.25A2.25 2.25 0 0118 10.5h-2.25a2.25 2.25 0 01-2.25-2.25V6zM13.5 15.75a2.25 2.25 0 012.25-2.25H18a2.25 2.25 0 012.25 2.25V18A2.25 2.25 0 0118 20.25h-2.25A2.25 2.25 0 0113.5 18v-2.25z" /></svg>
                            </div>
                            <div className="font-bold text-sm text-[var(--vora-text-primary)] text-center w-full">Browse by Genre</div>
                            <div className="text-xs text-emerald-100/80 text-center">Discover by mood</div>
                        </div>
                        {hasAnyHistory && (
                            <div
                                onClick={() => updateNav({ view: 'recap', year: availableYears[0] ?? new Date().getFullYear() })}
                                className="relative flex flex-col items-center bg-gradient-to-br from-pink-700 to-amber-700 hover:from-pink-600 hover:to-amber-600 border border-[var(--vora-border-subtle)] hover:border-pink-400 rounded-lg p-4 transition-all cursor-pointer text-left"
                                title="Year in music"
                            >
                                <div className="w-24 h-24 rounded bg-gradient-to-br from-pink-500/40 to-amber-500/40 border border-pink-400/30 flex items-center justify-center overflow-hidden mb-3">
                                    <svg className="w-12 h-12 text-[var(--vora-text-primary)]" fill="none" stroke="currentColor" strokeWidth={1.5} viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" d="M9 17.25v1.007a3 3 0 01-.879 2.122L7.5 21h9l-.621-.621A3 3 0 0115 18.257V17.25m6-12V15a2.25 2.25 0 01-2.25 2.25H5.25A2.25 2.25 0 013 15V5.25m18 0A2.25 2.25 0 0018.75 3H5.25A2.25 2.25 0 003 5.25m18 0V12a2.25 2.25 0 01-2.25 2.25H5.25A2.25 2.25 0 013 12V5.25" /></svg>
                                </div>
                                <div className="font-bold text-sm text-[var(--vora-text-primary)] text-center w-full">Year in Music</div>
                                <div className="text-xs text-pink-100/80 text-center">{availableYears[0] ?? new Date().getFullYear()}</div>
                            </div>
                        )}
                        {artists.map(artist => (
                            <div
                                key={artist.id}
                                onClick={() => updateNav({ view: 'artist', artistId: artist.id })}
                                className="relative flex flex-col items-center bg-[var(--vora-bg-sunken)] hover:bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] hover:border-[var(--vora-accent-500)] rounded-lg p-4 transition-all cursor-pointer text-left"
                            >
                                {isServerAdmin && (
                                    <button
                                        type="button"
                                        onClick={(e) => { e.stopPropagation(); setEditing({ kind: 'artist', artist }); }}
                                        className="absolute top-2 right-2 p-1.5 rounded bg-[var(--vora-bg-canvas)]/80 text-[var(--vora-text-muted)] hover:text-[var(--vora-accent-text)] transition-colors cursor-pointer opacity-0 group-hover:opacity-100"
                                        title="Edit artist"
                                        style={{ opacity: 1 }}
                                    >
                                        <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15.232 5.232l3.536 3.536m-2.036-5.036a2.5 2.5 0 113.536 3.536L6.5 21.036H3v-3.572L16.732 3.732z" /></svg>
                                    </button>
                                )}
                                <div className="w-24 h-24 rounded-full bg-[var(--vora-bg-canvas)] border border-[var(--vora-border-subtle)] flex items-center justify-center overflow-hidden mb-3">
                                    {artist.artworkUrl
                                        ? <img src={artist.artworkUrl} alt={artist.name} className="max-w-full max-h-full object-cover" />
                                        : <svg className="w-10 h-10 text-[var(--vora-text-disabled)]" fill="currentColor" viewBox="0 0 24 24"><path d="M12 12c2.21 0 4-1.79 4-4s-1.79-4-4-4-4 1.79-4 4 1.79 4 4 4zm0 2c-2.67 0-8 1.34-8 4v2h16v-2c0-2.66-5.33-4-8-4z" /></svg>}
                                </div>
                                <div className="font-bold text-sm text-[var(--vora-text-primary)] text-center truncate w-full" title={artist.name}>{artist.name}</div>
                            </div>
                        ))}
                    </div>
                    </>
                )
            )}

            {nav.view === 'artist' && currentArtist && (() => {
                const heroBackdrop = currentArtist.bannerUrl || currentArtist.backgroundUrl;
                const hasArtwork = !!currentArtist.artworkUrl;
                return (
                    <div className="relative w-full rounded-lg overflow-hidden mb-6 border border-[var(--vora-border-subtle)] bg-[var(--vora-bg-sunken)]" style={{ minHeight: '13rem' }}>
                        {heroBackdrop ? (
                            <img src={heroBackdrop} alt="" className="absolute inset-0 w-full h-full object-cover" />
                        ) : (
                            <div className="absolute inset-0 bg-gradient-to-br from-gray-900 via-gray-900 to-gray-800" />
                        )}
                        <div className="absolute inset-0 bg-gradient-to-t from-gray-950 via-gray-950/55 to-gray-950/20" />
                        <div className="absolute inset-0 bg-gradient-to-r from-gray-950/40 via-transparent to-gray-950/40" />

                        <div className="relative flex flex-col sm:flex-row items-stretch sm:items-end gap-4 p-5 sm:p-6 min-h-[13rem]">
                            {hasArtwork && (
                                <div className="w-28 h-28 sm:w-36 sm:h-36 rounded-full bg-[var(--vora-bg-canvas)] border border-[var(--vora-border-subtle)]/80 overflow-hidden shrink-0 shadow-lg self-start sm:self-end">
                                    <img src={currentArtist.artworkUrl} alt={currentArtist.name} className="w-full h-full object-cover" />
                                </div>
                            )}
                            <div className="flex-1 min-w-0 flex flex-col justify-end gap-3">
                                <div>
                                    <div className="text-xs uppercase tracking-widest text-[var(--vora-text-secondary)] font-bold mb-1">Artist</div>
                                    {currentArtist.clearLogoUrl ? (
                                        <img
                                            src={currentArtist.clearLogoUrl}
                                            alt={currentArtist.name}
                                            className="max-h-16 sm:max-h-20 max-w-full object-contain drop-shadow-[0_2px_6px_rgba(0,0,0,0.8)]"
                                            style={{ objectPosition: 'left center' }}
                                        />
                                    ) : (
                                        <h2 className="text-3xl sm:text-4xl font-bold text-[var(--vora-text-primary)] drop-shadow-[0_2px_6px_rgba(0,0,0,0.8)] truncate">{currentArtist.name}</h2>
                                    )}
                                </div>
                                <div className="flex flex-wrap items-center gap-2">
                                    {albums.length > 0 && (
                                        <>
                                            <button
                                                type="button"
                                                onClick={() => playArtist(false)}
                                                className="text-sm px-4 py-2 bg-[var(--vora-accent-500)] hover:bg-[var(--vora-accent-500)] text-[var(--vora-text-primary)] font-bold rounded transition-colors cursor-pointer flex items-center gap-2"
                                                title="Play every track from every album"
                                            >
                                                <svg className="w-4 h-4" fill="currentColor" viewBox="0 0 24 24"><path d="M8 5v14l11-7z" /></svg>
                                                Play Artist
                                            </button>
                                            <button
                                                type="button"
                                                onClick={() => playArtist(true)}
                                                className="text-sm px-4 py-2 bg-[var(--vora-bg-sunken)] hover:bg-[var(--vora-bg-surface)] text-[var(--vora-text-primary)] hover:text-[var(--vora-text-primary)] rounded transition-colors cursor-pointer flex items-center gap-2 backdrop-blur-sm border border-[var(--vora-border-subtle)]/60"
                                                title="Shuffle every track from every album"
                                            >
                                                <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 4l5 5m0 0V5m0 4H5m11-4l5 5m0 0V5m0 4h-4m-2 7l7 7m-7-7l-7 7m14 0v-4m0 4h-4" /></svg>
                                                Shuffle Artist
                                            </button>
                                            <button
                                                type="button"
                                                onClick={() => startRadioFromSeed({ seedKind: 'Artist', seedArtistId: currentArtist.id })}
                                                className="text-sm px-4 py-2 bg-[var(--vora-bg-sunken)] hover:bg-[var(--vora-bg-surface)] text-[var(--vora-text-primary)] hover:text-[var(--vora-text-primary)] rounded transition-colors cursor-pointer flex items-center gap-2 backdrop-blur-sm border border-[var(--vora-border-subtle)]/60"
                                                title="Start an endless radio station seeded by this artist"
                                            >
                                                <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9.348 14.652a3.75 3.75 0 010-5.304m5.304 0a3.75 3.75 0 010 5.304m-7.425 2.121a6.75 6.75 0 010-9.546m9.546 0a6.75 6.75 0 010 9.546M5.106 18.894c-3.808-3.807-3.808-9.98 0-13.788m13.788 0c3.808 3.807 3.808 9.98 0 13.788M12 12h.01" /></svg>
                                                Start Radio
                                            </button>
                                        </>
                                    )}
                                    {isServerAdmin && (
                                        <button
                                            type="button"
                                            onClick={() => setEditing({ kind: 'artist', artist: currentArtist })}
                                            className="text-xs px-3 py-1.5 bg-[var(--vora-bg-sunken)] hover:bg-[var(--vora-bg-surface)] text-[var(--vora-text-primary)] hover:text-[var(--vora-text-primary)] rounded transition-colors cursor-pointer flex items-center gap-1 backdrop-blur-sm border border-[var(--vora-border-subtle)]/60"
                                        >
                                            <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15.232 5.232l3.536 3.536m-2.036-5.036a2.5 2.5 0 113.536 3.536L6.5 21.036H3v-3.572L16.732 3.732z" /></svg>
                                            Edit Artist
                                        </button>
                                    )}
                                </div>
                            </div>
                        </div>
                    </div>
                );
            })()}

            {nav.view === 'artist' && (
                isLoading ? (
                    <div className="text-[var(--vora-text-muted)] py-12 text-center">Loading albums...</div>
                ) : albums.length === 0 ? (
                    <div className="text-[var(--vora-text-muted)] py-12 text-center">No albums for this artist.</div>
                ) : (
                    <>
                    <div className="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 gap-4">
                        {albums.map(album => (
                            <div
                                key={album.id}
                                onClick={() => updateNav({ view: 'album', artistId: nav.artistId, albumId: album.id })}
                                className="relative flex flex-col bg-[var(--vora-bg-sunken)] hover:bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] hover:border-[var(--vora-accent-500)] rounded-lg p-3 transition-all cursor-pointer text-left"
                            >
                                {isServerAdmin && (
                                    <button
                                        type="button"
                                        onClick={(e) => { e.stopPropagation(); setEditing({ kind: 'album', album }); }}
                                        className="absolute top-2 right-2 p-1.5 rounded bg-[var(--vora-bg-canvas)]/80 text-[var(--vora-text-muted)] hover:text-[var(--vora-accent-text)] transition-colors cursor-pointer z-10"
                                        title="Edit album"
                                    >
                                        <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15.232 5.232l3.536 3.536m-2.036-5.036a2.5 2.5 0 113.536 3.536L6.5 21.036H3v-3.572L16.732 3.732z" /></svg>
                                    </button>
                                )}
                                <div className="aspect-square rounded bg-[var(--vora-bg-canvas)] border border-[var(--vora-border-subtle)] flex items-center justify-center overflow-hidden mb-3">
                                    {album.artworkUrl
                                        ? <img src={album.artworkUrl} alt={album.title} className="max-w-full max-h-full object-cover" />
                                        : <svg className="w-10 h-10 text-[var(--vora-text-disabled)]" fill="currentColor" viewBox="0 0 24 24"><path d="M12 3v10.55c-.59-.34-1.27-.55-2-.55-2.21 0-4 1.79-4 4s1.79 4 4 4 4-1.79 4-4V7h4V3h-6z" /></svg>}
                                </div>
                                <div className="font-bold text-sm text-[var(--vora-text-primary)] truncate" title={album.title}>{album.title}</div>
                                <div className="text-xs text-[var(--vora-text-muted)]">{album.year || ''}</div>
                            </div>
                        ))}
                    </div>
                    {similarArtists.length > 0 && (
                        <div className="mt-10">
                            <h3 className="text-lg font-bold text-[var(--vora-text-primary)] mb-3">Fans Also Listen To</h3>
                            <div className="flex gap-4 overflow-x-auto pb-2 -mx-2 px-2">
                                {similarArtists.map(a => (
                                    <button
                                        key={a.id}
                                        type="button"
                                        onClick={() => updateNav({ view: 'artist', artistId: a.id })}
                                        className="w-28 sm:w-32 shrink-0 group text-left cursor-pointer"
                                        title={a.name}
                                    >
                                        <div className="w-full aspect-square rounded-full bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] group-hover:border-[var(--vora-accent-500)] transition-all overflow-hidden mb-2">
                                            {a.artworkUrl
                                                ? <img src={a.artworkUrl} alt="" className="w-full h-full object-cover" />
                                                : <div className="w-full h-full flex items-center justify-center text-[var(--vora-text-disabled)]"><svg className="w-10 h-10" fill="currentColor" viewBox="0 0 24 24"><path d="M12 12c2.21 0 4-1.79 4-4s-1.79-4-4-4-4 1.79-4 4 1.79 4 4 4zm0 2c-2.67 0-8 1.34-8 4v2h16v-2c0-2.66-5.33-4-8-4z" /></svg></div>}
                                        </div>
                                        <div className="text-sm font-bold text-[var(--vora-text-primary)] truncate text-center" title={a.name}>{a.name}</div>
                                    </button>
                                ))}
                            </div>
                        </div>
                    )}
                    </>
                )
            )}

            {nav.view === 'album' && (
                isLoading ? (
                    <div className="text-[var(--vora-text-muted)] py-12 text-center">Loading tracks...</div>
                ) : !currentAlbum ? (
                    <div className="text-[var(--vora-text-muted)] py-12 text-center">Album not found.</div>
                ) : (
                    <>
                        {(() => {
                            const displayArtist = currentAlbum.albumArtist || currentAlbum.artistName;
                            const discMap = new Map<number, typeof tracks>();
                            tracks.forEach(t => {
                                const d = t.discNumber ?? 1;
                                if (!discMap.has(d)) discMap.set(d, []);
                                discMap.get(d)!.push(t);
                            });
                            const discKeys = Array.from(discMap.keys()).sort((a, b) => a - b);
                            const hasMultipleDiscs = discKeys.length > 1;
                            const showTrackArtist = currentAlbum.isCompilation;
                            return (
                                <>
                                    <div className="flex flex-col sm:flex-row items-center sm:items-start gap-4 sm:gap-6 mb-8 pb-6 border-b border-[var(--vora-border-subtle)] text-center sm:text-left">
                                        <div className="relative shrink-0" style={{ width: currentAlbum.discArtUrl ? '14rem' : '10rem', height: '10rem' }}>
                                            {currentAlbum.discArtUrl && (
                                                <div className="absolute top-0 right-0 w-40 h-40 rounded-full bg-black border border-[var(--vora-border-subtle)] overflow-hidden shadow-lg hidden sm:block" style={{ transform: 'translateX(45%)' }}>
                                                    <img src={currentAlbum.discArtUrl} alt="" className="w-full h-full object-cover" />
                                                </div>
                                            )}
                                            <div className="relative w-40 h-40 rounded bg-[var(--vora-bg-canvas)] border border-[var(--vora-border-subtle)] flex items-center justify-center overflow-hidden shadow-lg z-10 mx-auto">
                                                {currentAlbum.artworkUrl
                                                    ? <img src={currentAlbum.artworkUrl} alt={currentAlbum.title} className="max-w-full max-h-full object-cover" />
                                                    : <svg className="w-16 h-16 text-[var(--vora-text-disabled)]" fill="currentColor" viewBox="0 0 24 24"><path d="M12 3v10.55c-.59-.34-1.27-.55-2-.55-2.21 0-4 1.79-4 4s1.79 4 4 4 4-1.79 4-4V7h4V3h-6z" /></svg>}
                                            </div>
                                        </div>
                                        <div className="flex-1 min-w-0 w-full">
                                            <div className="flex flex-col sm:flex-row sm:items-start sm:justify-between gap-3">
                                                <div className="min-w-0">
                                                    {currentAlbum.isCompilation && (
                                                        <div className="text-xs uppercase tracking-widest text-[var(--vora-accent-text)] font-bold mb-1">Compilation</div>
                                                    )}
                                                    <h2 className="text-2xl sm:text-3xl font-bold text-[var(--vora-text-primary)] truncate">{currentAlbum.title}</h2>
                                                    <p className="text-base sm:text-lg text-[var(--vora-text-secondary)] truncate">{displayArtist}</p>
                                                    <p className="text-xs sm:text-sm text-[var(--vora-text-muted)] mt-1">{currentAlbum.year || ''}{currentAlbum.genre ? ` • ${currentAlbum.genre}` : ''}{tracks.length > 0 ? ` • ${tracks.length} tracks` : ''}</p>
                                                </div>
                                                <div className="flex flex-wrap items-center gap-2 sm:justify-end shrink-0">
                                                    {tracks.length > 0 && (
                                                        <button
                                                            type="button"
                                                            onClick={playWholeAlbum}
                                                            className="text-sm px-4 py-2 bg-[var(--vora-accent-500)] hover:bg-[var(--vora-accent-500)] text-[var(--vora-text-primary)] font-bold rounded transition-colors cursor-pointer flex items-center gap-2"
                                                            title="Play this album from the beginning"
                                                        >
                                                            <svg className="w-4 h-4" fill="currentColor" viewBox="0 0 24 24"><path d="M8 5v14l11-7z" /></svg>
                                                            Play Album
                                                        </button>
                                                    )}
                                                    {currentAlbum.artistId && (
                                                        <button
                                                            type="button"
                                                            onClick={() => startRadioFromSeed({ seedKind: 'Artist', seedArtistId: currentAlbum.artistId })}
                                                            className="text-xs px-3 py-1.5 bg-[var(--vora-bg-surface)] hover:bg-[var(--vora-bg-raised)] text-[var(--vora-text-secondary)] hover:text-[var(--vora-text-primary)] rounded transition-colors cursor-pointer flex items-center gap-1"
                                                            title="Start an endless radio station based on this artist"
                                                        >
                                                            <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9.348 14.652a3.75 3.75 0 010-5.304m5.304 0a3.75 3.75 0 010 5.304m-7.425 2.121a6.75 6.75 0 010-9.546m9.546 0a6.75 6.75 0 010 9.546M5.106 18.894c-3.808-3.807-3.808-9.98 0-13.788m13.788 0c3.808 3.807 3.808 9.98 0 13.788M12 12h.01" /></svg>
                                                            Radio
                                                        </button>
                                                    )}
                                                    {isServerAdmin && (
                                                        <button
                                                            type="button"
                                                            onClick={() => setEditing({ kind: 'album', album: currentAlbum })}
                                                            className="text-xs px-3 py-1.5 bg-[var(--vora-bg-surface)] hover:bg-[var(--vora-bg-raised)] text-[var(--vora-text-secondary)] hover:text-[var(--vora-text-primary)] rounded transition-colors cursor-pointer flex items-center gap-1"
                                                            title="Edit album metadata"
                                                        >
                                                            <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15.232 5.232l3.536 3.536m-2.036-5.036a2.5 2.5 0 113.536 3.536L6.5 21.036H3v-3.572L16.732 3.732z" /></svg>
                                                            Edit
                                                        </button>
                                                    )}
                                                </div>
                                            </div>
                                        </div>
                                    </div>

                                    {discKeys.map(discNum => {
                                        const tracksInDisc = discMap.get(discNum)!;
                                        return (
                                            <div key={discNum} className="mb-4">
                                                {hasMultipleDiscs && (
                                                    <div className="flex items-center gap-2 mb-2 mt-4 first:mt-0">
                                                        <svg className="w-4 h-4 text-[var(--vora-text-muted)]" fill="currentColor" viewBox="0 0 24 24"><path d="M12 2a10 10 0 100 20 10 10 0 000-20zm0 14a4 4 0 110-8 4 4 0 010 8z" /></svg>
                                                        <span className="text-xs uppercase tracking-widest text-[var(--vora-text-muted)] font-bold">Disc {discNum}</span>
                                                        <div className="flex-1 h-px bg-[var(--vora-bg-surface)]" />
                                                    </div>
                                                )}
                                                <div className="space-y-1">
                                                    {tracksInDisc.map(track => {
                                                        const idx = tracks.indexOf(track);
                                                        return (
                                                            <div
                                                                key={track.id}
                                                                onClick={() => playFromIndex(idx)}
                                                                onContextMenu={(e) => {
                                                                    e.preventDefault();
                                                                    setTrackContextMenu({ x: e.pageX, y: e.pageY, track, index: idx });
                                                                }}
                                                                className="w-full text-left flex items-center gap-2 sm:gap-4 p-2 hover:bg-[var(--vora-bg-sunken)] border border-transparent hover:border-[var(--vora-border-subtle)] rounded transition-all cursor-pointer group"
                                                            >
                                                                <div className="w-6 sm:w-8 text-right text-sm text-[var(--vora-text-muted)] group-hover:text-[var(--vora-accent-text)] shrink-0">
                                                                    <span className="group-hover:hidden">{track.trackNumber || '—'}</span>
                                                                    <svg className="w-5 h-5 hidden group-hover:inline-block text-[var(--vora-accent-text)]" fill="currentColor" viewBox="0 0 24 24"><path d="M8 5v14l11-7z" /></svg>
                                                                </div>
                                                                <div className="flex-1 min-w-0">
                                                                    <div className="text-sm text-[var(--vora-text-primary)] group-hover:text-[var(--vora-text-primary)] truncate flex items-center gap-2">
                                                                        <span className="truncate">{track.title}</span>
                                                                        {track.contentRating && (
                                                                            <span className="text-[10px] sm:text-xs font-bold uppercase tracking-wide px-1.5 py-0.5 rounded bg-[var(--vora-bg-surface)] text-[var(--vora-text-secondary)] shrink-0">{track.contentRating}</span>
                                                                        )}
                                                                    </div>
                                                                    {showTrackArtist && track.artist && (
                                                                        <div className="text-xs text-[var(--vora-text-muted)] truncate">{track.artist}</div>
                                                                    )}
                                                                </div>
                                                                <button
                                                                    type="button"
                                                                    onClick={(e) => { e.stopPropagation(); toggleTrackLike(track.id, track.isLiked); }}
                                                                    className={`p-1.5 rounded transition-colors cursor-pointer shrink-0 ${track.isLiked ? 'text-[var(--vora-accent-text)] hover:text-[var(--vora-accent-text)] opacity-100' : 'text-[var(--vora-text-disabled)] hover:text-[var(--vora-accent-text)] sm:opacity-0 sm:group-hover:opacity-100'}`}
                                                                    title={track.isLiked ? 'Remove from Liked Songs' : 'Add to Liked Songs'}
                                                                >
                                                                    {track.isLiked
                                                                        ? <svg className="w-4 h-4" fill="currentColor" viewBox="0 0 24 24"><path d="M12 21.35l-1.45-1.32C5.4 15.36 2 12.28 2 8.5 2 5.42 4.42 3 7.5 3c1.74 0 3.41.81 4.5 2.09C13.09 3.81 14.76 3 16.5 3 19.58 3 22 5.42 22 8.5c0 3.78-3.4 6.86-8.55 11.54L12 21.35z" /></svg>
                                                                        : <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4.318 6.318a4.5 4.5 0 000 6.364L12 20.364l7.682-7.682a4.5 4.5 0 00-6.364-6.364L12 7.636l-1.318-1.318a4.5 4.5 0 00-6.364 0z" /></svg>}
                                                                </button>
                                                                <div className="text-xs text-[var(--vora-text-muted)] shrink-0 tabular-nums">{formatDuration(track.durationSeconds)}</div>
                                                                {isServerAdmin && (
                                                                    <button
                                                                        type="button"
                                                                        onClick={(e) => { e.stopPropagation(); setEditing({ kind: 'track', track }); }}
                                                                        className="p-1.5 rounded text-[var(--vora-text-disabled)] hover:text-[var(--vora-accent-text)] transition-colors cursor-pointer shrink-0 hidden sm:inline-flex"
                                                                        title="Edit track"
                                                                    >
                                                                        <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15.232 5.232l3.536 3.536m-2.036-5.036a2.5 2.5 0 113.536 3.536L6.5 21.036H3v-3.572L16.732 3.732z" /></svg>
                                                                    </button>
                                                                )}
                                                            </div>
                                                        );
                                                    })}
                                                </div>
                                            </div>
                                        );
                                    })}
                                </>
                            );
                        })()}
                    </>
                )
            )}

            {nav.view === 'likes' && (
                <>
                    <div className="flex flex-col sm:flex-row items-center sm:items-end gap-4 sm:gap-6 mb-8 pb-6 border-b border-[var(--vora-border-subtle)] text-center sm:text-left">
                        <div className="w-32 h-32 sm:w-40 sm:h-40 rounded bg-gradient-to-br from-orange-600 to-purple-900 border border-orange-400/30 flex items-center justify-center shrink-0 shadow-lg">
                            <svg className="w-16 h-16 sm:w-20 sm:h-20 text-[var(--vora-text-primary)]" fill="currentColor" viewBox="0 0 24 24"><path d="M12 21.35l-1.45-1.32C5.4 15.36 2 12.28 2 8.5 2 5.42 4.42 3 7.5 3c1.74 0 3.41.81 4.5 2.09C13.09 3.81 14.76 3 16.5 3 19.58 3 22 5.42 22 8.5c0 3.78-3.4 6.86-8.55 11.54L12 21.35z" /></svg>
                        </div>
                        <div className="flex-1 min-w-0">
                            <div className="text-xs uppercase tracking-widest text-[var(--vora-text-secondary)] font-bold mb-1">Playlist</div>
                            <h2 className="text-3xl sm:text-4xl font-bold text-[var(--vora-text-primary)]">Liked Songs</h2>
                            <p className="text-sm text-[var(--vora-text-secondary)] mt-2">{likedCount} {likedCount === 1 ? 'track' : 'tracks'}</p>
                        </div>
                        {likedTracks.length > 0 && (
                            <div className="flex flex-wrap items-center gap-2 justify-center sm:justify-end">
                                <button
                                    type="button"
                                    onClick={() => {
                                        const server = serverId ? serverVault.getServer(serverId) : serverVault.getActiveServer();
                                        const baseUrl = server?.url || '';
                                        const items = likedTracks.map(t => ({
                                            id: t.id,
                                            title: t.title,
                                            subtitle: t.albumTitle ?? 'Liked Songs',
                                            posterUrl: t.albumArtworkUrl,
                                            streamUrl: musicService.getTrackStreamUrl(t.id, baseUrl, audioQualityStore.get()),
                                            serverId: server?.id,
                                            container: 'audio' as const,
                                            playbackContextType: 'Music' as const
                                        }));
                                        if (isShuffled) toggleShuffle();
                                        playQueue(items, 0);
                                    }}
                                    className="text-sm px-4 py-2 bg-[var(--vora-accent-500)] hover:bg-[var(--vora-accent-500)] text-[var(--vora-text-primary)] font-bold rounded transition-colors cursor-pointer flex items-center gap-2"
                                >
                                    <svg className="w-4 h-4" fill="currentColor" viewBox="0 0 24 24"><path d="M8 5v14l11-7z" /></svg>
                                    Play
                                </button>
                                <button
                                    type="button"
                                    onClick={() => {
                                        const server = serverId ? serverVault.getServer(serverId) : serverVault.getActiveServer();
                                        const baseUrl = server?.url || '';
                                        const items = likedTracks.map(t => ({
                                            id: t.id,
                                            title: t.title,
                                            subtitle: t.albumTitle ?? 'Liked Songs',
                                            posterUrl: t.albumArtworkUrl,
                                            streamUrl: musicService.getTrackStreamUrl(t.id, baseUrl, audioQualityStore.get()),
                                            serverId: server?.id,
                                            container: 'audio' as const,
                                            playbackContextType: 'Music' as const
                                        }));
                                        if (!isShuffled) toggleShuffle();
                                        playQueue(items, 0);
                                    }}
                                    className="text-sm px-4 py-2 bg-[var(--vora-bg-surface)] hover:bg-[var(--vora-bg-raised)] text-[var(--vora-text-primary)] hover:text-[var(--vora-text-primary)] rounded transition-colors cursor-pointer flex items-center gap-2"
                                >
                                    <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 4l5 5m0 0V5m0 4H5m11-4l5 5m0 0V5m0 4h-4m-2 7l7 7m-7-7l-7 7m14 0v-4m0 4h-4" /></svg>
                                    Shuffle
                                </button>
                            </div>
                        )}
                    </div>

                    {likedTracks.length === 0 ? (
                        <div className="text-[var(--vora-text-muted)] py-12 text-center bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] rounded-lg">
                            <p className="mb-2">No liked songs yet.</p>
                            <p className="text-xs">Tap the heart on a track to add it here.</p>
                        </div>
                    ) : (
                        <div className="space-y-1">
                            {likedTracks.map((t, idx) => (
                                <div
                                    key={t.id}
                                    onClick={() => {
                                        const server = serverId ? serverVault.getServer(serverId) : serverVault.getActiveServer();
                                        const baseUrl = server?.url || '';
                                        const items = likedTracks.map(lt => ({
                                            id: lt.id,
                                            title: lt.title,
                                            subtitle: lt.albumTitle ?? 'Liked Songs',
                                            posterUrl: lt.albumArtworkUrl,
                                            streamUrl: musicService.getTrackStreamUrl(lt.id, baseUrl, audioQualityStore.get()),
                                            serverId: server?.id,
                                            container: 'audio' as const,
                                            playbackContextType: 'Music' as const
                                        }));
                                        playQueue(items, idx);
                                    }}
                                    className="w-full text-left flex items-center gap-3 p-2 hover:bg-[var(--vora-bg-sunken)] border border-transparent hover:border-[var(--vora-border-subtle)] rounded transition-all cursor-pointer group"
                                >
                                    <div className="w-10 h-10 rounded bg-[var(--vora-bg-canvas)] border border-[var(--vora-border-subtle)] flex items-center justify-center overflow-hidden shrink-0">
                                        {t.albumArtworkUrl
                                            ? <img src={t.albumArtworkUrl} alt="" className="w-full h-full object-cover" />
                                            : <svg className="w-5 h-5 text-[var(--vora-text-disabled)]" fill="currentColor" viewBox="0 0 24 24"><path d="M12 3v10.55c-.59-.34-1.27-.55-2-.55-2.21 0-4 1.79-4 4s1.79 4 4 4 4-1.79 4-4V7h4V3h-6z" /></svg>}
                                    </div>
                                    <div className="flex-1 min-w-0">
                                        <div className="text-sm text-[var(--vora-text-primary)] group-hover:text-[var(--vora-text-primary)] truncate">{t.title}</div>
                                        <div className="text-xs text-[var(--vora-text-muted)] truncate">{t.albumTitle ?? ''}</div>
                                    </div>
                                    <button
                                        type="button"
                                        onClick={async (e) => {
                                            e.stopPropagation();
                                            try {
                                                await musicService.unlikeTrack(t.id, serverId);
                                                setLikedTracks(prev => prev.filter(x => x.id !== t.id));
                                                setLikedCount(c => Math.max(0, c - 1));
                                                window.dispatchEvent(new CustomEvent('music-likes-changed'));
                                            } catch (err) {
                                                console.error('Unlike failed', err);
                                            }
                                        }}
                                        className="p-1.5 rounded text-[var(--vora-accent-text)] hover:text-[var(--vora-accent-text)] transition-colors cursor-pointer"
                                        title="Remove from Liked Songs"
                                    >
                                        <svg className="w-4 h-4" fill="currentColor" viewBox="0 0 24 24"><path d="M12 21.35l-1.45-1.32C5.4 15.36 2 12.28 2 8.5 2 5.42 4.42 3 7.5 3c1.74 0 3.41.81 4.5 2.09C13.09 3.81 14.76 3 16.5 3 19.58 3 22 5.42 22 8.5c0 3.78-3.4 6.86-8.55 11.54L12 21.35z" /></svg>
                                    </button>
                                    <div className="text-xs text-[var(--vora-text-muted)] w-12 text-right">{formatDuration(t.durationSeconds)}</div>
                                </div>
                            ))}
                        </div>
                    )}
                </>
            )}

            {nav.view === 'top' && (
                <>
                    <div className="flex flex-col sm:flex-row items-center sm:items-end gap-4 sm:gap-6 mb-8 pb-6 border-b border-[var(--vora-border-subtle)] text-center sm:text-left">
                        <div className="w-32 h-32 sm:w-40 sm:h-40 rounded bg-gradient-to-br from-indigo-600 to-cyan-900 border border-cyan-400/30 flex items-center justify-center shrink-0 shadow-lg">
                            <svg className="w-16 h-16 sm:w-20 sm:h-20 text-[var(--vora-text-primary)]" fill="currentColor" viewBox="0 0 24 24"><path d="M12 1l3 6h6l-5 4 2 7-6-4-6 4 2-7-5-4h6z" /></svg>
                        </div>
                        <div className="flex-1 min-w-0">
                            <div className="text-xs uppercase tracking-widest text-[var(--vora-text-secondary)] font-bold mb-1">Mix</div>
                            <h2 className="text-3xl sm:text-4xl font-bold text-[var(--vora-text-primary)]">Your Top Tracks</h2>
                            <p className="text-sm text-[var(--vora-text-secondary)] mt-2">{topTracks.length} {topTracks.length === 1 ? 'track' : 'tracks'} based on your play history</p>
                        </div>
                        {topTracks.length > 0 && (
                            <div className="flex flex-wrap items-center gap-2 justify-center sm:justify-end">
                                <button
                                    type="button"
                                    onClick={() => {
                                        if (isShuffled) toggleShuffle();
                                        playArtistTrackList(topTracks, 0);
                                    }}
                                    className="text-sm px-4 py-2 bg-[var(--vora-accent-500)] hover:bg-[var(--vora-accent-500)] text-[var(--vora-text-primary)] font-bold rounded transition-colors cursor-pointer flex items-center gap-2"
                                >
                                    <svg className="w-4 h-4" fill="currentColor" viewBox="0 0 24 24"><path d="M8 5v14l11-7z" /></svg>
                                    Play
                                </button>
                                <button
                                    type="button"
                                    onClick={() => {
                                        if (!isShuffled) toggleShuffle();
                                        playArtistTrackList(topTracks, 0);
                                    }}
                                    className="text-sm px-4 py-2 bg-[var(--vora-bg-surface)] hover:bg-[var(--vora-bg-raised)] text-[var(--vora-text-primary)] hover:text-[var(--vora-text-primary)] rounded transition-colors cursor-pointer flex items-center gap-2"
                                >
                                    <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 4l5 5m0 0V5m0 4H5m11-4l5 5m0 0V5m0 4h-4m-2 7l7 7m-7-7l-7 7m14 0v-4m0 4h-4" /></svg>
                                    Shuffle
                                </button>
                            </div>
                        )}
                    </div>

                    {topTracks.length === 0 ? (
                        <div className="text-[var(--vora-text-muted)] py-12 text-center bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] rounded-lg">
                            <p className="mb-2">No play history yet.</p>
                            <p className="text-xs">Play some music — tracks you listen to past 30 seconds will show up here.</p>
                        </div>
                    ) : (
                        <div className="space-y-1">
                            {topTracks.map((t, idx) => (
                                <div
                                    key={t.id}
                                    onClick={() => playArtistTrackList(topTracks, idx)}
                                    className="w-full text-left flex items-center gap-3 p-2 hover:bg-[var(--vora-bg-sunken)] border border-transparent hover:border-[var(--vora-border-subtle)] rounded transition-all cursor-pointer group"
                                >
                                    <div className="w-8 text-right text-sm text-[var(--vora-text-muted)] group-hover:text-[var(--vora-accent-text)] tabular-nums">{idx + 1}</div>
                                    <div className="w-10 h-10 rounded bg-[var(--vora-bg-canvas)] border border-[var(--vora-border-subtle)] flex items-center justify-center overflow-hidden shrink-0">
                                        {t.albumArtworkUrl
                                            ? <img src={t.albumArtworkUrl} alt="" className="w-full h-full object-cover" />
                                            : <svg className="w-5 h-5 text-[var(--vora-text-disabled)]" fill="currentColor" viewBox="0 0 24 24"><path d="M12 3v10.55c-.59-.34-1.27-.55-2-.55-2.21 0-4 1.79-4 4s1.79 4 4 4 4-1.79 4-4V7h4V3h-6z" /></svg>}
                                    </div>
                                    <div className="flex-1 min-w-0">
                                        <div className="text-sm text-[var(--vora-text-primary)] group-hover:text-[var(--vora-text-primary)] truncate">{t.title}</div>
                                        <div className="text-xs text-[var(--vora-text-muted)] truncate">{t.albumTitle ?? ''}</div>
                                    </div>
                                    <div className="text-xs text-[var(--vora-text-muted)] w-12 text-right">{formatDuration(t.durationSeconds)}</div>
                                </div>
                            ))}
                        </div>
                    )}
                </>
            )}

            {nav.view === 'mix' && (
                isLoading ? (
                    <div className="text-[var(--vora-text-muted)] py-12 text-center">Loading mix...</div>
                ) : !currentMix ? (
                    <div className="text-[var(--vora-text-muted)] py-12 text-center">Mix not found.</div>
                ) : (
                    <>
                        <div className="flex flex-col sm:flex-row items-center sm:items-end gap-4 sm:gap-6 mb-8 pb-6 border-b border-[var(--vora-border-subtle)] text-center sm:text-left">
                            <div className="w-32 h-32 sm:w-40 sm:h-40 rounded bg-gradient-to-br from-orange-700 via-purple-900 to-indigo-900 border border-orange-400/30 flex items-center justify-center shrink-0 shadow-lg overflow-hidden relative">
                                {currentMix.artworkUrl
                                    ? <img src={currentMix.artworkUrl} alt="" className="w-full h-full object-cover opacity-60" />
                                    : null}
                                <div className="absolute inset-0 flex flex-col items-center justify-center text-[var(--vora-text-primary)] drop-shadow-lg">
                                    <div className="text-xs uppercase tracking-widest text-orange-300/90 font-bold">Daily Mix {currentMix.slot}</div>
                                    <div className="text-lg sm:text-xl font-bold text-center px-2">{currentMix.descriptionTag ?? 'Mix'}</div>
                                </div>
                            </div>
                            <div className="flex-1 min-w-0">
                                <div className="text-xs uppercase tracking-widest text-[var(--vora-text-secondary)] font-bold mb-1">Made for You</div>
                                <h2 className="text-3xl sm:text-4xl font-bold text-[var(--vora-text-primary)] truncate">{currentMix.name}</h2>
                                <p className="text-sm text-[var(--vora-text-secondary)] mt-2">{currentMix.tracks.length} tracks{currentMix.lastDriftAt ? ` • Updated ${new Date(currentMix.lastDriftAt).toLocaleDateString()}` : ''}</p>
                                {currentMix.tracks.length > 0 && (
                                    <div className="flex flex-wrap items-center gap-2 mt-4 justify-center sm:justify-start">
                                        <button
                                            type="button"
                                            onClick={() => {
                                                if (isShuffled) toggleShuffle();
                                                playMixFromIndex(0);
                                            }}
                                            className="text-sm px-4 py-2 bg-[var(--vora-accent-500)] hover:bg-[var(--vora-accent-500)] text-[var(--vora-text-primary)] font-bold rounded transition-colors cursor-pointer flex items-center gap-2"
                                        >
                                            <svg className="w-4 h-4" fill="currentColor" viewBox="0 0 24 24"><path d="M8 5v14l11-7z" /></svg>
                                            Play
                                        </button>
                                        <button
                                            type="button"
                                            onClick={() => {
                                                if (!isShuffled) toggleShuffle();
                                                playMixFromIndex(0);
                                            }}
                                            className="text-sm px-4 py-2 bg-[var(--vora-bg-surface)] hover:bg-[var(--vora-bg-raised)] text-[var(--vora-text-primary)] hover:text-[var(--vora-text-primary)] rounded transition-colors cursor-pointer flex items-center gap-2"
                                        >
                                            <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 4l5 5m0 0V5m0 4H5m11-4l5 5m0 0V5m0 4h-4m-2 7l7 7m-7-7l-7 7m14 0v-4m0 4h-4" /></svg>
                                            Shuffle
                                        </button>
                                    </div>
                                )}
                            </div>
                        </div>

                        {currentMix.tracks.length === 0 ? (
                            <div className="text-[var(--vora-text-muted)] py-12 text-center bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] rounded-lg">
                                <p className="mb-2">This mix is empty.</p>
                                <p className="text-xs">It will populate once you build more play history.</p>
                            </div>
                        ) : (
                            <div className="space-y-1">
                                {currentMix.tracks.map((t, idx) => (
                                    <div
                                        key={t.id}
                                        onClick={() => playMixFromIndex(idx)}
                                        className="w-full text-left flex items-center gap-3 p-2 hover:bg-[var(--vora-bg-sunken)] border border-transparent hover:border-[var(--vora-border-subtle)] rounded transition-all cursor-pointer group"
                                    >
                                        <div className="w-8 text-right text-sm text-[var(--vora-text-muted)] group-hover:text-[var(--vora-accent-text)] tabular-nums shrink-0">{idx + 1}</div>
                                        <div className="flex-1 min-w-0">
                                            <div className="text-sm text-[var(--vora-text-primary)] group-hover:text-[var(--vora-text-primary)] truncate flex items-center gap-2">
                                                <span className="truncate">{t.title}</span>
                                                {t.contentRating && (
                                                    <span className="text-[10px] sm:text-xs font-bold uppercase tracking-wide px-1.5 py-0.5 rounded bg-[var(--vora-bg-surface)] text-[var(--vora-text-secondary)] shrink-0">{t.contentRating}</span>
                                                )}
                                            </div>
                                            {t.artist && <div className="text-xs text-[var(--vora-text-muted)] truncate">{t.artist}</div>}
                                        </div>
                                        <div className="text-xs text-[var(--vora-text-muted)] shrink-0 tabular-nums">{formatDuration(t.durationSeconds)}</div>
                                    </div>
                                ))}
                            </div>
                        )}
                    </>
                )
            )}

            {nav.view === 'recap' && (
                isLoading ? (
                    <div className="text-[var(--vora-text-muted)] py-12 text-center">Loading your year...</div>
                ) : !currentRecap ? (
                    <div className="text-[var(--vora-text-muted)] py-12 text-center">Couldn't load recap.</div>
                ) : (
                    <>
                        <div className="flex flex-col sm:flex-row items-center sm:items-end gap-4 sm:gap-6 mb-8 pb-6 border-b border-[var(--vora-border-subtle)] text-center sm:text-left">
                            <div className="w-32 h-32 sm:w-40 sm:h-40 rounded bg-gradient-to-br from-pink-600 via-amber-500 to-purple-700 border border-pink-400/30 flex items-center justify-center shrink-0 shadow-lg">
                                <div className="text-5xl sm:text-6xl font-bold text-[var(--vora-text-primary)] drop-shadow-lg">{currentRecap.year}</div>
                            </div>
                            <div className="flex-1 min-w-0">
                                <div className="text-xs uppercase tracking-widest text-[var(--vora-text-secondary)] font-bold mb-1">Year in Music</div>
                                <h2 className="text-3xl sm:text-4xl font-bold text-[var(--vora-text-primary)]">{currentRecap.year} in music</h2>
                                <p className="text-sm text-[var(--vora-text-secondary)] mt-2">
                                    {currentRecap.totalPlays.toLocaleString()} plays · {formatHours(currentRecap.totalListeningSeconds)} of listening
                                </p>
                                {availableYears.length > 1 && (
                                    <div className="mt-3 inline-flex gap-1 flex-wrap">
                                        {availableYears.map(y => (
                                            <button
                                                key={y}
                                                type="button"
                                                onClick={() => updateNav({ view: 'recap', year: y })}
                                                className={`text-xs px-3 py-1 rounded font-bold transition-colors cursor-pointer ${currentRecap.year === y ? 'bg-pink-600 text-[var(--vora-text-primary)]' : 'bg-[var(--vora-bg-surface)] text-[var(--vora-text-secondary)] hover:text-[var(--vora-text-primary)] hover:bg-[var(--vora-bg-raised)]'}`}
                                            >
                                                {y}
                                            </button>
                                        ))}
                                    </div>
                                )}
                            </div>
                        </div>

                        {currentRecap.totalPlays === 0 ? (
                            <div className="text-[var(--vora-text-muted)] py-12 text-center bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] rounded-lg">
                                <p className="mb-2">No plays in {currentRecap.year} yet.</p>
                                <p className="text-xs">Listen to some music and check back.</p>
                            </div>
                        ) : (
                            <>
                                <div className="grid grid-cols-2 sm:grid-cols-4 gap-4 mb-8">
                                    <StatBlock label="Tracks played" value={currentRecap.distinctTrackCount.toLocaleString()} />
                                    <StatBlock label="Artists" value={currentRecap.distinctArtistCount.toLocaleString()} />
                                    <StatBlock label="Albums" value={currentRecap.distinctAlbumCount.toLocaleString()} />
                                    <StatBlock label="Peak day" value={currentRecap.peakDayOfWeekLabel ?? '—'} />
                                </div>

                                {currentRecap.topArtists.length > 0 && (
                                    <div className="mb-8">
                                        <h3 className="text-lg font-bold text-[var(--vora-text-primary)] mb-3">Top Artists</h3>
                                        <div className="flex gap-4 overflow-x-auto pb-2 -mx-2 px-2">
                                            {currentRecap.topArtists.map((a, idx) => (
                                                <button
                                                    key={a.id}
                                                    type="button"
                                                    onClick={() => updateNav({ view: 'artist', artistId: a.id })}
                                                    className="w-28 sm:w-32 shrink-0 group text-left cursor-pointer"
                                                >
                                                    <div className="w-full aspect-square rounded-full bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] group-hover:border-pink-400 transition-all overflow-hidden mb-2 relative">
                                                        {a.artworkUrl
                                                            ? <img src={a.artworkUrl} alt="" className="w-full h-full object-cover" />
                                                            : <div className="w-full h-full flex items-center justify-center text-[var(--vora-text-disabled)]"><svg className="w-10 h-10" fill="currentColor" viewBox="0 0 24 24"><path d="M12 12c2.21 0 4-1.79 4-4s-1.79-4-4-4-4 1.79-4 4 1.79 4 4 4zm0 2c-2.67 0-8 1.34-8 4v2h16v-2c0-2.66-5.33-4-8-4z" /></svg></div>}
                                                        <div className="absolute top-1 left-1 bg-pink-600/90 text-[var(--vora-text-primary)] text-xs font-bold rounded px-1.5 py-0.5">#{idx + 1}</div>
                                                    </div>
                                                    <div className="text-sm font-bold text-[var(--vora-text-primary)] truncate text-center">{a.name}</div>
                                                    <div className="text-xs text-[var(--vora-text-muted)] text-center">{a.playCount} plays</div>
                                                </button>
                                            ))}
                                        </div>
                                    </div>
                                )}

                                {currentRecap.topTracks.length > 0 && (
                                    <div className="mb-8">
                                        <h3 className="text-lg font-bold text-[var(--vora-text-primary)] mb-3">Top Tracks</h3>
                                        <div className="space-y-1">
                                            {currentRecap.topTracks.map((t, idx) => (
                                                <div key={t.id} className="flex items-center gap-3 p-2 bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] rounded">
                                                    <div className="w-8 text-right text-sm text-[var(--vora-text-muted)] tabular-nums shrink-0 font-bold">{idx + 1}</div>
                                                    <div className="w-10 h-10 rounded bg-[var(--vora-bg-canvas)] border border-[var(--vora-border-subtle)] overflow-hidden shrink-0">
                                                        {t.albumArtworkUrl
                                                            ? <img src={t.albumArtworkUrl} alt="" className="w-full h-full object-cover" />
                                                            : <div className="w-full h-full flex items-center justify-center text-[var(--vora-text-disabled)]"><svg className="w-5 h-5" fill="currentColor" viewBox="0 0 24 24"><path d="M12 3v10.55c-.59-.34-1.27-.55-2-.55-2.21 0-4 1.79-4 4s1.79 4 4 4 4-1.79 4-4V7h4V3h-6z" /></svg></div>}
                                                    </div>
                                                    <div className="flex-1 min-w-0">
                                                        <div className="text-sm text-[var(--vora-text-primary)] truncate">{t.title}</div>
                                                        <div className="text-xs text-[var(--vora-text-muted)] truncate">{t.artist ?? t.albumTitle ?? ''}</div>
                                                    </div>
                                                    <div className="text-xs text-pink-400 font-bold shrink-0">{t.playCount} plays</div>
                                                </div>
                                            ))}
                                        </div>
                                    </div>
                                )}

                                {currentRecap.topGenres.length > 0 && (
                                    <div className="mb-8">
                                        <h3 className="text-lg font-bold text-[var(--vora-text-primary)] mb-3">Top Genres</h3>
                                        <div className="space-y-2 bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] rounded-lg p-4">
                                            {currentRecap.topGenres.map(g => (
                                                <div key={g.name}>
                                                    <div className="flex justify-between text-sm mb-1">
                                                        <span className="text-[var(--vora-text-primary)]">{g.name}</span>
                                                        <span className="text-[var(--vora-text-muted)]">{g.percent}% · {g.playCount} plays</span>
                                                    </div>
                                                    <div className="h-2 bg-[var(--vora-bg-surface)] rounded-full overflow-hidden">
                                                        <div className="h-full bg-gradient-to-r from-pink-500 to-amber-500" style={{ width: `${g.percent}%` }} />
                                                    </div>
                                                </div>
                                            ))}
                                        </div>
                                    </div>
                                )}

                                {currentRecap.newDiscoveries.length > 0 && (
                                    <div className="mb-8">
                                        <h3 className="text-lg font-bold text-[var(--vora-text-primary)] mb-3">New Discoveries</h3>
                                        <p className="text-xs text-[var(--vora-text-muted)] mb-3">Artists you played for the first time in {currentRecap.year}</p>
                                        <div className="flex gap-4 overflow-x-auto pb-2 -mx-2 px-2">
                                            {currentRecap.newDiscoveries.map(a => (
                                                <button
                                                    key={a.id}
                                                    type="button"
                                                    onClick={() => updateNav({ view: 'artist', artistId: a.id })}
                                                    className="w-24 sm:w-28 shrink-0 group text-left cursor-pointer"
                                                >
                                                    <div className="w-full aspect-square rounded-full bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] group-hover:border-orange-400 transition-all overflow-hidden mb-2">
                                                        {a.artworkUrl
                                                            ? <img src={a.artworkUrl} alt="" className="w-full h-full object-cover" />
                                                            : <div className="w-full h-full flex items-center justify-center text-[var(--vora-text-disabled)]"><svg className="w-8 h-8" fill="currentColor" viewBox="0 0 24 24"><path d="M12 12c2.21 0 4-1.79 4-4s-1.79-4-4-4-4 1.79-4 4 1.79 4 4 4zm0 2c-2.67 0-8 1.34-8 4v2h16v-2c0-2.66-5.33-4-8-4z" /></svg></div>}
                                                    </div>
                                                    <div className="text-xs font-bold text-[var(--vora-text-primary)] truncate text-center">{a.name}</div>
                                                </button>
                                            ))}
                                        </div>
                                    </div>
                                )}

                                <div className="grid grid-cols-1 lg:grid-cols-2 gap-4 mb-8">
                                    <DistributionBlock title="Plays by day of week" labels={['Sun','Mon','Tue','Wed','Thu','Fri','Sat']} values={currentRecap.playsByDayOfWeek} />
                                    <DistributionBlock title="Plays by hour" labels={Array.from({length:24}, (_,i) => i.toString())} values={currentRecap.playsByHour} compact />
                                </div>
                                {currentRecap.peakHourLabel && (
                                    <div className="text-sm text-[var(--vora-text-muted)] mb-4">Most active around <span className="text-pink-400 font-bold">{currentRecap.peakHourLabel}</span></div>
                                )}
                            </>
                        )}
                    </>
                )
            )}

            {nav.view === 'genres' && (
                isLoading ? (
                    <div className="text-[var(--vora-text-muted)] py-12 text-center">Loading genres...</div>
                ) : genres.length === 0 ? (
                    <div className="text-[var(--vora-text-muted)] py-12 text-center bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] rounded-lg">
                        <p className="mb-2">No genres in your library yet.</p>
                        <p className="text-xs">Genres come from album metadata — try a library scan if you've added new music.</p>
                    </div>
                ) : (
                    <>
                        <h2 className="text-lg font-bold text-[var(--vora-text-primary)] mb-4">Browse by Genre</h2>
                        <div className="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 gap-4">
                            {genres.map(g => (
                                <button
                                    key={g.name}
                                    type="button"
                                    onClick={() => updateNav({ view: 'genre', genre: g.name })}
                                    className="relative aspect-square rounded-lg overflow-hidden border border-[var(--vora-border-subtle)] hover:border-emerald-400 transition-all cursor-pointer group bg-gradient-to-br from-emerald-700 via-teal-900 to-indigo-900"
                                    title={g.name}
                                >
                                    {g.sampleArtworkUrl && (
                                        <img src={g.sampleArtworkUrl} alt="" className="absolute inset-0 w-full h-full object-cover opacity-30 group-hover:opacity-40 transition-opacity" />
                                    )}
                                    <div className="absolute inset-0 flex flex-col items-center justify-center p-3 text-center">
                                        <div className="text-xl sm:text-2xl font-bold text-[var(--vora-text-primary)] drop-shadow-md">{g.name}</div>
                                        <div className="text-xs text-emerald-200/90 mt-1">{g.trackCount} tracks · {g.artistCount} {g.artistCount === 1 ? 'artist' : 'artists'}</div>
                                    </div>
                                </button>
                            ))}
                        </div>
                    </>
                )
            )}

            {nav.view === 'genre' && (
                isLoading ? (
                    <div className="text-[var(--vora-text-muted)] py-12 text-center">Loading genre...</div>
                ) : !currentGenre ? (
                    <div className="text-[var(--vora-text-muted)] py-12 text-center">Genre not found.</div>
                ) : (
                    <>
                        <div className="flex flex-col sm:flex-row items-center sm:items-end gap-4 sm:gap-6 mb-8 pb-6 border-b border-[var(--vora-border-subtle)] text-center sm:text-left">
                            <div className="w-32 h-32 sm:w-40 sm:h-40 rounded bg-gradient-to-br from-emerald-600 via-teal-700 to-indigo-800 border border-emerald-400/30 flex items-center justify-center shrink-0 shadow-lg">
                                <svg className="w-16 h-16 sm:w-20 sm:h-20 text-[var(--vora-text-primary)]" fill="none" stroke="currentColor" strokeWidth={1.5} viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" d="M3.75 6A2.25 2.25 0 016 3.75h2.25A2.25 2.25 0 0110.5 6v2.25a2.25 2.25 0 01-2.25 2.25H6a2.25 2.25 0 01-2.25-2.25V6zM3.75 15.75A2.25 2.25 0 016 13.5h2.25a2.25 2.25 0 012.25 2.25V18a2.25 2.25 0 01-2.25 2.25H6A2.25 2.25 0 013.75 18v-2.25zM13.5 6a2.25 2.25 0 012.25-2.25H18A2.25 2.25 0 0120.25 6v2.25A2.25 2.25 0 0118 10.5h-2.25a2.25 2.25 0 01-2.25-2.25V6zM13.5 15.75a2.25 2.25 0 012.25-2.25H18a2.25 2.25 0 012.25 2.25V18A2.25 2.25 0 0118 20.25h-2.25A2.25 2.25 0 0113.5 18v-2.25z" /></svg>
                            </div>
                            <div className="flex-1 min-w-0">
                                <div className="text-xs uppercase tracking-widest text-[var(--vora-text-secondary)] font-bold mb-1">Genre</div>
                                <h2 className="text-3xl sm:text-4xl font-bold text-[var(--vora-text-primary)]">{currentGenre.name}</h2>
                                <p className="text-sm text-[var(--vora-text-secondary)] mt-2">{currentGenre.artists.length} {currentGenre.artists.length === 1 ? 'artist' : 'artists'} · {currentGenre.albums.length} {currentGenre.albums.length === 1 ? 'album' : 'albums'}</p>
                            </div>
                        </div>

                        {currentGenre.artists.length > 0 && (
                            <div className="mb-8">
                                <h3 className="text-lg font-bold text-[var(--vora-text-primary)] mb-3">Artists</h3>
                                <div className="flex gap-4 overflow-x-auto pb-2 -mx-2 px-2">
                                    {currentGenre.artists.map(a => (
                                        <button
                                            key={a.id}
                                            type="button"
                                            onClick={() => updateNav({ view: 'artist', artistId: a.id })}
                                            className="w-28 sm:w-32 shrink-0 group text-left cursor-pointer"
                                        >
                                            <div className="w-full aspect-square rounded-full bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] group-hover:border-emerald-400 transition-all overflow-hidden mb-2">
                                                {a.artworkUrl
                                                    ? <img src={a.artworkUrl} alt="" className="w-full h-full object-cover" />
                                                    : <div className="w-full h-full flex items-center justify-center text-[var(--vora-text-disabled)]"><svg className="w-10 h-10" fill="currentColor" viewBox="0 0 24 24"><path d="M12 12c2.21 0 4-1.79 4-4s-1.79-4-4-4-4 1.79-4 4 1.79 4 4 4zm0 2c-2.67 0-8 1.34-8 4v2h16v-2c0-2.66-5.33-4-8-4z" /></svg></div>}
                                            </div>
                                            <div className="text-sm font-bold text-[var(--vora-text-primary)] truncate text-center">{a.name}</div>
                                        </button>
                                    ))}
                                </div>
                            </div>
                        )}

                        {currentGenre.albums.length > 0 && (
                            <div className="mb-8">
                                <h3 className="text-lg font-bold text-[var(--vora-text-primary)] mb-3">Albums</h3>
                                <div className="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 gap-4">
                                    {currentGenre.albums.map(album => (
                                        <div
                                            key={album.id}
                                            onClick={() => updateNav({ view: 'album', artistId: album.artistId, albumId: album.id })}
                                            className="relative flex flex-col bg-[var(--vora-bg-sunken)] hover:bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] hover:border-emerald-400 rounded-lg p-3 transition-all cursor-pointer text-left"
                                        >
                                            <div className="aspect-square rounded bg-[var(--vora-bg-canvas)] border border-[var(--vora-border-subtle)] flex items-center justify-center overflow-hidden mb-3">
                                                {album.artworkUrl
                                                    ? <img src={album.artworkUrl} alt={album.title} className="max-w-full max-h-full object-cover" />
                                                    : <svg className="w-10 h-10 text-[var(--vora-text-disabled)]" fill="currentColor" viewBox="0 0 24 24"><path d="M12 3v10.55c-.59-.34-1.27-.55-2-.55-2.21 0-4 1.79-4 4s1.79 4 4 4 4-1.79 4-4V7h4V3h-6z" /></svg>}
                                            </div>
                                            <div className="font-bold text-sm text-[var(--vora-text-primary)] truncate" title={album.title}>{album.title}</div>
                                            <div className="text-xs text-[var(--vora-text-muted)] truncate">{album.artistName}{album.year ? ` · ${album.year}` : ''}</div>
                                        </div>
                                    ))}
                                </div>
                            </div>
                        )}
                    </>
                )
            )}

            {editing && (
                <MusicMetadataEditModal
                    isOpen={true}
                    kind={editing.kind}
                    artist={editing.artist}
                    album={editing.album}
                    track={editing.track}
                    onClose={() => setEditing(null)}
                    onSaved={() => setRefreshSeq(s => s + 1)}
                />
            )}

            {addToPlaylistTrackId && (
                <AddToPlaylistModal
                    isOpen={true}
                    onClose={() => setAddToPlaylistTrackId(null)}
                    mediaId={addToPlaylistTrackId}
                />
            )}
            </>
            )}

            {trackContextMenu && currentAlbum && (
                <div
                    style={{ top: trackContextMenu.y, left: trackContextMenu.x }}
                    className="fixed z-[9999] bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] rounded-md shadow-2xl py-1 w-48 text-sm"
                    onClick={e => e.stopPropagation()}
                >
                    <button
                        onClick={() => { playFromIndex(trackContextMenu.index); setTrackContextMenu(null); }}
                        className="w-full text-left px-4 py-2 hover:bg-[var(--vora-bg-surface)] text-[var(--vora-text-primary)]"
                    >
                        Play
                    </button>
                    <button
                        onClick={() => { playNext([buildPlayableForTrack(trackContextMenu.track, currentAlbum)]); setTrackContextMenu(null); }}
                        className="w-full text-left px-4 py-2 hover:bg-[var(--vora-bg-surface)] text-[var(--vora-text-primary)]"
                    >
                        Play Next
                    </button>
                    <button
                        onClick={() => { addToQueue([buildPlayableForTrack(trackContextMenu.track, currentAlbum)]); setTrackContextMenu(null); }}
                        className="w-full text-left px-4 py-2 hover:bg-[var(--vora-bg-surface)] text-[var(--vora-text-primary)]"
                    >
                        Add to Queue
                    </button>
                    <div className="border-t border-[var(--vora-border-subtle)] my-1" />
                    <button
                        onClick={() => { toggleTrackLike(trackContextMenu.track.id, trackContextMenu.track.isLiked); setTrackContextMenu(null); }}
                        className="w-full text-left px-4 py-2 hover:bg-[var(--vora-bg-surface)] text-[var(--vora-text-primary)]"
                    >
                        {trackContextMenu.track.isLiked ? 'Remove from Liked Songs' : 'Add to Liked Songs'}
                    </button>
                    <button
                        onClick={() => { setAddToPlaylistTrackId(trackContextMenu.track.id); setTrackContextMenu(null); }}
                        className="w-full text-left px-4 py-2 hover:bg-[var(--vora-bg-surface)] text-[var(--vora-text-primary)]"
                    >
                        Add to Playlist...
                    </button>
                    <div className="border-t border-[var(--vora-border-subtle)] my-1" />
                    <button
                        onClick={() => { startRadioFromSeed({ seedKind: 'Track', seedTrackId: trackContextMenu.track.id }); setTrackContextMenu(null); }}
                        className="w-full text-left px-4 py-2 hover:bg-[var(--vora-bg-surface)] text-[var(--vora-text-primary)]"
                    >
                        Start Track Radio
                    </button>
                    {isServerAdmin && (
                        <>
                            <div className="border-t border-[var(--vora-border-subtle)] my-1" />
                            <button
                                onClick={() => { setEditing({ kind: 'track', track: trackContextMenu.track }); setTrackContextMenu(null); }}
                                className="w-full text-left px-4 py-2 hover:bg-[var(--vora-bg-surface)] text-[var(--vora-text-secondary)]"
                            >
                                Edit Track
                            </button>
                        </>
                    )}
                </div>
            )}
            </div>
        </div>
    );
}

function formatHours(totalSeconds: number): string {
    if (totalSeconds <= 0) return '0 minutes';
    const hours = Math.floor(totalSeconds / 3600);
    const minutes = Math.floor((totalSeconds % 3600) / 60);
    if (hours === 0) return `${minutes} ${minutes === 1 ? 'minute' : 'minutes'}`;
    if (hours < 100) return `${hours}h ${minutes}m`;
    return `${hours.toLocaleString()} hours`;
}

function StatBlock({ label, value }: { label: string; value: string | number }) {
    return (
        <div className="bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] rounded-lg p-4 text-center">
            <div className="text-2xl sm:text-3xl font-bold text-[var(--vora-text-primary)]">{value}</div>
            <div className="text-xs uppercase tracking-widest text-[var(--vora-text-muted)] mt-1">{label}</div>
        </div>
    );
}

function DistributionBlock({ title, labels, values, compact }: { title: string; labels: string[]; values: number[]; compact?: boolean }) {
    const max = Math.max(...values, 1);
    return (
        <div className="bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] rounded-lg p-4">
            <h4 className="text-sm font-bold text-[var(--vora-text-primary)] mb-3">{title}</h4>
            <div className="flex items-end gap-1 h-24">
                {values.map((v, i) => (
                    <div key={i} className="flex-1 flex flex-col items-center justify-end h-full">
                        <div
                            className="w-full bg-gradient-to-t from-pink-600 to-amber-500 rounded-sm transition-all"
                            style={{ height: `${(v / max) * 100}%` }}
                            title={`${labels[i]}: ${v} plays`}
                        />
                    </div>
                ))}
            </div>
            <div className="flex gap-1 mt-2">
                {labels.map((l, i) => (
                    <div key={i} className={`flex-1 text-center text-[var(--vora-text-muted)] ${compact ? 'text-[9px]' : 'text-xs'}`}>{compact && i % 3 !== 0 ? '' : l}</div>
                ))}
            </div>
        </div>
    );
}
