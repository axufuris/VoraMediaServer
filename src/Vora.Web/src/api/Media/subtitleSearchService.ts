import { apiClient } from '../client';

export interface SubtitleSearchResult {
    providerId: string;
    providerFileId: string;
    releaseName: string;
    language: string | null;
    format: string | null;
    hearingImpaired: boolean;
    forced: boolean;
    downloadCount: number | null;
    uploader: string | null;
    rating: number | null;
}

export interface DownloadedSubtitle {
    id: string;
    mediaPartId: string;
    language: string | null;
    title: string | null;
    codec: string | null;
    isForced: boolean;
    isDefault: boolean;
}

// The languages offered in the picker. Deliberately short: a subtitle search
// with no language filter returns thousands of rows for a popular title, and a
// full ISO list would bury the handful anyone actually picks.
export const SUBTITLE_LANGUAGES: { code: string; label: string }[] = [
    { code: 'en', label: 'English' },
    { code: 'es', label: 'Spanish' },
    { code: 'fr', label: 'French' },
    { code: 'de', label: 'German' },
    { code: 'it', label: 'Italian' },
    { code: 'pt', label: 'Portuguese' },
    { code: 'nl', label: 'Dutch' },
    { code: 'pl', label: 'Polish' },
    { code: 'sv', label: 'Swedish' },
    { code: 'da', label: 'Danish' },
    { code: 'no', label: 'Norwegian' },
    { code: 'fi', label: 'Finnish' },
    { code: 'cs', label: 'Czech' },
    { code: 'ru', label: 'Russian' },
    { code: 'tr', label: 'Turkish' },
    { code: 'ar', label: 'Arabic' },
    { code: 'he', label: 'Hebrew' },
    { code: 'hi', label: 'Hindi' },
    { code: 'ja', label: 'Japanese' },
    { code: 'ko', label: 'Korean' },
    { code: 'zh', label: 'Chinese' },
];

// The browser's own preference is the closest thing the web client has to a
// per-viewer language, and it costs nothing to start there. Anything the list
// doesn't carry falls back to English rather than searching a code the picker
// can't then display.
export function defaultSubtitleLanguage(): string {
    const preferred = (navigator.language || 'en').slice(0, 2).toLowerCase();
    return SUBTITLE_LANGUAGES.some(l => l.code === preferred) ? preferred : 'en';
}

export const subtitleSearchService = {
    search: async (mediaId: string, languages: string[], serverId?: string): Promise<SubtitleSearchResult[]> => {
        const response = await apiClient.get<SubtitleSearchResult[]>(
            `/media/${mediaId}/subtitles/search`,
            { serverId, params: { languages: languages.join(',') } },
        );
        return response.data;
    },
    download: async (mediaId: string, providerFileId: string, language: string | null, serverId?: string): Promise<DownloadedSubtitle> => {
        const response = await apiClient.post<DownloadedSubtitle>(
            `/media/${mediaId}/subtitles/download`,
            { providerFileId, language },
            { serverId },
        );
        return response.data;
    },
};
