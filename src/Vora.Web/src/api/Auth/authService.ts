import { apiClient, createDirectClient, clearApiClientCache } from '../client';
import { StorageKeys } from '../../utils/storageKeys';
import { disconnectSignalR } from '../../hooks/useSignalREvent';

export interface AuthResponse {
    accessToken: string;
    userId: string;
    displayName: string;
    isAdmin: boolean;
}

export interface SetupStatus {
    isClaimed: boolean;
    registrationMode: number;
    serverName?: string;
    emailEnabled?: boolean;
}

export const authService = {
    getSetupStatus: async (serverId?: string): Promise<SetupStatus> => {
        const response = await apiClient.get<SetupStatus>('/auth/setup-status', { serverId });
        return response.data;
    },

    register: async (email: string, password: string, displayName: string, secretCode?: string, inviteToken?: string, serverId?: string): Promise<AuthResponse> => {
        const response = await apiClient.post<AuthResponse>('/auth/register', { email, password, displayName, secretCode, inviteToken }, { serverId });
        return response.data;
    },

    generateInviteCode: async (serverId?: string): Promise<string> => {
        const response = await apiClient.post<{ code: string }>('/auth/invite-code', null, { serverId });
        return response.data.code;
    },

    setupServer: async (email: string, password: string, displayName: string, serverId?: string): Promise<AuthResponse> => {
        const response = await apiClient.post<AuthResponse>('/auth/setup', { email, password, displayName }, { serverId });
        return response.data;
    },

    login: async (email: string, password: string, serverId?: string): Promise<AuthResponse> => {
        const response = await apiClient.post<AuthResponse>('/auth/login', { email, password }, { serverId });
        return response.data;
    },

    exchangeProfileToken: async (accountId: string, profileId: string, serverId?: string): Promise<string> => {
        const response = await apiClient.post<{ token: string }>(`/auth/exchange-profile-token?accountId=${accountId}&profileId=${profileId}`, null, { serverId });
        return response.data.token;
    },

    probeServer: async (baseUrl: string): Promise<SetupStatus> => {
        const client = createDirectClient(baseUrl);
        const response = await client.get<SetupStatus>('/auth/setup-status');
        return response.data;
    },

    loginToServer: async (baseUrl: string, email: string, password: string): Promise<AuthResponse> => {
        const client = createDirectClient(baseUrl);
        const response = await client.post<AuthResponse>('/auth/login', { email, password });
        return response.data;
    },

    setupServerAt: async (baseUrl: string, email: string, password: string, displayName: string): Promise<AuthResponse> => {
        const client = createDirectClient(baseUrl);
        const response = await client.post<AuthResponse>('/auth/setup', { email, password, displayName });
        return response.data;
    },

    exchangeProfileTokenWithToken: async (baseUrl: string, accountToken: string, accountId: string, profileId: string): Promise<string> => {
        const client = createDirectClient(baseUrl, accountToken);
        const response = await client.post<{ token: string }>(`/auth/exchange-profile-token?accountId=${accountId}&profileId=${profileId}`, null);
        return response.data.token;
    },

    registerOnServer: async (baseUrl: string, email: string, password: string, displayName: string, secretCode?: string, inviteToken?: string): Promise<AuthResponse> => {
        const client = createDirectClient(baseUrl);
        const response = await client.post<AuthResponse>('/auth/register', { email, password, displayName, secretCode, inviteToken });
        return response.data;
    },

    validateInvitation: async (baseUrl: string, token: string): Promise<{ email: string; expiresAt: string }> => {
        const client = createDirectClient(baseUrl);
        const response = await client.post<{ email: string; expiresAt: string }>('/auth/invitations/validate', { token });
        return response.data;
    },

    requestPasswordReset: async (baseUrl: string, email: string): Promise<void> => {
        const client = createDirectClient(baseUrl);
        await client.post('/auth/forgot-password', { email });
    },

    confirmPasswordReset: async (baseUrl: string, token: string, newPassword: string): Promise<void> => {
        const client = createDirectClient(baseUrl);
        await client.post('/auth/reset-password', { token, newPassword });
    },

    logout: () => {
        localStorage.removeItem(StorageKeys.accountToken);
        localStorage.removeItem(StorageKeys.profileToken);
        localStorage.removeItem(StorageKeys.userId);
        localStorage.removeItem(StorageKeys.profileName);
        localStorage.removeItem(StorageKeys.isServerAdmin);
        localStorage.removeItem(StorageKeys.isProfileAdmin);

        sessionStorage.clear();
        clearApiClientCache();
        disconnectSignalR();

        window.location.href = '/login';
    }
};
