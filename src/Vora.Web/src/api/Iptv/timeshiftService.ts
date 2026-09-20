import { apiClient } from '../client';
import { serverVault } from '../../utils/serverVault';
import { StorageKeys } from '../../utils/storageKeys';

export const timeshiftService = {
    startTimeshift: async (channelId: string, serverId?: string): Promise<{ url: string }> => {
        const response = await apiClient.post<{ url: string }>('/iptv/timeshift/start', { channelId }, { serverId });
        return response.data;
    },

    stopTimeshift: async (serverId?: string): Promise<void> => {
        await apiClient.post('/iptv/timeshift/stop', {}, { serverId });
    },

    stopTimeshiftBeacon: (serverId?: string): void => {
        try {
            const server = serverId ? serverVault.getServer(serverId) : serverVault.getActiveServer();
            const base = server ? `${server.url}/api` : (import.meta.env.VITE_API_BASE_URL || 'http://localhost:5000/api');
            const token = server?.token || localStorage.getItem(StorageKeys.profileToken) || localStorage.getItem(StorageKeys.accountToken);
            fetch(`${base}/iptv/timeshift/stop`, {
                method: 'POST',
                keepalive: true,
                headers: {
                    'Content-Type': 'application/json',
                    ...(token ? { Authorization: `Bearer ${token}` } : {}),
                },
                body: '{}',
            }).catch(() => { });
        } catch {
            // best-effort teardown during page unload
        }
    },

    pingTimeshift: async (serverId?: string): Promise<void> => {
        await apiClient.post('/iptv/timeshift/ping', {}, { serverId });
    }
};
