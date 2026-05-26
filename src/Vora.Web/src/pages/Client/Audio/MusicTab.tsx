import { useEffect, useState, useCallback, useMemo } from 'react';
import { useParams, useSearchParams } from 'react-router-dom';
import { StorageKeys, SessionKeys, getProfileIdFromToken } from '../../../utils/storageKeys';
import { musicService, type ArtistVM, type AlbumVM, type TrackVM, type ArtistTrackVM, type MusicSearchResultVM, type GeneratedMixSummaryVM, type GeneratedMixDetailVM, type BecauseYouPlayedRowVM, type RadioSeed, type StationVM, type YearRecapVM, type GenreSummaryVM, type GenreContentVM, type ServerPlaybackSessionVM } from '../../../api/Music/musicService';
import { mediaService } from '../../../api/Media/mediaService';
import { usePlayer, type PlayableMedia } from '../../../contexts/usePlayer';
import { serverVault } from '../../../utils/serverVault';
import { useSignalREvent } from '../../../hooks/useSignalREvent';
import MusicMetadataEditModal, { type MusicEntityKind } from '../../../components/Media/MusicMetadataEditModal';
import AddToPlaylistModal from '../../../components/Collections/AddToPlaylistModal';
import MusicServerSwitcher from '../../../components/Audio/MusicServerSwitcher';
import { useDialog } from '../../../dialogs';
import { audioQualityStore } from '../../../utils/audioQuality';

import { type MusicNavState } from './Music/musicNavState';
import MusicRecapView from './Music/MusicRecapView';
import MusicGenresView from './Music/MusicGenresView';
import MusicGenreView from './Music/MusicGenreView';
import MusicLikesView from './Music/MusicLikesView';
import MusicTopView from './Music/MusicTopView';
import MusicMixView from './Music/MusicMixView';
import MusicAlbumView from './Music/MusicAlbumView';
import MusicArtistView from './Music/MusicArtistView';
import MusicArtistsView from './Music/MusicArtistsView';

const NAV_STORAGE_KEY = SessionKeys.musicNavState;
const NAV_PROFILE_KEY = SessionKeys.musicNavProfile;

const readActiveProfileId = (): string => {
    try {
        const token = localStorage.getItem(StorageKeys.profileToken);
        return getProfileIdFromToken(token) ?? '';
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
        localStorage.getItem(StorageKeys.isServerAdmin) === 'true'
        && localStorage.getItem(StorageKeys.isProfileAdmin) === 'true'
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

    const handleSetAlbumRating = useCallback(async (albumId: string, next: number | null) => {
        setCurrentAlbum(prev => prev && prev.id === albumId ? {
            ...prev,
            myRating: next ?? undefined,
            serverAdminRating: isServerAdmin ? (next ?? undefined) : prev.serverAdminRating,
        } : prev);
        try {
            await musicService.setAlbumRating(albumId, next, serverId);
        } catch (err) {
            console.error('Failed to update album rating', err);
        }
    }, [serverId, isServerAdmin]);

    const handleSetArtistRating = useCallback(async (artistId: string, next: number | null) => {
        setCurrentArtist(prev => prev && prev.id === artistId ? {
            ...prev,
            myRating: next ?? undefined,
            serverAdminRating: isServerAdmin ? (next ?? undefined) : prev.serverAdminRating,
        } : prev);
        try {
            await musicService.setArtistRating(artistId, next, serverId);
        } catch (err) {
            console.error('Failed to update artist rating', err);
        }
    }, [serverId, isServerAdmin]);

    const handleSetTrackRating = useCallback(async (trackId: string, next: number | null) => {
        setTracks(prev => prev.map(t => t.id === trackId ? {
            ...t,
            myRating: next ?? undefined,
            serverAdminRating: isServerAdmin ? (next ?? undefined) : t.serverAdminRating,
        } : t));
        try {
            await mediaService.setRating(trackId, next, serverId);
        } catch (err) {
            console.error('Failed to update track rating', err);
        }
    }, [serverId, isServerAdmin]);
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
                <MusicArtistsView
                    isLoading={isLoading}
                    artists={artists}
                    serverPlayback={serverPlayback}
                    dailyMixes={dailyMixes}
                    stations={stations}
                    becauseYouPlayed={becauseYouPlayed}
                    recentlyPlayed={recentlyPlayed}
                    recentlyAddedAlbums={recentlyAddedAlbums}
                    topArtists={topArtists}
                    topTracks={topTracks}
                    likedCount={likedCount}
                    availableYears={availableYears}
                    hasAnyHistory={hasAnyHistory}
                    isServerAdmin={isServerAdmin}
                    updateNav={updateNav}
                    playArtistTrackList={playArtistTrackList}
                    startStationRadio={startStationRadio}
                    deleteStation={deleteStation}
                    onEditArtist={(artist) => setEditing({ kind: 'artist', artist })}
                    dialog={dialog}
                />
            )}

            {nav.view === 'artist' && (
                <MusicArtistView
                    isLoading={isLoading}
                    currentArtist={currentArtist}
                    albums={albums}
                    similarArtists={similarArtists}
                    isServerAdmin={isServerAdmin}
                    playArtist={playArtist}
                    startRadioFromSeed={startRadioFromSeed}
                    handleSetArtistRating={handleSetArtistRating}
                    onEditArtist={(artist) => setEditing({ kind: 'artist', artist })}
                    onEditAlbum={(album) => setEditing({ kind: 'album', album })}
                    updateNav={updateNav}
                    currentArtistId={nav.artistId}
                />
            )}

            {nav.view === 'album' && (
                <MusicAlbumView
                    isLoading={isLoading}
                    currentAlbum={currentAlbum}
                    tracks={tracks}
                    isServerAdmin={isServerAdmin}
                    playFromIndex={playFromIndex}
                    playWholeAlbum={playWholeAlbum}
                    startRadioFromSeed={startRadioFromSeed}
                    handleSetAlbumRating={handleSetAlbumRating}
                    handleSetTrackRating={handleSetTrackRating}
                    toggleTrackLike={toggleTrackLike}
                    onEditAlbum={(album) => setEditing({ kind: 'album', album })}
                    onEditTrack={(track) => setEditing({ kind: 'track', track })}
                    onTrackContextMenu={(payload) => setTrackContextMenu(payload)}
                    formatDuration={formatDuration}
                />
            )}

            {nav.view === 'likes' && (
                <MusicLikesView
                    likedTracks={likedTracks}
                    likedCount={likedCount}
                    serverId={serverId}
                    isShuffled={isShuffled}
                    toggleShuffle={toggleShuffle}
                    playQueue={playQueue}
                    formatDuration={formatDuration}
                    onUnlike={async (trackId) => {
                        try {
                            await musicService.unlikeTrack(trackId, serverId);
                            setLikedTracks(prev => prev.filter(x => x.id !== trackId));
                            setLikedCount(c => Math.max(0, c - 1));
                            window.dispatchEvent(new CustomEvent('music-likes-changed'));
                        } catch (err) {
                            console.error('Unlike failed', err);
                        }
                    }}
                />
            )}

            {nav.view === 'top' && (
                <MusicTopView
                    topTracks={topTracks}
                    isShuffled={isShuffled}
                    toggleShuffle={toggleShuffle}
                    playArtistTrackList={playArtistTrackList}
                    formatDuration={formatDuration}
                />
            )}

            {nav.view === 'mix' && (
                <MusicMixView
                    isLoading={isLoading}
                    currentMix={currentMix}
                    isShuffled={isShuffled}
                    toggleShuffle={toggleShuffle}
                    playMixFromIndex={playMixFromIndex}
                    formatDuration={formatDuration}
                />
            )}

            {nav.view === 'recap' && (
                <MusicRecapView
                    isLoading={isLoading}
                    currentRecap={currentRecap}
                    availableYears={availableYears}
                    updateNav={updateNav}
                />
            )}

            {nav.view === 'genres' && (
                <MusicGenresView
                    isLoading={isLoading}
                    genres={genres}
                    updateNav={updateNav}
                />
            )}

            {nav.view === 'genre' && (
                <MusicGenreView
                    isLoading={isLoading}
                    currentGenre={currentGenre}
                    updateNav={updateNav}
                />
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
