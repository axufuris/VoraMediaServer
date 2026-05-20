import { apiClient } from '../client';

export interface FeatureFlagsVM {
    discover: boolean;
    forYou: boolean;
    releaseCalendar: boolean;
    liveTv: boolean;
    dvr: boolean;
    internetRadio: boolean;
    podcasts: boolean;
}

export const DEFAULT_FEATURE_FLAGS: FeatureFlagsVM = {
    discover: true,
    forYou: true,
    releaseCalendar: true,
    liveTv: true,
    dvr: true,
    internetRadio: true,
    podcasts: true
};

export interface UpdateFeatureFlagsRequest extends FeatureFlagsVM {}

export const featureFlagsService = {
    getFeatureFlags: async (serverId?: string): Promise<FeatureFlagsVM> => {
        const response = await apiClient.get<FeatureFlagsVM>('/server/features', { serverId });
        return response.data;
    },

    updateFeatureFlags: async (flags: UpdateFeatureFlagsRequest, serverId?: string): Promise<void> => {
        await apiClient.put('/settings/features', flags, { serverId });
    }
};
