import { useEffect, useState, useCallback, useMemo, useRef } from 'react';
import { useLocation, useNavigate, useParams, useSearchParams } from 'react-router-dom';
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
import PageHeader from '../../../components/Client/Primitives/PageHeader';
import Tabs from '../../../components/Client/Primitives/Tabs';
import MusicSearchToggle from '../../../components/Audio/MusicSearchToggle';
import PlaylistsPage from '../Playlists/PlaylistsPage';

import { type MusicNavState, parseMusicNavState } from './Music/musicNavState';
import { MUSIC_SUB_TABS, musicSubTabLabel, readMusicSubTab, saveMusicSubTab, type MusicSubTab } from './Music/musicSubTab';
import MusicRecapView from './Music/MusicRecapView';
import MusicGenresView from './Music/MusicGenresView';
import MusicGenreView from './Music/MusicGenreView';
import MusicLikesView from './Music/MusicLikesView';
import MusicTopView from './Music/MusicTopView';
import MusicMixView from './Music/MusicMixView';
import MusicAlbumView from './Music/MusicAlbumView';
import MusicArtistView from './Music/MusicArtistView';
import MusicForYouView from './Music/MusicForYouView';
import MusicArtistsGrid from './Music/MusicArtistsGrid';
import MusicAlbumsView from './Music/MusicAlbumsView';
import { trackSubtitle } from '../../../utils/trackSubtitle';

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
    const location = useLocation();
    const navigate = useNavigate();
    const dialog = useDialog();
    const { playQueue, addToQueue, playNext, isShuffled, toggleShuffle, startRadio } = usePlayer();

    const [nav, setNav] = useState<MusicNavState>(() => {
        const stored = sessionStorage.getItem(NAV_STORAGE_KEY);
        const storedProfile = sessionStorage.getItem(NAV_PROFILE_KEY) || '';
        const currentProfile = readActiveProfileId();
        if (stored && storedProfile && storedProfile === currentProfile) {
            return parseMusicNavState(stored);
        }
        if (storedProfile !== currentProfile) {
            sessionStorage.removeItem(NAV_STORAGE_KEY);
            sessionStorage.removeItem(NAV_PROFILE_KEY);
        }
        return { view: 'root' };
    });
    const [subTab, setSubTab] = useState<MusicSubTab>(readMusicSubTab);
    const [isSearchOpen, setIsSearchOpen] = useState(false);
    const [homeLoaded, setHomeLoaded] = useState(false);
    const [libraryVersion, setLibraryVersion] = useState(0);

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

    // Every view change pushes a history entry carrying the nav state. Without
    // one, drilling into an artist or album changed no URL, so the browser's Back
    // — the only way back, since the client has no in-page Back buttons — left
    // the Music route entirely and landed on Home.
    const locationRef = useRef(location);
    locationRef.current = location;

    const updateNav = useCallback((next: MusicNavState) => {
        setNav(next);
        sessionStorage.setItem(NAV_STORAGE_KEY, JSON.stringify(next));
        sessionStorage.setItem(NAV_PROFILE_KEY, readActiveProfileId());
        const { pathname, search } = locationRef.current;
        navigate(`${pathname}${search}`, { state: { musicNav: next } });
    }, [navigate]);

    // The other direction: Back and Forward change the entry, so the view follows
    // it.
    const navRef = useRef(nav);
    navRef.current = nav;
    const syncedHistoryKey = useRef<string | null>(null);

    useEffect(() => {
        if (syncedHistoryKey.current === location.key) return;

        const isFirstEntry = syncedHistoryKey.current === null;
        syncedHistoryKey.current = location.key;

        // Stamp the entry we arrived on, the root view included. Leaving the root
        // entry bare meant Back out of the first drill-down found no state to
        // restore and fell through to re-stamping the CURRENT view onto it — so
        // that Back press was swallowed without moving, and the next one left
        // Music altogether. Artists → artist → album needed three Backs to reach
        // the artist list, and the third went Home instead.
        if (isFirstEntry) {
            navigate(`${location.pathname}${location.search}`, { replace: true, state: { musicNav: navRef.current } });
            return;
        }

        // Past the first entry every music entry carries state, so one without it
        // is the query-string replace below — treat it as the root rather than
        // holding the current view.
        const fromHistory = (location.state as { musicNav?: MusicNavState } | null)?.musicNav ?? { view: 'root' as const };

        if (JSON.stringify(navRef.current) !== JSON.stringify(fromHistory)) {
            setNav(fromHistory);
            sessionStorage.setItem(NAV_STORAGE_KEY, JSON.stringify(fromHistory));
        }
    }, [location, navigate]);

    useEffect(() => {
        const mixParam = searchParams.get('mix');
        if (mixParam) {
            queueMicrotask(() => updateNav({ view: 'mix', mixId: mixParam }));
            searchParams.delete('mix');
            setSearchParams(searchParams, { replace: true, state: { musicNav: navRef.current } });
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

    // Enrichment notifies per item, so a scan of a few thousand tracks fires these
    // several times a second. refreshSeq is a dependency of every loader and
    // libraryVersion is the KEY of the album grid, so each event remounted the
    // grid and threw infinite-scroll paging back to the first page — the page
    // appeared to reload continuously. Coalesce the burst: refresh once things go
    // quiet, and at most once a minute while they do not, so a long scan still
    // shows progress without fighting whoever is browsing.
    const REFRESH_QUIET_MS = 3000;
    const REFRESH_MAX_WAIT_MS = 60000;

    const pendingRefresh = useRef({ seq: false, library: false });
    const refreshTimer = useRef<number | null>(null);
    const firstPendingAt = useRef<number | null>(null);

    const flushRefresh = useCallback(() => {
        refreshTimer.current = null;
        firstPendingAt.current = null;
        const pending = pendingRefresh.current;
        pendingRefresh.current = { seq: false, library: false };
        if (pending.seq) setRefreshSeq(s => s + 1);
        if (pending.library) setLibraryVersion(v => v + 1);
    }, []);

    const scheduleRefresh = useCallback((kind: 'seq' | 'library') => {
        pendingRefresh.current[kind] = true;
        const now = Date.now();
        if (firstPendingAt.current === null) firstPendingAt.current = now;
        if (refreshTimer.current !== null) window.clearTimeout(refreshTimer.current);
        const waited = now - firstPendingAt.current;
        const delay = Math.min(REFRESH_QUIET_MS, Math.max(0, REFRESH_MAX_WAIT_MS - waited));
        refreshTimer.current = window.setTimeout(flushRefresh, delay);
    }, [flushRefresh]);

    useEffect(() => () => {
        if (refreshTimer.current !== null) window.clearTimeout(refreshTimer.current);
    }, []);

    const bumpRefresh = useCallback(() => scheduleRefresh('seq'), [scheduleRefresh]);

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
        } finally {
            setHomeLoaded(true);
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
    // LibraryUpdated fires on every ingest, so throwing the viewer back to the
    // root grid meant that during a scan you could not stay on an artist for more
    // than a few seconds. Refresh in place instead: bumping the sequence re-runs
    // whichever view is open, and if the artist or album really has gone, that
    // loader's catch already falls back to the root on its own.
    useSignalREvent<string>("LibraryUpdated", useCallback(() => {
        scheduleRefresh('seq');
        scheduleRefresh('library');
    }, [scheduleRefresh]));

    const resetToRootView = useCallback(() => {
        sessionStorage.removeItem(NAV_STORAGE_KEY);
        setCurrentArtist(null);
        setCurrentAlbum(null);
        setAlbums([]);
        setTracks([]);
        setNav({ view: 'root' });
    }, []);

    const showArtistsGrid = nav.view === 'root' && subTab === 'artists';
    useEffect(() => {
        if (!showArtistsGrid) return;
        queueMicrotask(() => setIsLoading(true));
        musicService.getArtists(undefined, serverId)
            .then(setArtists)
            .catch(err => console.error('Failed to load artists', err))
            .finally(() => setIsLoading(false));
    }, [showArtistsGrid, serverId, refreshSeq]);

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
                resetToRootView();
            })
            .finally(() => setIsLoading(false));
        musicService.getSimilarArtists(nav.artistId, serverId)
            .then(setSimilarArtists)
            .catch(() => setSimilarArtists([]));
    }, [nav.view, nav.artistId, serverId, refreshSeq, resetToRootView]);

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
                resetToRootView();
            })
            .finally(() => setIsLoading(false));
    }, [nav.view, nav.albumId, serverId, refreshSeq, resetToRootView]);

    useEffect(() => {
        if (nav.view !== 'mix' || !nav.mixId) return;
        queueMicrotask(() => setIsLoading(true));
        musicService.getMixDetail(nav.mixId, serverId)
            .then(detail => {
                if (!detail) {
                    resetToRootView();
                    return;
                }
                setCurrentMix(detail);
            })
            .catch(err => {
                console.error('Failed to load mix detail', err);
                resetToRootView();
            })
            .finally(() => setIsLoading(false));
    }, [nav.view, nav.mixId, serverId, refreshSeq, resetToRootView]);

    useEffect(() => {
        if (nav.view !== 'recap') return;
        const targetYear = nav.year ?? new Date().getFullYear();
        queueMicrotask(() => setIsLoading(true));
        musicService.getYearRecap(targetYear, serverId)
            .then(setCurrentRecap)
            .catch(err => {
                console.error('Failed to load year recap', err);
                resetToRootView();
            })
            .finally(() => setIsLoading(false));
    }, [nav.view, nav.year, serverId, refreshSeq, resetToRootView]);

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
                if (!content) { resetToRootView(); return; }
                setCurrentGenre(content);
            })
            .catch(err => {
                console.error('Failed to load genre content', err);
                resetToRootView();
            })
            .finally(() => setIsLoading(false));
    }, [nav.view, nav.genre, serverId, refreshSeq, resetToRootView]);

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
            subtitle: trackSubtitle(album.artistName, album.title),
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

    const buildPlayableForArtistTrack = useCallback((t: ArtistTrackVM, artistName?: string): PlayableMedia => {
        const server = serverId ? serverVault.getServer(serverId) : serverVault.getActiveServer();
        const baseUrl = server?.url || (import.meta.env.VITE_API_BASE_URL as string | undefined)?.replace(/\/api\/?$/, '') || '';
        return {
            id: t.id,
            title: t.title,
            subtitle: trackSubtitle(artistName ?? t.artist, t.albumTitle),
            posterUrl: t.albumArtworkUrl,
            streamUrl: musicService.getTrackStreamUrl(t.id, baseUrl, audioQualityStore.get()),
            serverId: server?.id,
            container: 'audio',
            playbackContextType: 'Music'
        };
    }, [serverId]);

    const playArtistTrackList = useCallback((tracks: ArtistTrackVM[], startIndex: number) => {
        if (tracks.length === 0) return;
        const items = tracks.map(t => buildPlayableForArtistTrack(t));
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

    const breadcrumbs = nav.view !== 'root' && (
        <nav aria-label="Music breadcrumb" className="flex items-center gap-2 text-sm mb-6">
            <button onClick={() => updateNav({ view: 'root' })} className="text-[var(--vora-text-muted)] hover:text-[var(--vora-accent-text)] transition-colors cursor-pointer">
                {musicSubTabLabel(subTab)}
            </button>
            {(nav.view === 'artist' || nav.view === 'album') && currentArtist && (
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
        </nav>
    );

    const setSearchOpen = (open: boolean) => {
        setIsSearchOpen(open);
        if (!open) setSearchQuery('');
    };

    const selectSubTab = (tab: MusicSubTab) => {
        setSubTab(tab);
        saveMusicSubTab(tab);
        setSearchOpen(false);
        if (nav.view !== 'root') updateNav({ view: 'root' });
    };

    const handleSearchResultClick = (r: MusicSearchResultVM) => {
        setSearchOpen(false);
        setSearchResults([]);
        if (r.type === 'Artist' && r.artistId) {
            updateNav({ view: 'artist', artistId: r.artistId });
        } else if (r.type === 'Album' && r.albumId) {
            updateNav({ view: 'album', albumId: r.albumId, artistId: r.artistId });
        } else if (r.type === 'Track' && r.albumId) {
            updateNav({ view: 'album', albumId: r.albumId, artistId: r.artistId });
        }
    };

    const activeBackgroundUrl = searchActive ? null
        : nav.view === 'album' ? currentAlbum?.backgroundUrl
        : null;

    return (
        <div className="relative">
            {activeBackgroundUrl && (
                <div className="absolute inset-x-0 top-0 z-0 pointer-events-none">
                    <img src={activeBackgroundUrl} className="w-full h-[40vh] object-cover opacity-25" alt="" />
                    <div
                        className="absolute inset-0"
                        style={{ background: 'linear-gradient(to bottom, transparent, color-mix(in srgb, var(--vora-bg-canvas) 70%, transparent), var(--vora-bg-canvas))' }}
                    />
                </div>
            )}
            <div className="relative z-10">
            <PageHeader
                title="Music"
                subtitle="Your artists, albums, mixes, and stations."
                titleAccessory={(
                    <MusicSearchToggle
                        isOpen={isSearchOpen}
                        query={searchQuery}
                        onOpenChange={setSearchOpen}
                        onQueryChange={setSearchQuery}
                    />
                )}
            />
            <div className="px-8">
                <MusicServerSwitcher />
                <Tabs<MusicSubTab>
                    tabs={MUSIC_SUB_TABS}
                    active={subTab}
                    onChange={selectSubTab}
                    className="mb-6"
                />
            </div>

            {searchActive && (
                <div className="px-8">
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
                                    className="vora-row-interactive w-full flex items-center gap-3 bg-[var(--vora-bg-surface)] border border-[var(--vora-border-subtle)] rounded-lg p-2.5 cursor-pointer text-left"
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
            )}

            <div hidden={searchActive || nav.view !== 'root'}>
                {subTab === 'forYou' && (
                    <MusicForYouView
                        isLoading={!homeLoaded}
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
                        updateNav={updateNav}
                        playArtistTrackList={playArtistTrackList}
                        startStationRadio={startStationRadio}
                        deleteStation={deleteStation}
                        dialog={dialog}
                    />
                )}
                {subTab === 'artists' && (
                    <MusicArtistsGrid
                        isLoading={isLoading}
                        artists={artists}
                        isServerAdmin={isServerAdmin}
                        onOpenArtist={(artist) => updateNav({ view: 'artist', artistId: artist.id })}
                        onEditArtist={(artist) => setEditing({ kind: 'artist', artist })}
                    />
                )}
                {subTab === 'albums' && (
                    <MusicAlbumsView
                        serverId={serverId}
                        refreshKey={libraryVersion}
                        onOpenAlbum={(album) => updateNav({ view: 'album', albumId: album.id, artistId: album.artistId })}
                    />
                )}
                {subTab === 'playlists' && <PlaylistsPage embedded lockedType="music" showMixes={false} />}
            </div>

            {!searchActive && nav.view !== 'root' && (
                <div className="px-8">
                {breadcrumbs}

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

                </div>
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
            {trackContextMenu && currentAlbum && (
                <div
                    style={{ top: trackContextMenu.y, left: trackContextMenu.x }}
                    className="fixed z-[9999] bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] rounded-md shadow-2xl py-1 w-48 text-sm"
                    onClick={e => e.stopPropagation()}
                >
                    <button
                        onClick={() => { playFromIndex(trackContextMenu.index); setTrackContextMenu(null); }}
                        className="vora-row-interactive w-full text-left px-4 py-2 text-[var(--vora-text-primary)]"
                    >
                        Play
                    </button>
                    <button
                        onClick={() => { playNext([buildPlayableForTrack(trackContextMenu.track, currentAlbum)]); setTrackContextMenu(null); }}
                        className="vora-row-interactive w-full text-left px-4 py-2 text-[var(--vora-text-primary)]"
                    >
                        Play Next
                    </button>
                    <button
                        onClick={() => { addToQueue([buildPlayableForTrack(trackContextMenu.track, currentAlbum)]); setTrackContextMenu(null); }}
                        className="vora-row-interactive w-full text-left px-4 py-2 text-[var(--vora-text-primary)]"
                    >
                        Add to Queue
                    </button>
                    <div className="border-t border-[var(--vora-border-subtle)] my-1" />
                    <button
                        onClick={() => { toggleTrackLike(trackContextMenu.track.id, trackContextMenu.track.isLiked); setTrackContextMenu(null); }}
                        className="vora-row-interactive w-full text-left px-4 py-2 text-[var(--vora-text-primary)]"
                    >
                        {trackContextMenu.track.isLiked ? 'Remove from Liked Songs' : 'Add to Liked Songs'}
                    </button>
                    <button
                        onClick={() => { setAddToPlaylistTrackId(trackContextMenu.track.id); setTrackContextMenu(null); }}
                        className="vora-row-interactive w-full text-left px-4 py-2 text-[var(--vora-text-primary)]"
                    >
                        Add to Playlist...
                    </button>
                    <div className="border-t border-[var(--vora-border-subtle)] my-1" />
                    <button
                        onClick={() => { startRadioFromSeed({ seedKind: 'Track', seedTrackId: trackContextMenu.track.id }); setTrackContextMenu(null); }}
                        className="vora-row-interactive w-full text-left px-4 py-2 text-[var(--vora-text-primary)]"
                    >
                        Start Track Radio
                    </button>
                    {isServerAdmin && (
                        <>
                            <div className="border-t border-[var(--vora-border-subtle)] my-1" />
                            <button
                                onClick={() => { setEditing({ kind: 'track', track: trackContextMenu.track }); setTrackContextMenu(null); }}
                                className="vora-row-interactive w-full text-left px-4 py-2 text-[var(--vora-text-secondary)]"
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
