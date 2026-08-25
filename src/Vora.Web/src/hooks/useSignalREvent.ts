import { useEffect, useRef } from 'react';
import { HubConnectionBuilder, HubConnection, HubConnectionState } from '@microsoft/signalr';
import { serverVault } from '../utils/serverVault';
import { StorageKeys } from '../utils/storageKeys';

let sharedConnection: HubConnection | null = null;
let currentConnectionUrl: string | null = null;

/**
 * Tear down the shared SignalR hub connection. Call from sign-out and
 * switch-profile paths so the next `useSignalREvent` mount rebuilds the
 * connection with the fresh token. Without this, soft navigations (no full
 * page reload) leak the old profile's hub connection — it keeps receiving
 * events authenticated as the previous identity until the tab is closed.
 */
export function disconnectSignalR(): void {
    if (sharedConnection) {
        try {
            sharedConnection.stop();
        } catch {
            // Ignore — stop() can throw if already disconnected.
        }
    }
    sharedConnection = null;
    currentConnectionUrl = null;
}

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
        }

        const connection = sharedConnection;
        const handleEvent = (payload: T) => {
            callbackRef.current(payload);
        };

        connection.on(eventName, handleEvent);

        // Ensure the connection is actually running. A soft navigation can reuse
        // a shared connection that's sitting in Disconnected state — e.g. its
        // initial start failed, or it dropped and withAutomaticReconnect
        // exhausted its retry schedule. Neither case is retried automatically,
        // so start it here; otherwise the handler is attached to a dead
        // connection and no events arrive until a full page reload rebuilds it.
        if (connection.state === HubConnectionState.Disconnected) {
            connection.start()
                .then(() => console.log(`Connected to Vora SignalR Hub at ${baseUrl}!`))
                .catch(err => console.error("SignalR Connection Error: ", err));
        }

        return () => {
            connection.off(eventName, handleEvent);
        };
    }, [eventName]);
}
