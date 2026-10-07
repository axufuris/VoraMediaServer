import { apiClient } from '../client';

export type UnusedFileKind =
    | 'Artwork'
    | 'ProfilePictures'
    | 'ScrubThumbnails'
    | 'DownloadedSubtitles'
    | 'OriginalArtworkCache'
    | 'SubtitleCache'
    | 'RemovedPlugins'
    | 'UnfinishedFiles'
    | 'Recordings';

export interface UnusedFileGroupVM {
    kind: UnusedFileKind;
    folder: string;
    files: number;
    bytes: number;
    removable: boolean;
    examples: string[];
}

export interface UnusedFilesReportVM {
    scannedAt: string;
    removed: boolean;
    removableFiles: number;
    removableBytes: number;
    groups: UnusedFileGroupVM[];
}

export const maintenanceService = {
    scanUnusedFiles: async (serverId?: string): Promise<UnusedFilesReportVM> => {
        const response = await apiClient.get<UnusedFilesReportVM>('/admin/maintenance/unused-files', { serverId });
        return response.data;
    },

    removeUnusedFiles: async (serverId?: string): Promise<void> => {
        await apiClient.post<void>('/admin/maintenance/unused-files/remove', undefined, { serverId });
    },
};
