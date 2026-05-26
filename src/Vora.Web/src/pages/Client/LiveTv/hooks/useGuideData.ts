import { useState, useEffect, useCallback } from 'react';
import { type IptvChannelVM } from '../../../../api/Iptv/iptvAdminService';
import { iptvClientService, type IptvProgramDto } from '../../../../api/Iptv/iptvClientService';
import { dvrService, type IptvRecordingSessionVM } from '../../../../api/Iptv/dvrService';
import { profileDeviceSettingsService } from '../../../../api/Users/profileDeviceSettingsService';
import { serverVault } from '../../../../utils/serverVault';
import { StorageKeys, getProfileIdFromToken } from '../../../../utils/storageKeys';
import { useSignalREvent } from '../../../../hooks/useSignalREvent';

export interface GuidePrefs {
    enabledProviders: string[];
    hiddenChannels: string[];
    favoriteChannels: string[];
    regions: string[];
    resolutions: string[];
    hideEmpty: boolean;
}

const emptyPrefs = (): GuidePrefs => ({
    enabledProviders: [],
    hiddenChannels: [],
    favoriteChannels: [],
    regions: [],
    resolutions: [],
    hideEmpty: false,
});

export interface UseGuideDataResult {
    channels: IptvChannelVM[];
    guideData: Record<string, IptvProgramDto[]>;
    recordingSessions: IptvRecordingSessionVM[];
    isLoading: boolean;
    prefs: GuidePrefs;
    setPrefs: (prefs: GuidePrefs) => void;
    updatePrefs: (newPrefs: GuidePrefs) => void;
}

export function useGuideData(serverId: string | undefined, timelineStart: Date, timelineEnd: Date): UseGuideDataResult {
    const [channels, setChannels] = useState<IptvChannelVM[]>([]);
    const [guideData, setGuideData] = useState<Record<string, IptvProgramDto[]>>({});
    const [recordingSessions, setRecordingSessions] = useState<IptvRecordingSessionVM[]>([]);
    const [isLoading, setIsLoading] = useState(true);
    const [prefs, setPrefs] = useState<GuidePrefs>(emptyPrefs());

    useSignalREvent("DvrSessionsUpdated", useCallback(() => {
        const fetchFreshSessions = async () => {
            try {
                const activeServer = serverVault.getActiveServer();
                if (!activeServer) return;

                const profileToken = localStorage.getItem(StorageKeys.profileToken);
                const activeProfileId = getProfileIdFromToken(profileToken) ?? activeServer.profileId;

                const sessions = await dvrService.getRecordingSessions(activeProfileId, activeServer.id);
                setRecordingSessions(sessions);
            } catch (e) {
                console.error("SignalR: Failed to refresh DVR sessions", e);
            }
        };
        fetchFreshSessions();
    }, []));

    const updatePrefs = useCallback((newPrefs: GuidePrefs) => {
        setPrefs(newPrefs);
        const activeServer = serverVault.getActiveServer();
        if (!activeServer) return;

        const deviceId = localStorage.getItem(StorageKeys.deviceId) || 'unknown';
        const json = JSON.stringify(newPrefs);
        localStorage.setItem(StorageKeys.iptvPrefs(activeServer.profileId, deviceId), json);
        if (profileDeviceSettingsService.saveIptvPrefs) {
            profileDeviceSettingsService.saveIptvPrefs(activeServer.profileId, deviceId, json, serverId).catch(console.error);
        }
    }, [serverId]);

    useEffect(() => {
        const loadGuide = async () => {
            try {
                const activeServer = serverVault.getActiveServer();
                if (!activeServer) return;

                const profileToken = localStorage.getItem(StorageKeys.profileToken);
                const activeProfileId = getProfileIdFromToken(profileToken) ?? activeServer.profileId;
                const userId = localStorage.getItem(StorageKeys.userId) || activeProfileId;
                const deviceId = localStorage.getItem(StorageKeys.deviceId) || 'unknown';

                const allProviders = await iptvClientService.getPlaylists(userId, activeProfileId, serverId);

                let currentPrefs = emptyPrefs();

                let hasSavedSettings = false;
                const savedIptv = localStorage.getItem(StorageKeys.iptvPrefs(activeProfileId, deviceId));

                if (savedIptv && savedIptv !== "[]" && savedIptv !== "") {
                    hasSavedSettings = true;
                    const raw = JSON.parse(savedIptv);
                    if (Array.isArray(raw)) currentPrefs.enabledProviders = raw;
                    else currentPrefs = { ...currentPrefs, ...raw };
                }

                currentPrefs.enabledProviders = currentPrefs.enabledProviders.filter(id => allProviders.some(p => p.id === id));

                if (!hasSavedSettings && currentPrefs.enabledProviders.length === 0 && allProviders.length > 0) {
                    currentPrefs.enabledProviders = allProviders.map(p => p.id);
                }
                setPrefs(currentPrefs);

                const activeChannels = allProviders.filter(p => currentPrefs.enabledProviders.includes(p.id)).flatMap(p => p.channels || []).filter(c => c.kind === 'Tv');
                setChannels(activeChannels);

                const channelIds = activeChannels.map(c => c.externalChannelId);
                const guide = await iptvClientService.getGuide(userId, activeProfileId, channelIds, timelineStart.toISOString(), timelineEnd.toISOString(), serverId);

                const normalizedGuide: Record<string, IptvProgramDto[]> = {};
                for (const [key, value] of Object.entries(guide)) normalizedGuide[key.toLowerCase()] = value;
                setGuideData(normalizedGuide);

                try {
                    const sessions = await dvrService.getRecordingSessions(activeProfileId, serverId);
                    setRecordingSessions(sessions);
                } catch (e) { console.error(e); }

            } catch (error) {
                console.error("Failed to load Live TV Guide", error);
            } finally {
                setIsLoading(false);
            }
        };
        loadGuide();
    }, [serverId, timelineStart, timelineEnd]);

    return {
        channels,
        guideData,
        recordingSessions,
        isLoading,
        prefs,
        setPrefs,
        updatePrefs,
    };
}
