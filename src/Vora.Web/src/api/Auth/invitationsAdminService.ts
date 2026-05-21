import { apiClient } from '../client';

export interface Invitation {
    id: string;
    email: string;
    createdAt: string;
    expiresAt: string;
    invitedByUserId: string | null;
}

export interface CreateInvitationResponse {
    invitation: Invitation;
    emailSent: boolean;
    message: string | null;
}

export const invitationsAdminService = {
    list: async (serverId?: string): Promise<Invitation[]> => {
        const response = await apiClient.get<Invitation[]>('/auth/invitations', { serverId });
        return response.data;
    },
    create: async (email: string, expiresInDays: number | null, serverId?: string): Promise<CreateInvitationResponse> => {
        const response = await apiClient.post<CreateInvitationResponse>('/auth/invitations', { email, expiresInDays }, { serverId });
        return response.data;
    },
    revoke: async (id: string, serverId?: string): Promise<void> => {
        await apiClient.delete(`/auth/invitations/${id}`, { serverId });
    },
};
