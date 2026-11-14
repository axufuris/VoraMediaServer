import { apiClient } from '../client';

export interface ArtworkResult {
    id: string;
    url: string;
    isUserUploaded: boolean;
    type: string;
    language?: string;
    width?: number;
    height?: number;
    voteAverage?: number;
}

export type ArtworkKind = 'Poster' | 'Backdrop' | 'Logo';

// The API binds `[FromQuery] ArtworkKind kind`. The edit modals sent `type=`,
// which left the required parameter unbound, so every upload and add-from-URL
// was rejected with a bare 400 before any Vora code ran. Built in one place so
// the name cannot drift again.
export function artworkKindQuery(kind: ArtworkKind): string {
    return new URLSearchParams({ kind }).toString();
}

interface SavedArtworkResponse {
    url: string;
}

export async function postArtworkUpload(ownerPath: string, kind: ArtworkKind, file: File, serverId?: string): Promise<string> {
    const data = new FormData();
    data.append('file', file);
    const response = await apiClient.post<SavedArtworkResponse>(`${ownerPath}/artwork/upload?${artworkKindQuery(kind)}`, data, {
        headers: { 'Content-Type': 'multipart/form-data' },
        serverId,
    });
    return response.data.url;
}

export async function postArtworkUrl(ownerPath: string, kind: ArtworkKind, url: string, serverId?: string): Promise<string> {
    const response = await apiClient.post<SavedArtworkResponse>(`${ownerPath}/artwork/url?${artworkKindQuery(kind)}`, JSON.stringify(url), {
        headers: { 'Content-Type': 'application/json' },
        serverId,
    });
    return response.data.url;
}

export const artworkService = {
    getArtworkOptions: async (mediaItemId: string, serverId?: string): Promise<ArtworkResult[]> => {
        const response = await apiClient.get<ArtworkResult[]>(`/media/${mediaItemId}/artwork`, { serverId });
        return response.data;
    },
    fetchProviderArtwork: async (mediaItemId: string, providerId: string, serverId?: string): Promise<void> => {
        await apiClient.post(`/media/${mediaItemId}/artwork/fetch?providerId=${encodeURIComponent(providerId)}`, null, { serverId });
    },
    uploadArtwork: (mediaItemId: string, kind: ArtworkKind, file: File, serverId?: string): Promise<string> =>
        postArtworkUpload(`/media/${mediaItemId}`, kind, file, serverId),
    addArtworkUrl: (mediaItemId: string, kind: ArtworkKind, url: string, serverId?: string): Promise<string> =>
        postArtworkUrl(`/media/${mediaItemId}`, kind, url, serverId),
    deleteArtwork: async (artworkId: string, serverId?: string): Promise<void> => {
        await apiClient.delete(`/media/artwork/${artworkId}`, { serverId });
    }
};
