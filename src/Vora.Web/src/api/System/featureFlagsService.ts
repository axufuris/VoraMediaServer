import { apiClient } from '../client';

export interface FeatureFlagsVM {
    discover: boolean;
    forYou: boolean;
    releaseCalendar: boolean;
    liveTv: boolean;
    dvr: boolean;
    internetRadio: boolean;
    podcasts: boolean;
    // Derived from the installed plugins rather than an admin toggle: true only
    // when a subtitle-search provider is installed AND configured.
    subtitleSearch: boolean;
}

export const DEFAULT_FEATURE_FLAGS: FeatureFlagsVM = {
    discover: true,
    forYou: true,
    releaseCalendar: true,
    liveTv: true,
    dvr: true,
    internetRadio: true,
    podcasts: true,
    // Off by default: the feature needs an API key, so assuming it works would
    // show a Find Subtitles button that can only fail.
    subtitleSearch: false
};

export type UpdateFeatureFlagsRequest = FeatureFlagsVM;

export const featureFlagsService = {
    getFeatureFlags: async (serverId?: string): Promise<FeatureFlagsVM> => {
        const response = await apiClient.get<FeatureFlagsVM>('/server/features', { serverId });
        return response.data;
    },

    updateFeatureFlags: async (flags: UpdateFeatureFlagsRequest, serverId?: string): Promise<void> => {
        await apiClient.put('/settings/features', flags, { serverId });
    }
};
