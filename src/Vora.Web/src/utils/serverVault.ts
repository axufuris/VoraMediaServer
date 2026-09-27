export interface VoraServer {
    id: string;          // e.g., 'home-server-1'
    name: string;        // e.g., 'My Vora Home'
    url: string;         // e.g., 'https://home.myVora.local:5001'
    token: string;       // The user's specific profile token for this server
    profileId: string;   // The decoded profile ID
    isAdmin: boolean;    // Is this profile a server admin?
}

const VAULT_KEY = 'Vora_servers_vault';
const ACTIVE_SERVER_KEY = 'Vora_active_server_id';

export const serverVault = {
    getServers: (): VoraServer[] => {
        try {
            return JSON.parse(localStorage.getItem(VAULT_KEY) || '[]');
        } catch {
            return [];
        }
    },

    getServer: (id: string): VoraServer | undefined => {
        return serverVault.getServers().find(s => s.id === id);
    },

    addOrUpdateServer: (server: VoraServer) => {
        const servers = serverVault.getServers();
        const index = servers.findIndex(s => s.id === server.id);

        if (index >= 0) {
            servers[index] = server;
        } else {
            servers.push(server);
        }

        localStorage.setItem(VAULT_KEY, JSON.stringify(servers));
    },

    removeServer: (id: string) => {
        const servers = serverVault.getServers().filter(s => s.id !== id);
        localStorage.setItem(VAULT_KEY, JSON.stringify(servers));

        if (serverVault.getActiveServerId() === id) {
            localStorage.removeItem(ACTIVE_SERVER_KEY);
        }
    },

    getActiveServerId: (): string | null => {
        return localStorage.getItem(ACTIVE_SERVER_KEY);
    },

    setActiveServerId: (id: string) => {
        localStorage.setItem(ACTIVE_SERVER_KEY, id);
    },

    getActiveServer: (): VoraServer | undefined => {
        const activeId = serverVault.getActiveServerId();
        if (!activeId) return undefined;
        return serverVault.getServer(activeId);
    },

    clearVault: () => {
        localStorage.removeItem(VAULT_KEY);
        localStorage.removeItem(ACTIVE_SERVER_KEY);
    }
};