import { apiClient } from '../client';

export interface FeatureFlagsVM {
    discover: boolean;
    forYou: boolean;
    releaseCalendar: boolean;
    // Switched on AND usable: the admin toggle plus an IPTV source of that kind.
    // This is what navigation gates on — an entry that opens an empty page is
    // worse than no entry.
    liveTv: boolean;
    dvr: boolean;
    internetRadio: boolean;
    podcasts: boolean;
    // What the admin stored, for the admin screens only. The switch shows and
    // saves this; the derived flag above only decides whether to warn that
    // nothing will reach clients yet.
    liveTvEnabled: boolean;
    internetRadioEnabled: boolean;
    discoverEnabled: boolean;
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
    liveTvEnabled: true,
    internetRadioEnabled: true,
    discoverEnabled: true,
    // Off by default: the feature needs an API key, so assuming it works would
    // show a Find Subtitles button that can only fail.
    subtitleSearch: false
};

export interface UpdateFeatureFlagsRequest {
    discover: boolean;
    forYou: boolean;
    releaseCalendar: boolean;
    liveTv: boolean;
    dvr: boolean;
    internetRadio: boolean;
    podcasts: boolean;
}

// The server reads liveTv / internetRadio from this body straight into the
// stored toggles, so they must carry the stored values. Saving the derived ones
// would erase an admin's choice the moment their playlist stopped resolving —
// which is why the conversion lives here rather than in each caller.
const toRequest = (flags: FeatureFlagsVM): UpdateFeatureFlagsRequest => ({
    discover: flags.discoverEnabled,
    forYou: flags.forYou,
    releaseCalendar: flags.releaseCalendar,
    liveTv: flags.liveTvEnabled,
    dvr: flags.dvr,
    internetRadio: flags.internetRadioEnabled,
    podcasts: flags.podcasts,
});

export const featureFlagsService = {
    getFeatureFlags: async (serverId?: string): Promise<FeatureFlagsVM> => {
        const response = await apiClient.get<FeatureFlagsVM>('/server/features', { serverId });
        return response.data;
    },

    updateFeatureFlags: async (flags: FeatureFlagsVM, serverId?: string): Promise<void> => {
        await apiClient.put('/settings/features', toRequest(flags), { serverId });
    }
};
