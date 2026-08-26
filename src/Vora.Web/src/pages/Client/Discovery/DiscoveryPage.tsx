import { useEffect, useState } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { discoveryService, type DiscoveryRowConfig, type DiscoveryItem } from '../../../api/Discovery/discoveryService';
import { profileDeviceSettingsService } from '../../../api/Users/profileDeviceSettingsService';
import { serverVault } from '../../../utils/serverVault';
import ClientDiscoveryCustomizeModal, { type ClientLayoutItem } from '../../../components/Discovery/DiscoveryCustomizeModal';
import PageHeader from '../../../components/Client/Primitives/PageHeader';
import EmptyState from '../../../components/Client/Primitives/EmptyState';
import MediaRow, { MediaRowItem } from '../../../components/Client/Primitives/MediaRow';
import MediaCard from '../../../components/Client/Primitives/MediaCard';
import DiscoveryStatusBadge from '../../../components/Discovery/DiscoveryStatusBadge';
import { StorageKeys, getProfileIdFromToken } from '../../../utils/storageKeys';

interface DiscoveryPageProps {
    embedded?: boolean;
}

export default function DiscoveryPage({ embedded = false }: DiscoveryPageProps = {}) {
    const { serverId } = useParams<{ serverId?: string }>();
    const [configs, setConfigs] = useState<(DiscoveryRowConfig & { uniqueId: string, serverId?: string, serverName?: string })[]>([]);
    const [clientLayout, setClientLayout] = useState<ClientLayoutItem[]>([]);
    const [watchlistIds, setWatchlistIds] = useState<Set<string>>(new Set());
    const [isLoading, setIsLoading] = useState(true);
    const [isCustomizeOpen, setIsCustomizeOpen] = useState(false);

    const profileToken = localStorage.getItem(StorageKeys.profileToken);
    const activeProfileId = getProfileIdFromToken(profileToken) ?? '';
    const deviceId = localStorage.getItem(StorageKeys.deviceId) || 'unknown';

    useEffect(() => {
        const fetchData = async () => {
            try {
                let savedLayoutJson: string | null = null;
                if (activeProfileId && deviceId !== 'unknown') {
                    savedLayoutJson = await profileDeviceSettingsService.getDiscoveryLayout(activeProfileId, deviceId, serverId);
                }
                if (!savedLayoutJson) {
                    savedLayoutJson = localStorage.getItem(`client_discovery_layout_${activeProfileId}_${deviceId}`);
                }
                const savedLayout: ClientLayoutItem[] = savedLayoutJson ? JSON.parse(savedLayoutJson) : [];
                setClientLayout(savedLayout);

                const server = serverId ? serverVault.getServer(serverId) : serverVault.getActiveServer();
                const serverNameStr = server?.name || 'Local Server';
                const rawConfigs = await discoveryService.getAdminConfigs(serverId);

                const mappedConfigs = rawConfigs
                    .filter(c => c.isEnabled)
                    .map(c => ({
                        ...c,
                        serverId,
                        serverName: serverNameStr,
                        uniqueId: `${serverId || 'global'}_${c.providerId}_${c.rowId}`,
                    }));
                setConfigs(mappedConfigs);

                if (activeProfileId) {
                    const wItems = await discoveryService.getWatchlist(activeProfileId, serverId);
                    setWatchlistIds(new Set(wItems.map(i => i.externalId)));
                }
            } catch (error) {
                console.error('Failed to load discovery data', error);
            } finally {
                setIsLoading(false);
            }
        };
        fetchData();
    }, [serverId, activeProfileId, deviceId]);

    const handleSaveLayout = async (newLayout: ClientLayoutItem[]) => {
        const layoutJson = JSON.stringify(newLayout);
        setClientLayout(newLayout);
        setIsCustomizeOpen(false);
        localStorage.setItem(`client_discovery_layout_${activeProfileId}_${deviceId}`, layoutJson);
        try {
            if (activeProfileId && deviceId !== 'unknown') {
                await profileDeviceSettingsService.saveDiscoveryLayout(activeProfileId, deviceId, layoutJson, serverId);
            }
        } catch (error) {
            console.error('Failed to sync discovery layout to server:', error);
        }
    };

    const displayConfigs = [...configs].map(config => {
        const layoutPref = clientLayout.find(l => l.uniqueId === config.uniqueId);
        if (layoutPref) {
            return { ...config, isEnabled: layoutPref.isEnabled, orderIndex: layoutPref.orderIndex };
        }
        return config;
    }).filter(c => c.isEnabled).sort((a, b) => a.orderIndex - b.orderIndex);

    const customizeAction = (
        <button
            type="button"
            onClick={() => setIsCustomizeOpen(true)}
            className="vora-button-secondary cursor-pointer inline-flex items-center gap-2"
        >
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75">
                <line x1="4" y1="21" x2="4" y2="14" />
                <line x1="4" y1="10" x2="4" y2="3" />
                <line x1="12" y1="21" x2="12" y2="12" />
                <line x1="12" y1="8" x2="12" y2="3" />
                <line x1="20" y1="21" x2="20" y2="16" />
                <line x1="20" y1="12" x2="20" y2="3" />
                <line x1="1" y1="14" x2="7" y2="14" />
                <line x1="9" y1="8" x2="15" y2="8" />
                <line x1="17" y1="16" x2="23" y2="16" />
            </svg>
            Customize
        </button>
    );

    return (
        <>
            <ClientDiscoveryCustomizeModal
                isOpen={isCustomizeOpen}
                onClose={() => setIsCustomizeOpen(false)}
                configs={configs}
                savedLayout={clientLayout}
                onSave={handleSaveLayout}
            />

            <div className="min-h-full pb-20">
                {embedded ? (
                    <div className="flex justify-end px-8 pt-4">
                        {customizeAction}
                    </div>
                ) : (
                    <PageHeader
                        title="Discover"
                        subtitle="What's out there — trending, popular, and curated picks from external sources."
                        actions={customizeAction}
                    />
                )}

                <div className="space-y-10 pt-2">
                    {isLoading ? (
                        <>
                            {[1, 2, 3].map(i => (
                                <div key={i} className="px-8">
                                    <div className="vora-skeleton mb-4 h-6 w-48" />
                                    <div className="flex gap-4 overflow-hidden">
                                        {Array.from({ length: 6 }, (_, j) => <div key={j} className="vora-skeleton h-72 w-48 flex-none" />)}
                                    </div>
                                </div>
                            ))}
                        </>
                    ) : displayConfigs.length === 0 ? (
                        <EmptyState
                            title="No discovery rows enabled"
                            description="Tap Customize to turn on rows you'd like to see, or ask your server admin to configure providers."
                            icon={(
                                <svg width="40" height="40" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5">
                                    <circle cx="12" cy="12" r="9" />
                                    <path d="m15 9-2 6-6 2 2-6 6-2z" />
                                </svg>
                            )}
                        />
                    ) : (
                        displayConfigs.map(config => (
                            <DiscoveryRow
                                key={config.uniqueId}
                                config={config}
                                serverId={config.serverId}
                                watchlistIds={watchlistIds}
                            />
                        ))
                    )}
                </div>
            </div>
        </>
    );
}

function DiscoveryRow({ config, serverId, watchlistIds }: { config: DiscoveryRowConfig, serverId?: string, watchlistIds: Set<string> }) {
    const navigate = useNavigate();
    const [items, setItems] = useState<DiscoveryItem[]>([]);
    const [loading, setLoading] = useState(true);
    const [errorMessage, setErrorMessage] = useState<string | null>(null);

    useEffect(() => {
        let cancelled = false;
        discoveryService.getRowItems(config.providerId, config.rowId, 1, serverId)
            .then(data => {
                if (cancelled) return;
                setItems(data);
                setErrorMessage(null);
                setLoading(false);
            })
            .catch(err => {
                if (cancelled) return;
                console.error(`Discovery row "${config.name}" (${config.providerId}/${config.rowId}) failed to load`, err);
                const data = err?.response?.data;
                const msg = (typeof data === 'string' ? data : data?.error || data?.detail || data?.title) || err?.message || 'Unknown error';
                setErrorMessage(msg);
                setLoading(false);
            });
        return () => { cancelled = true; };
    }, [config, serverId]);

    if (loading) {
        return (
            <div className="px-8">
                <div className="vora-skeleton mb-4 h-6 w-48" />
                <div className="flex gap-4 overflow-hidden">
                    {Array.from({ length: 6 }, (_, i) => <div key={i} className="vora-skeleton h-72 w-48 flex-none" />)}
                </div>
            </div>
        );
    }
    if (errorMessage) {
        return (
            <div className="px-8">
                <h3 className="m-0 mb-2 text-base font-semibold" style={{ color: 'var(--vora-text-primary)' }}>
                    {config.name} <span style={{ color: 'var(--vora-text-muted)' }}>· {config.providerName || config.providerId}</span>
                </h3>
                <p className="m-0 text-sm" style={{ color: 'var(--vora-danger-text)' }}>
                    Couldn't load this row: {errorMessage}
                </p>
                <p className="m-0 mt-1 text-xs" style={{ color: 'var(--vora-text-muted)' }}>
                    Check the provider's API key and connectivity in admin Plugins settings.
                </p>
            </div>
        );
    }
    if (items.length === 0) {
        return (
            <div className="px-8">
                <h3 className="m-0 mb-2 text-base font-semibold" style={{ color: 'var(--vora-text-primary)' }}>
                    {config.name} <span style={{ color: 'var(--vora-text-muted)' }}>· {config.providerName || config.providerId}</span>
                </h3>
                <p className="m-0 text-sm" style={{ color: 'var(--vora-text-muted)' }}>No items right now.</p>
            </div>
        );
    }

    const providerLabel = config.providerName || config.providerId;

    return (
        <MediaRow
            title={config.name}
            subtitle={providerLabel}
            onMore={() => navigate(serverId ? `/server/${serverId}/discovery/${config.providerId}/row/${config.rowId}` : `/discovery/${config.providerId}/row/${config.rowId}`)}
        >
            {items.map(item => {
                const inWatchlist = watchlistIds.has(item.externalId);
                const statusBadge = (item.inLibrary || item.requestStatus)
                    ? <DiscoveryStatusBadge inLibrary={item.inLibrary} requestStatus={item.requestStatus} />
                    : undefined;
                return (
                    <MediaRowItem key={item.externalId}>
                        <MediaCard
                            imageUrl={item.posterUrl}
                            title={item.title}
                            captionLines={item.year ? [item.year.toString()] : []}
                            inWatchlist={inWatchlist}
                            onClick={() => navigate(serverId ? `/server/${serverId}/discovery/${config.providerId}/${item.type}/${item.externalId}` : `/discovery/${config.providerId}/${item.type}/${item.externalId}`)}
                            bottomLeftBadge={statusBadge}
                        />
                    </MediaRowItem>
                );
            })}
        </MediaRow>
    );
}
