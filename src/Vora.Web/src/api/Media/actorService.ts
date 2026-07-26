import { apiClient } from '../client';

export interface ActorFilmographyItem {
    id: string;
    title: string;
    sortTitle?: string;
    releaseDate?: string;
    type: string;
    posterUrl?: string;
    libraryId: string;
    characterName?: string;
    role: string;
    sortOrder: number;
}

export interface ActorProfile {
    id: string;
    tmdbId: number;
    name: string;
    profileImageUrl?: string;
    biography?: string;
    filmography: ActorFilmographyItem[];
    birthday?: string;
    deathday?: string;
    placeOfBirth?: string;
}

export const actorService = {
    getActorProfile: async (id: string, serverId?: string): Promise<ActorProfile> => {
        const response = await apiClient.get<ActorProfile>(`/actors/${id}`, { serverId });
        return response.data;
    }
};
