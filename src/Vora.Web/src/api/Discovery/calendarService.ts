import { apiClient } from '../client';

export interface CalendarEventVM {
    id: string;
    externalId?: string;
    externalProviderId?: string;
    libraryItemId?: string;
    title: string;
    subTitle?: string;
    mediaType: string;
    releaseDate: string; // ISO date string
    airTime?: string; // "HH:mm:ss" format
    releaseType: string;
    contentRating: string;
    posterUrl?: string;
    backgroundUrl?: string;
    isInLibrary: boolean;
    isWatchlisted: boolean;
}

export const calendarService = {
    getEvents: async (startDate: Date, endDate: Date, serverId?: string): Promise<CalendarEventVM[]> => {
        const response = await apiClient.get<CalendarEventVM[]>('/calendar', {
            params: {
                startDate: startDate.toISOString(),
                endDate: endDate.toISOString()
            },
            serverId
        });
        return response.data;
    }
};