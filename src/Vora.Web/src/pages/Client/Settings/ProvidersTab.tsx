import { useState, useEffect } from 'react';
import { type IptvPlaylistVM, type IptvChannelVM } from '../../../api/Iptv/iptvAdminService';
import { iptvClientService } from '../../../api/Iptv/iptvClientService';
import { profileDeviceSettingsService } from '../../../api/Users/profileDeviceSettingsService';
import { StorageKeys } from '../../../utils/storageKeys';

export default function ProvidersTab({ activeProfileId, serverId, onSaved }: { activeProfileId: string, serverId?: string, onSaved: () => void }) {
    const [providers, setProviders] = useState<IptvPlaylistVM[]>([]);
    const [iptvPrefs, setIptvPrefs] = useState({
        enabledProviders: [] as string[],
        hiddenChannels: [] as string[],
        favoriteChannels: [] as string[],
        regions: [] as string[],
        resolutions: [] as string[],
        hideEmpty: false,
    });

    useEffect(() => {
        const load = async () => {
            try {
                const userId = localStorage.getItem(StorageKeys.userId) || activeProfileId;
                const provs = await iptvClientService.getPlaylists(userId, activeProfileId, serverId);
                setProviders(provs);
                const deviceId = localStorage.getItem(StorageKeys.deviceId) || 'unknown';
                const savedIptv = localStorage.getItem(StorageKeys.iptvPrefs(activeProfileId, deviceId));

                let parsedIptv = {
                    enabledProviders: [] as string[],
                    hiddenChannels: [] as string[],
                    favoriteChannels: [] as string[],
                    regions: [] as string[],
                    resolutions: [] as string[],
                    hideEmpty: false,
                };
                let hasSavedSettings = false;
                if (savedIptv && savedIptv !== '[]' && savedIptv !== '') {
                    hasSavedSettings = true;
                    const raw = JSON.parse(savedIptv);
                    if (Array.isArray(raw)) {
                        parsedIptv.enabledProviders = raw.filter((id: string) => provs.some(p => p.id === id));
                    } else {
                        parsedIptv = { ...parsedIptv, ...raw };
                        parsedIptv.enabledProviders = parsedIptv.enabledProviders.filter((id: string) => provs.some(p => p.id === id));
                    }
                }
                if (!hasSavedSettings && parsedIptv.enabledProviders.length === 0 && provs.length > 0) {
                    parsedIptv.enabledProviders = provs.filter(p => p.m3uUrl).map(p => p.id);
                }
                setIptvPrefs(parsedIptv);
            } catch (error) {
                console.error('Failed to load provider settings', error);
            }
        };
        load();
    }, [activeProfileId, serverId]);

    const toggleProvider = (id: string) => {
        setIptvPrefs(prev => ({
            ...prev,
            enabledProviders: prev.enabledProviders.includes(id)
                ? prev.enabledProviders.filter(p => p !== id)
                : [...prev.enabledProviders, id],
        }));
    };

    const unhideChannel = (channelId: string) => {
        setIptvPrefs(prev => ({
            ...prev,
            hiddenChannels: prev.hiddenChannels.filter(id => id !== channelId),
        }));
    };

    const save = async () => {
        const deviceId = localStorage.getItem(StorageKeys.deviceId) || 'unknown';
        const iptvKey = StorageKeys.iptvPrefs(activeProfileId, deviceId);
        const iptvPrefsString = JSON.stringify(iptvPrefs);
        localStorage.setItem(iptvKey, iptvPrefsString);

        if (activeProfileId) {
            try {
                const prefsKey = `playback_prefs_${activeProfileId}_${deviceId}`;
                const prefsString = localStorage.getItem(prefsKey) || '{}';
                await profileDeviceSettingsService.saveClientSettings(activeProfileId, deviceId, prefsString, iptvPrefsString, serverId);
            } catch (error) {
                console.error('Failed to save provider settings to server:', error);
            }
        }
        onSaved();
    };

    const liveTvProviders = providers.filter(p => p.m3uUrl && p.defaultChannelKind === 'Tv');
    const radioProviders = providers.filter(p => p.m3uUrl && p.defaultChannelKind === 'Radio');
    const allChannels = providers.flatMap(p => p.channels || []);
    const channelById = new Map(allChannels.map(c => [c.externalChannelId, c]));
    const hiddenTvChannelIds = iptvPrefs.hiddenChannels.filter(id => (channelById.get(id)?.kind ?? 'Tv') === 'Tv');
    const hiddenRadioChannelIds = iptvPrefs.hiddenChannels.filter(id => channelById.get(id)?.kind === 'Radio');

    return (
        <div className="space-y-6">
            <ProviderSection
                title="Live TV providers"
                description="Pick which M3U Live TV providers are available on this device."
                list={liveTvProviders}
                emptyLabel="No Live TV providers available."
                enabledIds={iptvPrefs.enabledProviders}
                onToggle={toggleProvider}
            />
            <ProviderSection
                title="Radio providers"
                description="Pick which M3U radio providers are available on this device."
                list={radioProviders}
                emptyLabel="No radio providers available."
                enabledIds={iptvPrefs.enabledProviders}
                onToggle={toggleProvider}
            />
            <HiddenChannelsSection
                title="Hidden Live TV channels"
                description="Channels you have hidden from the Live TV guide."
                ids={hiddenTvChannelIds}
                emptyLabel="You have not hidden any Live TV channels."
                channelById={channelById}
                onUnhide={unhideChannel}
            />
            <HiddenChannelsSection
                title="Hidden radio stations"
                description="Stations you have hidden from the Audio hub."
                ids={hiddenRadioChannelIds}
                emptyLabel="You have not hidden any radio stations."
                channelById={channelById}
                onUnhide={unhideChannel}
            />

            <div className="flex justify-end">
                <button type="button" onClick={save} className="vora-button-primary cursor-pointer">Save provider settings</button>
            </div>
        </div>
    );
}

interface ProviderSectionProps {
    title: string;
    description: string;
    list: IptvPlaylistVM[];
    emptyLabel: string;
    enabledIds: string[];
    onToggle: (id: string) => void;
}

function ProviderSection({ title, description, list, emptyLabel, enabledIds, onToggle }: ProviderSectionProps) {
    return (
        <section className="vora-card p-6">
            <h2 className="m-0 mb-1 text-base font-semibold" style={{ color: 'var(--vora-text-primary)' }}>{title}</h2>
            <p className="m-0 mb-4 text-sm" style={{ color: 'var(--vora-text-muted)' }}>{description}</p>
            {list.length === 0 ? (
                <p className="m-0 rounded p-4 text-sm" style={{ background: 'var(--vora-bg-sunken)', color: 'var(--vora-text-muted)', border: '1px solid var(--vora-border-subtle)' }}>{emptyLabel}</p>
            ) : (
                <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
                    {list.map(p => (
                        <label
                            key={p.id}
                            className="flex cursor-pointer items-center gap-3 rounded-md p-3 transition-colors"
                            style={{ background: 'var(--vora-bg-sunken)', border: '1px solid var(--vora-border-subtle)' }}
                        >
                            <input
                                type="checkbox"
                                checked={enabledIds.includes(p.id)}
                                onChange={() => onToggle(p.id)}
                                className="h-4 w-4 cursor-pointer accent-[var(--vora-accent-500)]"
                            />
                            <span className="text-sm font-medium" style={{ color: 'var(--vora-text-primary)' }}>{p.name}</span>
                        </label>
                    ))}
                </div>
            )}
        </section>
    );
}

interface HiddenChannelsSectionProps {
    title: string;
    description: string;
    ids: string[];
    emptyLabel: string;
    channelById: Map<string, IptvChannelVM>;
    onUnhide: (channelId: string) => void;
}

function HiddenChannelsSection({ title, description, ids, emptyLabel, channelById, onUnhide }: HiddenChannelsSectionProps) {
    return (
        <section className="vora-card p-6">
            <h2 className="m-0 mb-1 text-base font-semibold" style={{ color: 'var(--vora-text-primary)' }}>{title}</h2>
            <p className="m-0 mb-4 text-sm" style={{ color: 'var(--vora-text-muted)' }}>{description}</p>
            {ids.length === 0 ? (
                <p className="m-0 rounded p-4 text-sm" style={{ background: 'var(--vora-bg-sunken)', color: 'var(--vora-text-muted)', border: '1px solid var(--vora-border-subtle)' }}>{emptyLabel}</p>
            ) : (
                <div className="max-h-60 space-y-2 overflow-y-auto pr-1">
                    {ids.map(channelId => {
                        const channel = channelById.get(channelId);
                        return (
                            <div
                                key={channelId}
                                className="flex items-center justify-between rounded-md p-3"
                                style={{ background: 'var(--vora-bg-sunken)', border: '1px solid var(--vora-border-subtle)' }}
                            >
                                <div className="flex items-center gap-3">
                                    <div className="flex h-8 w-12 shrink-0 items-center justify-center overflow-hidden rounded" style={{ background: 'var(--vora-bg-canvas)' }}>
                                        {channel?.logoUrl ? <img src={channel.logoUrl} alt="" className="max-h-full max-w-full object-contain p-1" /> : <span className="text-[8px]" style={{ color: 'var(--vora-text-disabled)' }}>No logo</span>}
                                    </div>
                                    <span className="text-sm font-medium" style={{ color: 'var(--vora-text-primary)' }}>{channel?.name || 'Unknown channel'}</span>
                                </div>
                                <button
                                    type="button"
                                    onClick={() => onUnhide(channelId)}
                                    className="vora-button-secondary cursor-pointer text-xs"
                                >
                                    Unhide
                                </button>
                            </div>
                        );
                    })}
                </div>
            )}
        </section>
    );
}
