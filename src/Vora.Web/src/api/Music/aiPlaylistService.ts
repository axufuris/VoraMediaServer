import { apiClient } from '../client';

export type AiPlaylistKind = 'AiPlaylist' | 'Bridge' | 'Blend' | 'Requested';

export interface AiPlaylistVM {
    id: string;
    kind: AiPlaylistKind;
    name: string;
    // Why it suits the listener, in a sentence.
    description?: string | null;
    // The words of a "Make me a playlist for..." request.
    prompt?: string | null;
    // The other profile in a Blend.
    partnerName?: string | null;
    artworkUrl?: string | null;
    trackCount: number;
    generatedAt: string;
}

export interface AiPlaylistsVM {
    // False when the server has AI playlists off or this profile opted out.
    enabled: boolean;
    requestsEnabled: boolean;
    weekly: AiPlaylistVM[];
    blends: AiPlaylistVM[];
    requests: AiPlaylistVM[];
}

export interface BlendPartner {
    profileId: string;
    name: string;
    imageUrl?: string | null;
}

// Each made playlist is a mix: open it with musicService.getMixDetail and keep
// it with musicService.saveMixAsPlaylist.
export const aiPlaylistService = {
    get: async (serverId?: string): Promise<AiPlaylistsVM> => {
        const response = await apiClient.get<AiPlaylistsVM>('/music/ai', { serverId });
        return response.data;
    },
    make: async (prompt: string, serverId?: string): Promise<{ mixId: string }> => {
        const response = await apiClient.post<{ mixId: string }>('/music/ai/requests', { prompt }, { serverId });
        return response.data;
    },
    getBlendPartners: async (serverId?: string): Promise<BlendPartner[]> => {
        const response = await apiClient.get<BlendPartner[]>('/music/ai/blend-partners', { serverId });
        return response.data;
    },
    blend: async (partnerProfileId: string, serverId?: string): Promise<{ mixId: string }> => {
        const response = await apiClient.post<{ mixId: string }>('/music/ai/blends', { partnerProfileId }, { serverId });
        return response.data;
    },
    // Admin: make this week's set for every eligible profile now.
    generateNow: async (serverId?: string): Promise<void> => {
        await apiClient.post('/music/ai/generate', null, { serverId });
    },
};
