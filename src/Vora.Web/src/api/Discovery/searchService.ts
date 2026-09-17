import { apiClient } from '../client';
import { serverVault } from '../../utils/serverVault';

export interface MediaSearchResult {
    id: string;
    type: string;
    title: string;
    sortTitle?: string;
    contentRating?: string;
    posterUrl?: string;
    backgroundUrl?: string;
    releaseDate?: string;
}

export interface ActorSearchResult {
    id: string;
    name: string;
    profileImageUrl?: string;
}

export interface CollectionSearchResult {
    id: string;
    title: string;
    posterUrl?: string;
}

export interface MusicSearchResult {
    id: string;
    type: 'Artist' | 'Album' | 'Track';
    title: string;
    subtitle?: string;
    artworkUrl?: string;
    artistId?: string;
    albumId?: string;
}

export interface GlobalSearchResponse {
    query: string;
    movies: MediaSearchResult[];
    tvShows: MediaSearchResult[];
    actors: ActorSearchResult[];
    collections: CollectionSearchResult[];
    music: MusicSearchResult[];
}

export interface ServerOrigin {
    serverId: string;
    serverName: string;
}

export type TaggedMediaSearchResult = MediaSearchResult & ServerOrigin;
export type TaggedActorSearchResult = ActorSearchResult & ServerOrigin;
export type TaggedCollectionSearchResult = CollectionSearchResult & ServerOrigin;
export type TaggedMusicSearchResult = MusicSearchResult & ServerOrigin;

export interface AggregatedGlobalSearchResponse {
    query: string;
    movies: TaggedMediaSearchResult[];
    tvShows: TaggedMediaSearchResult[];
    actors: TaggedActorSearchResult[];
    collections: TaggedCollectionSearchResult[];
    music: TaggedMusicSearchResult[];
    failedServerIds: string[];
    serverCount: number;
}

export const searchService = {
    searchAll: async (query: string, serverId?: string): Promise<GlobalSearchResponse> => {
        const response = await apiClient.get<GlobalSearchResponse>(`/search?q=${encodeURIComponent(query)}`, { serverId });
        return response.data;
    },

    searchAllServers: async (query: string): Promise<AggregatedGlobalSearchResponse> => {
        const servers = serverVault.getServers();
        if (servers.length === 0) {
            return { query, movies: [], tvShows: [], actors: [], collections: [], music: [], failedServerIds: [], serverCount: 0 };
        }

        const probes = await Promise.allSettled(
            servers.map(s => apiClient.get<GlobalSearchResponse>(`/search?q=${encodeURIComponent(query)}`, { serverId: s.id }).then(r => ({ server: s, data: r.data })))
        );

        const movies: TaggedMediaSearchResult[] = [];
        const tvShows: TaggedMediaSearchResult[] = [];
        const actors: TaggedActorSearchResult[] = [];
        const collections: TaggedCollectionSearchResult[] = [];
        const music: TaggedMusicSearchResult[] = [];
        const failedServerIds: string[] = [];

        const seenMovie = new Set<string>();
        const seenTvShow = new Set<string>();
        const seenActor = new Set<string>();
        const seenCollection = new Set<string>();
        const seenMusic = new Set<string>();

        for (let i = 0; i < probes.length; i++) {
            const probe = probes[i];
            const server = servers[i];
            if (probe.status === 'rejected') {
                failedServerIds.push(server.id);
                continue;
            }
            const origin: ServerOrigin = { serverId: server.id, serverName: server.name };
            const data = probe.value.data;

            for (const m of data.movies) {
                const key = `${server.id}-${m.id}`;
                if (seenMovie.has(key)) continue;
                seenMovie.add(key);
                movies.push({ ...m, ...origin });
            }
            for (const s of data.tvShows) {
                const key = `${server.id}-${s.id}`;
                if (seenTvShow.has(key)) continue;
                seenTvShow.add(key);
                tvShows.push({ ...s, ...origin });
            }
            for (const a of data.actors) {
                const key = `${server.id}-${a.id}`;
                if (seenActor.has(key)) continue;
                seenActor.add(key);
                actors.push({ ...a, ...origin });
            }
            for (const c of data.collections) {
                const key = `${server.id}-${c.id}`;
                if (seenCollection.has(key)) continue;
                seenCollection.add(key);
                collections.push({ ...c, ...origin });
            }
            if (data.music) {
                for (const m of data.music) {
                    const key = `${server.id}-${m.type}-${m.id}`;
                    if (seenMusic.has(key)) continue;
                    seenMusic.add(key);
                    music.push({ ...m, ...origin });
                }
            }
        }

        return { query, movies, tvShows, actors, collections, music, failedServerIds, serverCount: servers.length };
    }
};
