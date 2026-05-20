import { useState, useEffect, useMemo } from 'react';
import { Link, useParams } from 'react-router-dom';
import { type IptvPlaylistVM } from '../../api/Iptv/iptvAdminService';
import { iptvClientService } from '../../api/Iptv/iptvClientService';
import { serverVault } from '../../utils/serverVault';
import { profileDeviceSettingsService } from '../../api/Users/profileDeviceSettingsService';
import { clientTemplateService, type TemplateMetaVM } from '../../api/System/clientTemplateService';
import { useClientTemplate } from '../../theme/ClientTemplateProvider';
import { useDialog } from '../../dialogs';
import PageHeader from '../../components/Client/Primitives/PageHeader';
import Tabs from '../../components/Client/Primitives/Tabs';
import EmptyState from '../../components/Client/Primitives/EmptyState';
import ScheduledTemplateBanner from '../../components/Client/Primitives/ScheduledTemplateBanner';

type SettingsTabKey = 'templates' | 'playback' | 'providers' | 'account' | 'about';

interface TemplateSwatch {
    canvas: string;
    surface: string;
    accent: string;
    accentText: string;
    text: string;
}

const BUILT_IN_SWATCHES: Record<string, TemplateSwatch> = {
    'vora-cinema': { canvas: '#08080b', surface: '#101015', accent: '#f59e0b', accentText: '#fbbf24', text: '#fafafa' },
    'vora-noir':   { canvas: '#000000', surface: '#0a0a0a', accent: '#94a3b8', accentText: '#cbd5e1', text: '#f8fafc' },
    'vora-velvet': { canvas: '#1a0a0e', surface: '#2a1218', accent: '#c2410c', accentText: '#fed7aa', text: '#fef9c3' },
    'vora-aurora': { canvas: '#020617', surface: '#0c1726', accent: '#14b8a6', accentText: '#5eead4', text: '#e0f2fe' },
    default:       { canvas: '#0a0a0e', surface: '#181820', accent: '#9090a0', accentText: '#c4c4cc', text: '#fafafa' },
};

function TemplateSwatchStrip({ template, isActive }: { template: TemplateMetaVM, isActive: boolean }) {
    if (template.preview) {
        return (
            <div className="h-24 overflow-hidden" style={{ borderBottom: '1px solid var(--vora-border-subtle)' }}>
                <img src={template.preview} alt={`${template.name} preview`} className="h-full w-full object-cover" />
            </div>
        );
    }

    if (isActive) {
        return (
            <div className="grid h-24 grid-cols-5" style={{ borderBottom: '1px solid var(--vora-border-subtle)' }}>
                <div style={{ background: 'var(--vora-bg-canvas)' }} />
                <div style={{ background: 'var(--vora-bg-surface)' }} />
                <div style={{ background: 'var(--vora-accent-500)' }} />
                <div style={{ background: 'var(--vora-accent-text)' }} />
                <div style={{ background: 'var(--vora-text-primary)' }} />
            </div>
        );
    }

    const s = BUILT_IN_SWATCHES[template.id] ?? BUILT_IN_SWATCHES.default;
    return (
        <div className="grid h-24 grid-cols-5" style={{ borderBottom: '1px solid var(--vora-border-subtle)' }}>
            <div style={{ background: s.canvas }} />
            <div style={{ background: s.surface }} />
            <div style={{ background: s.accent }} />
            <div style={{ background: s.accentText }} />
            <div style={{ background: s.text }} />
        </div>
    );
}

const SPOTLIGHT_PREF_KEY = (profileId: string) => `vora_show_spotlight_${profileId}`;

function TemplatesTab({ activeProfileId }: { activeProfileId: string }) {
    const dialog = useDialog();
    const { serverId } = useParams<{ serverId?: string }>();
    const { active, activeInfo, activeSchedule, isSwitching, setActive, clearActive, refresh } = useClientTemplate();
    const [templates, setTemplates] = useState<TemplateMetaVM[]>([]);
    const [isLoading, setIsLoading] = useState(true);
    const [savingId, setSavingId] = useState<string | null>(null);
    const [showSpotlight, setShowSpotlight] = useState<boolean>(() => {
        if (!activeProfileId) return true;
        const stored = localStorage.getItem(SPOTLIGHT_PREF_KEY(activeProfileId));
        return stored === null ? true : stored === 'true';
    });

    useEffect(() => {
        if (!activeProfileId) return;
        const stored = localStorage.getItem(SPOTLIGHT_PREF_KEY(activeProfileId));
        setShowSpotlight(stored === null ? true : stored === 'true');
    }, [activeProfileId]);

    const handleSpotlightToggle = (next: boolean) => {
        setShowSpotlight(next);
        if (activeProfileId) {
            localStorage.setItem(SPOTLIGHT_PREF_KEY(activeProfileId), String(next));
            window.dispatchEvent(new CustomEvent('vora:home-prefs-changed'));
        }
    };

    useEffect(() => {
        let cancelled = false;
        clientTemplateService.getAll(serverId)
            .then(list => { if (!cancelled) setTemplates(list); })
            .catch(err => console.error('Failed to load templates', err))
            .finally(() => { if (!cancelled) setIsLoading(false); });
        return () => { cancelled = true; };
    }, [serverId]);

    const handleSelect = async (templateId: string) => {
        setSavingId(templateId);
        try {
            const ok = await setActive(templateId);
            if (!ok) {
                await dialog.alert({ title: 'Could not apply template', message: 'Please try again.', tone: 'danger' });
            }
        } finally {
            setSavingId(null);
        }
    };

    const handleRevert = async () => {
        const ok = await clearActive();
        if (!ok) {
            await dialog.alert({ title: 'Could not revert', message: 'Please try again.', tone: 'danger' });
            return;
        }
        await refresh();
    };

    const handleUseScheduled = async () => {
        if (!activeSchedule) return;
        await handleSelect(activeSchedule.templateId);
    };

    const sortedTemplates = useMemo(() => {
        return [...templates].sort((a, b) => {
            if (a.isBuiltIn !== b.isBuiltIn) return a.isBuiltIn ? -1 : 1;
            return a.name.localeCompare(b.name);
        });
    }, [templates]);

    const source = activeInfo?.source;
    const activeId = active.id;

    return (
        <div>
            {activeSchedule && (
                <div className="mb-6">
                    <ScheduledTemplateBanner
                        schedule={activeSchedule}
                        isOverridden={source === 'Override'}
                        onRevert={source === 'Override' || source === 'Profile' ? handleRevert : undefined}
                        onApplySchedule={source === 'Override' ? handleUseScheduled : undefined}
                    />
                </div>
            )}

            {!activeSchedule && source === 'Profile' && (
                <div
                    className="mb-6 flex items-center justify-between gap-4 rounded-xl p-4"
                    style={{
                        background: 'var(--vora-bg-surface)',
                        border: '1px solid var(--vora-border-subtle)',
                    }}
                >
                    <div className="flex items-start gap-3">
                        <div
                            className="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg"
                            style={{ background: 'var(--vora-accent-soft)', color: 'var(--vora-accent-text)' }}
                        >
                            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75">
                                <circle cx="12" cy="12" r="10" />
                                <line x1="12" y1="16" x2="12" y2="12" />
                                <line x1="12" y1="8" x2="12.01" y2="8" />
                            </svg>
                        </div>
                        <div>
                            <p className="m-0 text-sm font-semibold" style={{ color: 'var(--vora-text-primary)' }}>You've picked a template for this profile</p>
                            <p className="m-0 mt-0.5 text-xs" style={{ color: 'var(--vora-text-muted)' }}>
                                Your pick wins over whatever the server admin sets as the default. Match the default to follow whatever the admin chooses going forward.
                            </p>
                        </div>
                    </div>
                    <button type="button" onClick={handleRevert} className="vora-button-secondary shrink-0 cursor-pointer">
                        Match server default
                    </button>
                </div>
            )}

            <section className="vora-card mb-6 p-5">
                <h2 className="m-0 mb-1 text-base font-semibold" style={{ color: 'var(--vora-text-primary)' }}>Home page</h2>
                <p className="m-0 mb-4 text-sm" style={{ color: 'var(--vora-text-muted)' }}>Tune what shows up the moment you land on Home.</p>
                <label className="flex cursor-pointer items-start justify-between gap-4 rounded-lg p-3 transition-colors hover:bg-[var(--vora-bg-sunken)]/40">
                    <div>
                        <p className="m-0 text-sm font-semibold" style={{ color: 'var(--vora-text-primary)' }}>Show spotlight hero</p>
                        <p className="m-0 mt-0.5 text-xs" style={{ color: 'var(--vora-text-muted)' }}>
                            The big rotating banner at the top of Home. Turn off if you'd rather jump straight to your rails.
                        </p>
                    </div>
                    <span
                        className="relative inline-flex h-6 w-11 shrink-0 items-center rounded-full transition-colors"
                        style={{ background: showSpotlight ? 'var(--vora-accent-500)' : 'var(--vora-bg-raised)', border: '1px solid var(--vora-border-subtle)' }}
                    >
                        <input
                            type="checkbox"
                            checked={showSpotlight}
                            onChange={e => handleSpotlightToggle(e.target.checked)}
                            className="absolute inset-0 cursor-pointer opacity-0"
                            aria-label="Show spotlight hero on Home"
                        />
                        <span
                            className="inline-block h-4 w-4 transform rounded-full shadow-sm transition-transform"
                            style={{ background: 'var(--vora-text-primary)', transform: `translateX(${showSpotlight ? '22px' : '4px'})` }}
                        />
                    </span>
                </label>
            </section>

            {isLoading ? (
                <div className="grid grid-cols-1 gap-5 sm:grid-cols-2 lg:grid-cols-3">
                    {[1, 2, 3, 4, 5, 6].map(i => <div key={i} className="vora-skeleton h-64" />)}
                </div>
            ) : sortedTemplates.length === 0 ? (
                <EmptyState
                    title="No templates installed"
                    description="Drop a template bundle into the server's Templates folder and ask an admin to rescan."
                />
            ) : (
                <div className="grid grid-cols-1 gap-5 sm:grid-cols-2 lg:grid-cols-3">
                    {sortedTemplates.map(template => {
                        const isActive = activeId === template.id;
                        const isSaving = savingId === template.id || (isSwitching && isActive);
                        const buttonLabel = activeSchedule
                            ? (isActive
                                ? 'In use'
                                : source === 'Override' && activeSchedule.templateId === template.id
                                    ? 'Use scheduled'
                                    : `Override "${activeSchedule.name}"`)
                            : (isActive ? 'In use' : 'Set as default');

                        return (
                            <div
                                key={template.id}
                                className="vora-card overflow-hidden"
                                style={isActive ? { boxShadow: '0 0 0 2px var(--vora-accent-500)' } : undefined}
                            >
                                <TemplateSwatchStrip template={template} isActive={isActive} />
                                <div className="flex flex-1 flex-col p-5">
                                    <div className="mb-1 flex items-start justify-between gap-3">
                                        <h3 className="m-0 text-base font-semibold" style={{ color: 'var(--vora-text-primary)' }}>{template.name}</h3>
                                        <div className="flex shrink-0 flex-wrap gap-1.5">
                                            {template.isBuiltIn ? (
                                                <span className="rounded px-2 py-0.5 text-[10px] font-semibold" style={{ background: 'rgba(255,255,255,0.06)', color: 'var(--vora-text-secondary)', border: '1px solid var(--vora-border-subtle)' }}>Built-in</span>
                                            ) : (
                                                <span className="rounded px-2 py-0.5 text-[10px] font-semibold" style={{ background: 'var(--vora-info-soft)', color: 'var(--vora-info-text)', border: '1px solid color-mix(in srgb, var(--vora-info-500) 35%, transparent)' }}>Plugin</span>
                                            )}
                                            {isActive && (
                                                <span className="rounded px-2 py-0.5 text-[10px] font-semibold" style={{ background: 'var(--vora-success-soft)', color: 'var(--vora-success-text)', border: '1px solid color-mix(in srgb, var(--vora-success-500) 35%, transparent)' }}>Active</span>
                                            )}
                                        </div>
                                    </div>
                                    {template.description && (
                                        <p className="m-0 text-sm" style={{ color: 'var(--vora-text-muted)' }}>{template.description}</p>
                                    )}
                                    <div className="mt-3 flex items-center gap-2 text-xs" style={{ color: 'var(--vora-text-disabled)' }}>
                                        {template.author && <span>by {template.author}</span>}
                                        {template.author && <span>·</span>}
                                        <span>v{template.version}</span>
                                    </div>
                                    <div className="mt-4 flex justify-end border-t pt-3" style={{ borderColor: 'var(--vora-border-subtle)' }}>
                                        {isActive ? (
                                            <span className="text-xs font-semibold" style={{ color: 'var(--vora-text-muted)' }}>{buttonLabel}</span>
                                        ) : (
                                            <button
                                                type="button"
                                                onClick={() => handleSelect(template.id)}
                                                disabled={isSaving}
                                                className="vora-button-primary cursor-pointer text-xs disabled:opacity-50"
                                            >
                                                {isSaving ? 'Applying…' : buttonLabel}
                                            </button>
                                        )}
                                    </div>
                                </div>
                            </div>
                        );
                    })}
                </div>
            )}
        </div>
    );
}

function PlaybackTab({ activeProfileId, serverId, onSaved }: { activeProfileId: string, serverId?: string, onSaved: () => void }) {
    const [clientBitrateLimit, setClientBitrateLimit] = useState(0);
    const [maxResolution, setMaxResolution] = useState(0);
    const [maxAudioChannels, setMaxAudioChannels] = useState(0);

    useEffect(() => {
        const deviceId = localStorage.getItem('device_id') || 'unknown';
        const savedPref = localStorage.getItem(`playback_prefs_${activeProfileId}_${deviceId}`);
        if (savedPref) {
            try {
                const parsed = JSON.parse(savedPref);
                if (parsed.bitrate) setClientBitrateLimit(parsed.bitrate);
                if (parsed.maxResolution !== undefined) setMaxResolution(parsed.maxResolution);
                if (parsed.maxAudioChannels !== undefined) setMaxAudioChannels(parsed.maxAudioChannels);
            } catch {
                setClientBitrateLimit(parseInt(savedPref, 10) || 0);
            }
        }
    }, [activeProfileId]);

    const save = async () => {
        const deviceId = localStorage.getItem('device_id') || 'unknown';
        const prefsKey = `playback_prefs_${activeProfileId}_${deviceId}`;
        const prefsString = JSON.stringify({ bitrate: clientBitrateLimit, maxResolution, maxAudioChannels });
        localStorage.setItem(prefsKey, prefsString);

        if (activeProfileId) {
            try {
                const iptvKey = `iptv_prefs_${activeProfileId}_${deviceId}`;
                const iptvPrefsString = localStorage.getItem(iptvKey) || '{}';
                await profileDeviceSettingsService.saveClientSettings(activeProfileId, deviceId, prefsString, iptvPrefsString, serverId);
            } catch (error) {
                console.error('Failed to save client settings to server:', error);
            }
        }
        onSaved();
    };

    return (
        <div className="space-y-6">
            <section className="vora-card p-6">
                <h2 className="m-0 mb-1 text-base font-semibold" style={{ color: 'var(--vora-text-primary)' }}>Bandwidth &amp; quality</h2>
                <p className="m-0 mb-5 text-sm" style={{ color: 'var(--vora-text-muted)' }}>Cap how much your device pulls per stream. Lower values help on mobile or slow connections.</p>
                <div className="grid grid-cols-1 gap-5 md:grid-cols-3">
                    <div>
                        <label className="mb-1.5 block text-xs font-semibold uppercase tracking-wider" style={{ color: 'var(--vora-text-muted)' }}>Maximum bitrate</label>
                        <select value={clientBitrateLimit} onChange={e => setClientBitrateLimit(parseInt(e.target.value, 10))} className="w-full cursor-pointer rounded-md p-2.5 text-sm outline-none" style={{ background: 'var(--vora-bg-surface)', border: '1px solid var(--vora-border-subtle)', color: 'var(--vora-text-primary)' }}>
                            <option value={0}>Original (no limit)</option>
                            <option value={40}>40 Mbps</option>
                            <option value={20}>20 Mbps</option>
                            <option value={10}>10 Mbps</option>
                            <option value={4}>4 Mbps</option>
                        </select>
                    </div>
                    <div>
                        <label className="mb-1.5 block text-xs font-semibold uppercase tracking-wider" style={{ color: 'var(--vora-text-muted)' }}>Maximum resolution</label>
                        <select value={maxResolution} onChange={e => setMaxResolution(parseInt(e.target.value, 10))} className="w-full cursor-pointer rounded-md p-2.5 text-sm outline-none" style={{ background: 'var(--vora-bg-surface)', border: '1px solid var(--vora-border-subtle)', color: 'var(--vora-text-primary)' }}>
                            <option value={0}>Original (device limit)</option>
                            <option value={2160}>4K (2160p)</option>
                            <option value={1080}>HD (1080p)</option>
                            <option value={720}>HD (720p)</option>
                        </select>
                    </div>
                    <div>
                        <label className="mb-1.5 block text-xs font-semibold uppercase tracking-wider" style={{ color: 'var(--vora-text-muted)' }}>Max audio channels</label>
                        <select value={maxAudioChannels} onChange={e => setMaxAudioChannels(parseInt(e.target.value, 10))} className="w-full cursor-pointer rounded-md p-2.5 text-sm outline-none" style={{ background: 'var(--vora-bg-surface)', border: '1px solid var(--vora-border-subtle)', color: 'var(--vora-text-primary)' }}>
                            <option value={0}>Device capability</option>
                            <option value={2}>Stereo (2.0)</option>
                            <option value={6}>5.1 surround</option>
                            <option value={8}>7.1 surround</option>
                        </select>
                    </div>
                </div>
            </section>

            <div className="flex justify-end">
                <button type="button" onClick={save} className="vora-button-primary cursor-pointer">Save playback settings</button>
            </div>
        </div>
    );
}

function ProvidersTab({ activeProfileId, serverId, onSaved }: { activeProfileId: string, serverId?: string, onSaved: () => void }) {
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
                const userId = localStorage.getItem('user_id') || activeProfileId;
                const provs = await iptvClientService.getPlaylists(userId, activeProfileId, serverId);
                setProviders(provs);
                const deviceId = localStorage.getItem('device_id') || 'unknown';
                const savedIptv = localStorage.getItem(`iptv_prefs_${activeProfileId}_${deviceId}`);

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
        const deviceId = localStorage.getItem('device_id') || 'unknown';
        const iptvKey = `iptv_prefs_${activeProfileId}_${deviceId}`;
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

    const ProviderSection = ({ title, description, list, emptyLabel }: { title: string, description: string, list: IptvPlaylistVM[], emptyLabel: string }) => (
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
                                checked={iptvPrefs.enabledProviders.includes(p.id)}
                                onChange={() => toggleProvider(p.id)}
                                className="h-4 w-4 cursor-pointer accent-[var(--vora-accent-500)]"
                            />
                            <span className="text-sm font-medium" style={{ color: 'var(--vora-text-primary)' }}>{p.name}</span>
                        </label>
                    ))}
                </div>
            )}
        </section>
    );

    const HiddenChannelsSection = ({ title, description, ids, emptyLabel }: { title: string, description: string, ids: string[], emptyLabel: string }) => (
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
                                    onClick={() => unhideChannel(channelId)}
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

    return (
        <div className="space-y-6">
            <ProviderSection title="Live TV providers" description="Pick which M3U Live TV providers are available on this device." list={liveTvProviders} emptyLabel="No Live TV providers available." />
            <ProviderSection title="Radio providers" description="Pick which M3U radio providers are available on this device." list={radioProviders} emptyLabel="No radio providers available." />
            <HiddenChannelsSection title="Hidden Live TV channels" description="Channels you have hidden from the Live TV guide." ids={hiddenTvChannelIds} emptyLabel="You have not hidden any Live TV channels." />
            <HiddenChannelsSection title="Hidden radio stations" description="Stations you have hidden from the Audio hub." ids={hiddenRadioChannelIds} emptyLabel="You have not hidden any radio stations." />

            <div className="flex justify-end">
                <button type="button" onClick={save} className="vora-button-primary cursor-pointer">Save provider settings</button>
            </div>
        </div>
    );
}

function AccountTab({ serverId }: { serverId?: string }) {
    const profileName = localStorage.getItem('profile_name') || 'You';
    return (
        <div className="space-y-6">
            <section className="vora-card p-6">
                <h2 className="m-0 mb-1 text-base font-semibold" style={{ color: 'var(--vora-text-primary)' }}>Signed in as</h2>
                <p className="m-0 text-2xl font-semibold" style={{ color: 'var(--vora-accent-text)' }}>{profileName}</p>
            </section>
            <section className="vora-card p-6">
                <h2 className="m-0 mb-3 text-base font-semibold" style={{ color: 'var(--vora-text-primary)' }}>Manage your profile</h2>
                <div className="flex flex-col gap-2 sm:flex-row">
                    <Link to={serverId ? `/server/${serverId}/account` : '/account'} className="vora-button-secondary cursor-pointer text-center">Account &amp; security</Link>
                    <Link to={serverId ? `/server/${serverId}/history` : '/history'} className="vora-button-secondary cursor-pointer text-center">Watch history</Link>
                    <Link to="/profiles" className="vora-button-secondary cursor-pointer text-center">Switch profile</Link>
                </div>
            </section>
        </div>
    );
}

function AboutTab() {
    return (
        <div className="space-y-6">
            <section className="vora-card p-6">
                <h2 className="m-0 mb-1 text-base font-semibold" style={{ color: 'var(--vora-text-primary)' }}>About this client</h2>
                <p className="m-0 mb-4 text-sm" style={{ color: 'var(--vora-text-muted)' }}>Vora is a self-hosted media server. This is the client running in your browser.</p>
                <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                    <div className="rounded-md p-3" style={{ background: 'var(--vora-bg-sunken)', border: '1px solid var(--vora-border-subtle)' }}>
                        <div className="text-xs font-semibold uppercase tracking-wider" style={{ color: 'var(--vora-text-muted)' }}>Client build</div>
                        <div className="mt-1 text-sm" style={{ color: 'var(--vora-text-primary)' }}>{import.meta.env.MODE ?? 'unknown'}</div>
                    </div>
                    <div className="rounded-md p-3" style={{ background: 'var(--vora-bg-sunken)', border: '1px solid var(--vora-border-subtle)' }}>
                        <div className="text-xs font-semibold uppercase tracking-wider" style={{ color: 'var(--vora-text-muted)' }}>Device id</div>
                        <div className="mt-1 truncate text-sm" style={{ color: 'var(--vora-text-primary)' }} title={localStorage.getItem('device_id') ?? ''}>{localStorage.getItem('device_id') ?? '—'}</div>
                    </div>
                </div>
            </section>
        </div>
    );
}

export default function SettingsPage() {
    const dialog = useDialog();
    const activeServer = serverVault.getActiveServer();
    const activeProfileId = activeServer?.profileId || '';
    const serverId = activeServer?.id;

    const [activeTab, setActiveTab] = useState<SettingsTabKey>('templates');

    const onSaved = () => {
        void dialog.alert({
            title: 'Settings saved',
            message: 'Your preferences have been updated.',
            tone: 'success',
        });
    };

    return (
        <div className="min-h-full pb-20">
            <PageHeader
                title="Settings"
                subtitle="Personalize Vora for this profile. Most changes save automatically."
            />

            <div className="px-8">
                <Tabs<SettingsTabKey>
                    tabs={[
                        { key: 'templates', label: 'Templates' },
                        { key: 'playback', label: 'Playback' },
                        { key: 'providers', label: 'Providers' },
                        { key: 'account', label: 'Account' },
                        { key: 'about', label: 'About' },
                    ]}
                    active={activeTab}
                    onChange={setActiveTab}
                />
            </div>

            <div className="px-8 pt-6">
                {activeTab === 'templates' && <TemplatesTab activeProfileId={activeProfileId} />}
                {activeTab === 'playback' && <PlaybackTab activeProfileId={activeProfileId} serverId={serverId} onSaved={onSaved} />}
                {activeTab === 'providers' && <ProvidersTab activeProfileId={activeProfileId} serverId={serverId} onSaved={onSaved} />}
                {activeTab === 'account' && <AccountTab serverId={serverId} />}
                {activeTab === 'about' && <AboutTab />}
            </div>
        </div>
    );
}
