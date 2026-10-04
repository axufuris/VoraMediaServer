import { apiClient } from '../client';

export type SetupGuideStatus = 'NotStarted' | 'InProgress' | 'Skipped' | 'Completed';

export interface SetupGuideVM {
    status: SetupGuideStatus;
    step?: string | null;
    moviesAndShows: boolean;
    music: boolean;
    liveTv: boolean;
    internetRadio: boolean;
    podcasts: boolean;
}

export const setupGuideService = {
    get: async (serverId?: string): Promise<SetupGuideVM> => {
        const response = await apiClient.get<SetupGuideVM>('/settings/setup-guide', { serverId });
        return response.data;
    },
    save: async (guide: SetupGuideVM, serverId?: string): Promise<SetupGuideVM> => {
        const response = await apiClient.put<SetupGuideVM>('/settings/setup-guide', guide, { serverId });
        return response.data;
    },
};
