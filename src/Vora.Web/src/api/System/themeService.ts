import { apiClient } from '../client';
import type { ThemeManifest } from '../../theme/types';

export interface ThemeMetaVM {
    id: string;
    name: string;
    version: string;
    author?: string;
    description?: string;
    /** Absolute URL the backend computed for the preview image, if any. */
    preview?: string;
    isBuiltIn: boolean;
}

export interface ActiveThemeResponse {
    themeId: string;
}

export const themeService = {
    /** List all admin themes installed on the server. Admin-only. */
    getAll: async (serverId?: string): Promise<ThemeMetaVM[]> => {
        const response = await apiClient.get<ThemeMetaVM[]>('/admin/themes', { serverId });
        return response.data;
    },

    /**
     * Active theme id for this server. Profile-accessible (not admin-only) so the
     * ThemeProvider can fetch it on boot for any signed-in user, before they
     * navigate into the admin.
     */
    getActiveId: async (serverId?: string): Promise<string> => {
        const response = await apiClient.get<ActiveThemeResponse>('/admin/themes/active', { serverId });
        return response.data.themeId;
    },

    /** Set the active theme. Admin-only. Persists per server. */
    setActiveId: async (themeId: string, serverId?: string): Promise<void> => {
        await apiClient.put('/admin/themes/active', { themeId }, { serverId });
    },

    /**
     * Full manifest for a plugin-shipped theme. Built-in themes return 404
     * here — the frontend has them bundled at build time. The returned
     * manifest's `assetsBaseUrl` is filled in by the caller (ThemeProvider)
     * so relative image paths inside the manifest resolve to the server's
     * asset-serving endpoint.
     */
    getManifest: async (themeId: string, serverId?: string): Promise<ThemeManifest> => {
        const response = await apiClient.get<ThemeManifest>(`/admin/themes/${encodeURIComponent(themeId)}/manifest`, { serverId });
        return response.data;
    },

    /**
     * Re-scan the Themes/ folder on disk for new or changed bundles. Useful
     * after dropping a new bundle without restarting the API. Returns the
     * new total bundle count.
     */
    rescan: async (serverId?: string): Promise<number> => {
        const response = await apiClient.post<{ bundleCount: number }>('/admin/themes/rescan', undefined, { serverId });
        return response.data.bundleCount;
    },
};
