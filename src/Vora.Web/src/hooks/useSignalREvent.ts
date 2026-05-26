import { useEffect, useRef } from 'react';
import { HubConnectionBuilder, HubConnection } from '@microsoft/signalr';
import { serverVault } from '../utils/serverVault';
import { StorageKeys } from '../utils/storageKeys';

let sharedConnection: HubConnection | null = null;
let currentConnectionUrl: string | null = null;

export const VORA_EVENTS = {
    LogEntryBatch: 'LogEntryBatch',
    CollectionUpdated: 'CollectionUpdated',
    LibraryUpdated: 'LibraryUpdated',
    MediaItemUpdated: 'MediaItemUpdated',
    MediaAnalysisUpdated: 'MediaAnalysisUpdated',
    VideoThumbnailsReady: 'VideoThumbnailsReady',
    SmartListsUpdated: 'SmartListsUpdated',
    TasksUpdated: 'TasksUpdated',
    UserAccessUpdated: 'UserAccessUpdated',
    ProfileAccessUpdated: 'ProfileAccessUpdated',
    DvrSessionsUpdated: 'DvrSessionsUpdated',
    PodcastEpisodesUpdated: 'PodcastEpisodesUpdated',
    MusicArtistUpdated: 'MusicArtistUpdated',
    MusicAlbumUpdated: 'MusicAlbumUpdated',
    MusicMixesUpdated: 'MusicMixesUpdated',
    ServerPlaybackUpdated: 'ServerPlaybackUpdated',
    AdminAlert: 'AdminAlert',
    AdminAlertUnreadChanged: 'AdminAlertUnreadChanged',
    AdminThemeChanged: 'AdminThemeChanged',
    ClientTemplateConfigurationChanged: 'ClientTemplateConfigurationChanged',
    BackupCreated: 'BackupCreated',
    BackupRestored: 'BackupRestored',
    LibraryMigrationUpdated: 'LibraryMigrationUpdated',
    YouTubeAccessChanged: 'YouTubeAccessChanged',
} as const;

export type VoraEventName = typeof VORA_EVENTS[keyof typeof VORA_EVENTS];

export function useSignalREvent<T = unknown>(eventName: VoraEventName | string, callback: (payload: T) => void) {
    const callbackRef = useRef(callback);

    useEffect(() => {
        callbackRef.current = callback;
    }, [callback]);

    useEffect(() => {
        const activeServer = serverVault.getActiveServer();
        const baseUrl = activeServer ? activeServer.url : (import.meta.env.VITE_API_BASE_URL?.replace('/api', '') || '');
        const token = activeServer ? activeServer.token : localStorage.getItem(StorageKeys.profileToken);

        if (!token) {
            return;
        }

        if (!sharedConnection || currentConnectionUrl !== baseUrl) {
            if (sharedConnection) {
                sharedConnection.stop();
            }

            currentConnectionUrl = baseUrl;
            sharedConnection = new HubConnectionBuilder()
                .withUrl(`${baseUrl}/hubs/Vora`, {
                    accessTokenFactory: () => {
                        const server = serverVault.getActiveServer();
                        return (server ? server.token : localStorage.getItem(StorageKeys.profileToken)) || '';
                    }
                })
                .withAutomaticReconnect()
                .build();

            sharedConnection.start()
                .then(() => console.log(`Connected to Vora SignalR Hub at ${baseUrl}!`))
                .catch(err => console.error("SignalR Connection Error: ", err));
        }

        const handleEvent = (payload: T) => {
            callbackRef.current(payload);
        };

        sharedConnection.on(eventName, handleEvent);

        return () => {
            if (sharedConnection) {
                sharedConnection.off(eventName, handleEvent);
            }
        };
    }, [eventName]);
}
