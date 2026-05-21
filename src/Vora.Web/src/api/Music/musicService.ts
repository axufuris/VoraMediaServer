import { apiClient } from '../client';

export interface ArtistVM {
    id: string;
    name: string;
    sortName?: string;
    biography?: string;
    artworkUrl?: string;
    backgroundUrl?: string;
    bannerUrl?: string;
    clearLogoUrl?: string;
    libraryId: string;
    serverAdminRating?: number;
    myRating?: number;
    lockedFields: string[];
}

export interface AlbumVM {
    id: string;
    title: string;
    sortTitle?: string;
    year?: number;
    genre?: string;
    artworkUrl?: string;
    backgroundUrl?: string;
    discArtUrl?: string;
    albumArtist?: string;
    isCompilation: boolean;
    artistId: string;
    artistName: string;
    serverAdminRating?: number;
    myRating?: number;
    lockedFields: string[];
}

export interface TrackVM {
    id: string;
    title: string;
    sortTitle?: string;
    artist?: string;
    trackNumber: number;
    discNumber?: number;
    durationSeconds?: number;
    contentRating?: string;
    albumId?: string;
    isLiked: boolean;
    serverAdminRating?: number;
    myRating?: number;
    lockedFields: string[];
}

export interface ArtistTrackVM {
    id: string;
    title: string;
    artist?: string;
    trackNumber: number;
    discNumber?: number;
    durationSeconds?: number;
    contentRating?: string;
    albumId?: string;
    albumTitle?: string;
    albumArtworkUrl?: string;
    isLiked: boolean;
    serverAdminRating?: number;
    myRating?: number;
}

export interface LikedTracksVM {
    count: number;
    tracks: ArtistTrackVM[];
}

export interface GeneratedMixSummaryVM {
    id: string;
    slot: number;
    name: string;
    descriptionTag?: string;
    artworkUrl?: string;
    trackCount: number;
    kind: 'DailyMix' | 'DiscoverMix' | 'MoodMix' | 'ReleaseRadar';
    generatedAt: string;
    lastDriftAt?: string;
}

export interface GeneratedMixDetailVM {
    id: string;
    slot: number;
    name: string;
    descriptionTag?: string;
    artworkUrl?: string;
    generatedAt: string;
    lastDriftAt?: string;
    tracks: TrackVM[];
}

export interface BecauseYouPlayedRowVM {
    heading: string;
    seedArtistId: string;
    tracks: ArtistTrackVM[];
}

export interface GenreSummaryVM {
    name: string;
    trackCount: number;
    albumCount: number;
    artistCount: number;
    sampleArtworkUrl?: string;
}

export interface GenreContentVM {
    name: string;
    artists: ArtistVM[];
    albums: AlbumVM[];
    tracks: TrackVM[];
}

export interface ServerPlaybackSessionVM {
    profileId: string;
    profileName: string;
    profileImageUrl?: string;
    trackId: string;
    trackTitle: string;
    artist?: string;
    albumTitle?: string;
    albumArtworkUrl?: string;
    durationSeconds?: number;
    currentTimeSeconds?: number;
    startedAt: string;
    lastHeartbeatAt: string;
}

export interface PlaybackHeartbeatRequest {
    trackId: string;
    trackTitle: string;
    artist?: string;
    albumTitle?: string;
    albumArtworkUrl?: string;
    durationSeconds?: number;
    currentTimeSeconds?: number;
}

export type RadioSeedKind = 'Artist' | 'Track' | 'Genre';

export interface RadioSeed {
    seedKind: RadioSeedKind;
    seedArtistId?: string;
    seedTrackId?: string;
    seedGenre?: string;
}

export interface RadioQueueVM extends RadioSeed {
    seedLabel: string;
    tracks: TrackVM[];
}

export interface YearRecapTrackVM {
    id: string;
    title: string;
    artist?: string;
    albumTitle?: string;
    albumArtworkUrl?: string;
    playCount: number;
}

export interface YearRecapArtistVM {
    id: string;
    name: string;
    artworkUrl?: string;
    playCount: number;
}

export interface YearRecapGenreVM {
    name: string;
    playCount: number;
    percent: number;
}

export interface YearRecapVM {
    year: number;
    totalPlays: number;
    totalListeningSeconds: number;
    distinctTrackCount: number;
    distinctArtistCount: number;
    distinctAlbumCount: number;
    topTracks: YearRecapTrackVM[];
    topArtists: YearRecapArtistVM[];
    topGenres: YearRecapGenreVM[];
    playsByDayOfWeek: number[];
    playsByHour: number[];
    peakDayOfWeekLabel?: string;
    peakHourLabel?: string;
    newDiscoveries: YearRecapArtistVM[];
}

export interface StationVM {
    id: string;
    name: string;
    seedKind: RadioSeedKind;
    seedArtistId?: string;
    seedTrackId?: string;
    seedGenre?: string;
    artworkUrl?: string;
    subtitleHint?: string;
    createdAt: string;
    lastPlayedAt?: string;
}

export interface LyricsVM {
    plainLyrics?: string;
    syncedLyrics?: string;
    isSynced: boolean;
    providerName: string;
    sourceUrl?: string;
}

export interface MusicArtworkResultVM {
    url: string;
    thumbnailUrl?: string;
    width?: number;
    height?: number;
    providerName: string;
}

export interface MusicSearchResultVM {
    id: string;
    type: 'Artist' | 'Album' | 'Track';
    title: string;
    subtitle?: string;
    artworkUrl?: string;
    artistId?: string;
    albumId?: string;
}

export interface UpdateArtistRequest {
    name: string;
    sortName?: string | null;
    biography?: string | null;
    artworkUrl?: string | null;
    backgroundUrl?: string | null;
    bannerUrl?: string | null;
    clearLogoUrl?: string | null;
    lockedFields: string[];
}

export interface UpdateAlbumRequest {
    title: string;
    sortTitle?: string | null;
    year?: number | null;
    genre?: string | null;
    artworkUrl?: string | null;
    backgroundUrl?: string | null;
    discArtUrl?: string | null;
    lockedFields: string[];
}

export interface UpdateTrackRequest {
    title: string;
    sortTitle?: string | null;
    trackNumber: number;
    discNumber?: number | null;
    contentRating?: string | null;
    lockedFields: string[];
}

export interface ArtistDetail {
    artist: ArtistVM;
    albums: AlbumVM[];
}

export interface AlbumDetail {
    album: AlbumVM;
    tracks: TrackVM[];
}

export interface AdminMusicHistoryQuery {
    profileId?: string;
    from?: string;
    to?: string;
    search?: string;
    page?: number;
    pageSize?: number;
}

export interface AdminMusicSummaryQuery {
    from?: string;
    to?: string;
}

export interface AdminMusicHistoryRowVM {
    id: string;
    profileId: string;
    profileName: string;
    trackId: string;
    trackTitle: string;
    artist?: string;
    albumTitle?: string;
    albumArtworkUrl?: string;
    playedAt: string;
    durationListenedSeconds: number;
    completed: boolean;
}

export interface AdminMusicHistoryVM {
    rows: AdminMusicHistoryRowVM[];
    total: number;
    page: number;
    pageSize: number;
}

export interface AdminTopTrackVM {
    trackId: string;
    trackTitle: string;
    artist?: string;
    albumTitle?: string;
    albumArtworkUrl?: string;
    playCount: number;
}

export interface AdminTopArtistVM {
    artistId: string;
    artistName: string;
    artworkUrl?: string;
    playCount: number;
}

export interface AdminProfilePlayCountVM {
    profileId: string;
    profileName: string;
    playCount: number;
}

export interface AdminMusicSummaryVM {
    totalPlays: number;
    distinctProfileCount: number;
    topTracks: AdminTopTrackVM[];
    topArtists: AdminTopArtistVM[];
    playsPerProfile: AdminProfilePlayCountVM[];
}

export const musicService = {
    getArtists: async (libraryId?: string, serverId?: string, options?: { limit?: number }): Promise<ArtistVM[]> => {
        const params: Record<string, string | number> = {};
        if (libraryId) params.libraryId = libraryId;
        if (options?.limit !== undefined) params.limit = options.limit;
        const response = await apiClient.get<ArtistVM[]>('/music/artists', {
            params: Object.keys(params).length > 0 ? params : undefined,
            serverId
        });
        return response.data;
    },

    getArtistDetail: async (artistId: string, serverId?: string): Promise<ArtistDetail> => {
        const response = await apiClient.get<ArtistDetail>(`/music/artists/${artistId}`, { serverId });
        return response.data;
    },

    getAlbumDetail: async (albumId: string, serverId?: string): Promise<AlbumDetail> => {
        const response = await apiClient.get<AlbumDetail>(`/music/albums/${albumId}`, { serverId });
        return response.data;
    },

    getArtistTracks: async (artistId: string, serverId?: string): Promise<ArtistTrackVM[]> => {
        const response = await apiClient.get<ArtistTrackVM[]>(`/music/artists/${artistId}/tracks`, { serverId });
        return response.data;
    },

    getTrackStreamUrl: (trackId: string, serverBaseUrl: string, quality?: string): string => {
        const base = serverBaseUrl.endsWith('/') ? serverBaseUrl.slice(0, -1) : serverBaseUrl;
        const qualityParam = quality && quality !== 'Auto' && quality !== 'Original' ? `?quality=${encodeURIComponent(quality.toLowerCase())}` : '';
        return `${base}/api/music/tracks/${trackId}/stream${qualityParam}`;
    },

    updateArtist: async (artistId: string, request: UpdateArtistRequest, serverId?: string): Promise<void> => {
        await apiClient.put(`/music/artists/${artistId}`, request, { serverId });
    },

    updateAlbum: async (albumId: string, request: UpdateAlbumRequest, serverId?: string): Promise<void> => {
        await apiClient.put(`/music/albums/${albumId}`, request, { serverId });
    },

    updateTrack: async (trackId: string, request: UpdateTrackRequest, serverId?: string): Promise<void> => {
        await apiClient.put(`/music/tracks/${trackId}`, request, { serverId });
    },

    uploadArtistArtwork: async (artistId: string, file: File, serverId?: string): Promise<string> => {
        const data = new FormData();
        data.append('file', file);
        const response = await apiClient.post<{ url: string }>(`/music/artists/${artistId}/artwork/upload`, data, {
            headers: { 'Content-Type': 'multipart/form-data' },
            serverId
        });
        return response.data.url;
    },

    uploadAlbumArtwork: async (albumId: string, file: File, serverId?: string): Promise<string> => {
        const data = new FormData();
        data.append('file', file);
        const response = await apiClient.post<{ url: string }>(`/music/albums/${albumId}/artwork/upload`, data, {
            headers: { 'Content-Type': 'multipart/form-data' },
            serverId
        });
        return response.data.url;
    },

    uploadArtistBackground: async (artistId: string, file: File, serverId?: string): Promise<string> => {
        const data = new FormData();
        data.append('file', file);
        const response = await apiClient.post<{ url: string }>(`/music/artists/${artistId}/background/upload`, data, {
            headers: { 'Content-Type': 'multipart/form-data' },
            serverId
        });
        return response.data.url;
    },

    uploadAlbumBackground: async (albumId: string, file: File, serverId?: string): Promise<string> => {
        const data = new FormData();
        data.append('file', file);
        const response = await apiClient.post<{ url: string }>(`/music/albums/${albumId}/background/upload`, data, {
            headers: { 'Content-Type': 'multipart/form-data' },
            serverId
        });
        return response.data.url;
    },

    uploadArtistBanner: async (artistId: string, file: File, serverId?: string): Promise<string> => {
        const data = new FormData();
        data.append('file', file);
        const response = await apiClient.post<{ url: string }>(`/music/artists/${artistId}/banner/upload`, data, {
            headers: { 'Content-Type': 'multipart/form-data' },
            serverId
        });
        return response.data.url;
    },

    uploadArtistClearLogo: async (artistId: string, file: File, serverId?: string): Promise<string> => {
        const data = new FormData();
        data.append('file', file);
        const response = await apiClient.post<{ url: string }>(`/music/artists/${artistId}/clearlogo/upload`, data, {
            headers: { 'Content-Type': 'multipart/form-data' },
            serverId
        });
        return response.data.url;
    },

    uploadAlbumDiscArt: async (albumId: string, file: File, serverId?: string): Promise<string> => {
        const data = new FormData();
        data.append('file', file);
        const response = await apiClient.post<{ url: string }>(`/music/albums/${albumId}/discart/upload`, data, {
            headers: { 'Content-Type': 'multipart/form-data' },
            serverId
        });
        return response.data.url;
    },

    getRecentlyAddedAlbums: async (limit?: number, serverId?: string): Promise<AlbumVM[]> => {
        const response = await apiClient.get<AlbumVM[]>(`/music/albums/recent`, {
            params: limit ? { limit } : undefined,
            serverId
        });
        return response.data;
    },

    getAlbumArtworkSuggestions: async (albumId: string, serverId?: string): Promise<MusicArtworkResultVM[]> => {
        const response = await apiClient.get<MusicArtworkResultVM[]>(`/music/albums/${albumId}/artwork/suggestions`, { serverId });
        return response.data;
    },

    getArtistArtworkSuggestions: async (artistId: string, serverId?: string): Promise<MusicArtworkResultVM[]> => {
        const response = await apiClient.get<MusicArtworkResultVM[]>(`/music/artists/${artistId}/artwork/suggestions`, { serverId });
        return response.data;
    },

    refreshArtistArtwork: async (artistId: string, force: boolean, serverId?: string): Promise<{ updated: boolean; artworkUrl: string | null }> => {
        const response = await apiClient.post<{ updated: boolean; artworkUrl: string | null }>(`/music/artists/${artistId}/artwork/refresh`, undefined, {
            params: { force },
            serverId
        });
        return response.data;
    },

    refreshAlbumArtwork: async (albumId: string, force: boolean, serverId?: string): Promise<{ updated: boolean; artworkUrl: string | null }> => {
        const response = await apiClient.post<{ updated: boolean; artworkUrl: string | null }>(`/music/albums/${albumId}/artwork/refresh`, undefined, {
            params: { force },
            serverId
        });
        return response.data;
    },

    search: async (query: string, limit?: number, serverId?: string): Promise<MusicSearchResultVM[]> => {
        if (!query || query.trim().length < 2) return [];
        const response = await apiClient.get<MusicSearchResultVM[]>(`/music/search`, {
            params: { q: query.trim(), limit },
            serverId
        });
        return response.data;
    },

    likeTrack: async (trackId: string, serverId?: string): Promise<void> => {
        await apiClient.post(`/music/tracks/${trackId}/like`, undefined, { serverId });
    },

    unlikeTrack: async (trackId: string, serverId?: string): Promise<void> => {
        await apiClient.delete(`/music/tracks/${trackId}/like`, { serverId });
    },

    setAlbumRating: async (albumId: string, rating: number | null, serverId?: string): Promise<void> => {
        await apiClient.put(`/music/albums/${albumId}/rating`, { rating }, { serverId });
    },

    setArtistRating: async (artistId: string, rating: number | null, serverId?: string): Promise<void> => {
        await apiClient.put(`/music/artists/${artistId}/rating`, { rating }, { serverId });
    },

    getLikedTracks: async (serverId?: string): Promise<LikedTracksVM> => {
        const response = await apiClient.get<LikedTracksVM>(`/music/likes`, { serverId });
        return response.data;
    },

    getTrackLyrics: async (trackId: string, serverId?: string): Promise<LyricsVM | null> => {
        try {
            const response = await apiClient.get<LyricsVM>(`/music/tracks/${trackId}/lyrics`, { serverId });
            return response.data;
        } catch (err: unknown) {
            const status = (err as { response?: { status?: number } })?.response?.status;
            if (status === 404) return null;
            throw err;
        }
    },

    recordPlay: async (trackId: string, durationListenedSeconds: number, completed: boolean, serverId?: string): Promise<void> => {
        await apiClient.post(`/music/tracks/${trackId}/played`, { durationListenedSeconds, completed }, { serverId });
    },

    getRecentlyPlayed: async (limit?: number, serverId?: string): Promise<ArtistTrackVM[]> => {
        const response = await apiClient.get<ArtistTrackVM[]>(`/music/history/recent`, {
            params: limit ? { limit } : undefined,
            serverId
        });
        return response.data;
    },

    getTopTracks: async (limit?: number, serverId?: string): Promise<ArtistTrackVM[]> => {
        const response = await apiClient.get<ArtistTrackVM[]>(`/music/history/top-tracks`, {
            params: limit ? { limit } : undefined,
            serverId
        });
        return response.data;
    },

    getTopArtists: async (limit?: number, serverId?: string): Promise<ArtistVM[]> => {
        const response = await apiClient.get<ArtistVM[]>(`/music/history/top-artists`, {
            params: limit ? { limit } : undefined,
            serverId
        });
        return response.data;
    },

    updateNowPlaying: async (trackId: string, serverId?: string): Promise<void> => {
        try {
            await apiClient.post(`/music/tracks/${trackId}/now-playing`, undefined, { serverId });
        } catch {
            // best-effort; ignore failures so playback isn't blocked
        }
    },

    startLastFmAuth: async (serverId?: string): Promise<{ token: string; authUrl: string }> => {
        const response = await apiClient.post<{ token: string; authUrl: string }>(`/music/lastfm/auth/start`, undefined, { serverId });
        return response.data;
    },

    completeLastFmAuth: async (token: string, serverId?: string): Promise<{ username: string }> => {
        const response = await apiClient.post<{ username: string }>(`/music/lastfm/auth/complete`, { token }, { serverId });
        return response.data;
    },

    disconnectLastFm: async (serverId?: string): Promise<void> => {
        await apiClient.delete(`/music/lastfm/auth`, { serverId });
    },

    getMixes: async (serverId?: string): Promise<GeneratedMixSummaryVM[]> => {
        const response = await apiClient.get<GeneratedMixSummaryVM[]>(`/music/recommendations/mixes`, { serverId });
        return response.data;
    },

    getMixDetail: async (mixId: string, serverId?: string): Promise<GeneratedMixDetailVM | null> => {
        try {
            const response = await apiClient.get<GeneratedMixDetailVM>(`/music/recommendations/mixes/${mixId}`, { serverId });
            return response.data;
        } catch (err: unknown) {
            const status = (err as { response?: { status?: number } })?.response?.status;
            if (status === 404) return null;
            throw err;
        }
    },

    getBecauseYouPlayed: async (serverId?: string): Promise<BecauseYouPlayedRowVM[]> => {
        const response = await apiClient.get<BecauseYouPlayedRowVM[]>(`/music/recommendations/because-you-played`, { serverId });
        return response.data;
    },

    refreshRecommendations: async (serverId?: string): Promise<void> => {
        await apiClient.post(`/music/recommendations/refresh`, undefined, { serverId });
    },

    startRadio: async (seed: RadioSeed, size?: number, serverId?: string): Promise<RadioQueueVM> => {
        const response = await apiClient.post<RadioQueueVM>(`/music/recommendations/radio`, { ...seed, size }, { serverId });
        return response.data;
    },

    extendRadio: async (seed: RadioSeed, excludeTrackIds: string[], size?: number, serverId?: string): Promise<RadioQueueVM> => {
        const response = await apiClient.post<RadioQueueVM>(`/music/recommendations/radio/extend`, { ...seed, excludeTrackIds, size }, { serverId });
        return response.data;
    },

    listStations: async (serverId?: string): Promise<StationVM[]> => {
        const response = await apiClient.get<StationVM[]>(`/music/stations`, { serverId });
        return response.data;
    },

    saveStation: async (name: string, seed: RadioSeed, serverId?: string): Promise<StationVM> => {
        const response = await apiClient.post<StationVM>(`/music/stations`, { name, ...seed }, { serverId });
        return response.data;
    },

    deleteStation: async (stationId: string, serverId?: string): Promise<void> => {
        await apiClient.delete(`/music/stations/${stationId}`, { serverId });
    },

    touchStation: async (stationId: string, serverId?: string): Promise<void> => {
        try {
            await apiClient.post(`/music/stations/${stationId}/play`, undefined, { serverId });
        } catch {
            // best-effort
        }
    },

    getYearRecap: async (year: number, serverId?: string): Promise<YearRecapVM> => {
        const response = await apiClient.get<YearRecapVM>(`/music/recommendations/year-recap`, { params: { year }, serverId });
        return response.data;
    },

    getYearsWithHistory: async (serverId?: string): Promise<number[]> => {
        const response = await apiClient.get<number[]>(`/music/recommendations/years`, { serverId });
        return response.data;
    },

    getSimilarArtists: async (artistId: string, serverId?: string): Promise<ArtistVM[]> => {
        const response = await apiClient.get<ArtistVM[]>(`/music/artists/${artistId}/similar`, { serverId });
        return response.data;
    },

    getGenres: async (serverId?: string): Promise<GenreSummaryVM[]> => {
        const response = await apiClient.get<GenreSummaryVM[]>(`/music/genres`, { serverId });
        return response.data;
    },

    getGenreContent: async (genre: string, serverId?: string): Promise<GenreContentVM | null> => {
        try {
            const response = await apiClient.get<GenreContentVM>(`/music/genres/${encodeURIComponent(genre)}`, { serverId });
            return response.data;
        } catch (err: unknown) {
            const status = (err as { response?: { status?: number } })?.response?.status;
            if (status === 404) return null;
            throw err;
        }
    },

    playbackHeartbeat: async (req: PlaybackHeartbeatRequest, serverId?: string): Promise<void> => {
        try {
            await apiClient.post(`/music/playback/heartbeat`, req, { serverId });
        } catch {
            // best-effort
        }
    },

    playbackStop: async (serverId?: string): Promise<void> => {
        try {
            await apiClient.post(`/music/playback/stop`, undefined, { serverId });
        } catch {
            // best-effort
        }
    },

    getActiveServerPlayback: async (serverId?: string): Promise<ServerPlaybackSessionVM[]> => {
        try {
            const response = await apiClient.get<ServerPlaybackSessionVM[]>(`/music/playback/active`, { serverId });
            return response.data;
        } catch {
            return [];
        }
    },

    getAdminMusicHistory: async (params: AdminMusicHistoryQuery, serverId?: string): Promise<AdminMusicHistoryVM> => {
        const response = await apiClient.get<AdminMusicHistoryVM>(`/admin/music/history`, { params, serverId });
        return response.data;
    },

    getAdminMusicSummary: async (params: AdminMusicSummaryQuery, serverId?: string): Promise<AdminMusicSummaryVM> => {
        const response = await apiClient.get<AdminMusicSummaryVM>(`/admin/music/summary`, { params, serverId });
        return response.data;
    }
};