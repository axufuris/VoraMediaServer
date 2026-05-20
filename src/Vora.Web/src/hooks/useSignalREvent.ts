import { useEffect } from 'react';
import { HubConnectionBuilder, HubConnection } from '@microsoft/signalr';
import { serverVault } from '../utils/serverVault';

let sharedConnection: HubConnection | null = null;
let currentConnectionUrl: string | null = null;

export function useSignalREvent<T = any>(eventName: string, callback: (payload: T) => void) {
    useEffect(() => {
        const activeServer = serverVault.getActiveServer();
        const baseUrl = activeServer ? activeServer.url : (import.meta.env.VITE_API_BASE_URL?.replace('/api', '') || '');
        const token = activeServer ? activeServer.token : localStorage.getItem('profile_token');

        if (!sharedConnection || currentConnectionUrl !== baseUrl) {
            if (sharedConnection) {
                sharedConnection.stop();
            }

            currentConnectionUrl = baseUrl;
            sharedConnection = new HubConnectionBuilder()
                .withUrl(`${baseUrl}/hubs/Vora`, {
                    accessTokenFactory: () => token || ''
                })
                .withAutomaticReconnect()
                .build();

            sharedConnection.start()
                .then(() => console.log(`Connected to Vora SignalR Hub at ${baseUrl}!`))
                .catch(err => console.error("SignalR Connection Error: ", err));
        }

        const handleEvent = (payload: T) => {
            callback(payload);
        };

        sharedConnection.on(eventName, handleEvent);

        return () => {
            if (sharedConnection) {
                sharedConnection.off(eventName, handleEvent);
            }
        };
    }, [eventName, callback]);
}