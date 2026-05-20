import { apiClient, createDirectClient } from '../client';

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
}

export const authService = {
    getSetupStatus: async (serverId?: string): Promise<SetupStatus> => {
        const response = await apiClient.get<SetupStatus>('/auth/setup-status', { serverId });
        return response.data;
    },

    register: async (email: string, password: string, displayName: string, secretCode?: string, serverId?: string): Promise<AuthResponse> => {
        const response = await apiClient.post<AuthResponse>('/auth/register', { email, password, displayName, secretCode }, { serverId });
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

    registerOnServer: async (baseUrl: string, email: string, password: string, displayName: string, secretCode?: string): Promise<AuthResponse> => {
        const client = createDirectClient(baseUrl);
        const response = await client.post<AuthResponse>('/auth/register', { email, password, displayName, secretCode });
        return response.data;
    },

    logout: () => {
        localStorage.removeItem('account_token');
        localStorage.removeItem('profile_token');
        localStorage.removeItem('user_id');
        localStorage.removeItem('profile_name');
        localStorage.removeItem('is_server_admin');
        localStorage.removeItem('is_profile_admin');

        sessionStorage.clear();

        window.location.href = '/login';
    }
};
