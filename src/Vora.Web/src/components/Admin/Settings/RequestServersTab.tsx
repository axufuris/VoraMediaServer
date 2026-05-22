import { useEffect, useState, useCallback } from 'react';
import { requestAdminService, type RequestServerVM, type ProviderOptionDto } from '../../../api/Discovery/requestAdminService';
import { isAxiosError } from 'axios';
import { useDialog } from '../../../dialogs';
import EntityCard from '../Primitives/EntityCard';
import HealthBadge from '../Primitives/HealthBadge';
import EmptyState from '../Primitives/EmptyState';

interface RequestServersTabProps {
    serverId?: string;
    showModal: (title: string, message: string, isError?: boolean) => void;
}

function FieldLabel({ children }: { children: React.ReactNode }) {
    return <label className="block text-xs font-bold uppercase tracking-widest text-[var(--vora-text-muted)] mb-1.5">{children}</label>;
}

export default function RequestServersTab({ serverId, showModal }: RequestServersTabProps) {
    const dialog = useDialog();
    const [requestServers, setRequestServers] = useState<RequestServerVM[]>([]);
    const [editingRequestServer, setEditingRequestServer] = useState<RequestServerVM | null>(null);
    const [dynamicSettings, setDynamicSettings] = useState<Record<string, string | number | boolean>>({});
    const [qualityProfiles, setQualityProfiles] = useState<ProviderOptionDto[]>([]);
    const [rootFolders, setRootFolders] = useState<ProviderOptionDto[]>([]);
    const [isTestingServer, setIsTestingServer] = useState(false);
    const [isSaving, setIsSaving] = useState(false);

    const loadRequestServers = useCallback(async () => {
        try {
            const data = await requestAdminService.getServers(serverId);
            setRequestServers(data);
        } catch {
            showModal('Error', 'Failed to load request servers.', true);
        }
    }, [serverId, showModal]);

    useEffect(() => {
        loadRequestServers();
    }, [loadRequestServers]);

    const handleAddRequestServer = () => {
        setEditingRequestServer({
            name: 'New Server',
            providerId: 'radarr_requester',
            mediaType: 'Movie',
            hostname: '192.168.',
            port: 7878,
            useSsl: false,
            apiKey: '',
            urlBase: '',
            isDefault: true,
            is4K: false,
            isEnabled: true,
            providesReleaseCalendar: false,
            providerSettingsJson: '{}',
        });
        setDynamicSettings({ minimumAvailability: 'announced', searchOnAdd: true });
        setQualityProfiles([]);
        setRootFolders([]);
    };

    const handleEditRequestServer = async (server: RequestServerVM) => {
        setEditingRequestServer({ ...server });
        try {
            setDynamicSettings(JSON.parse(server.providerSettingsJson || '{}'));
        } catch {
            setDynamicSettings({});
        }
        setQualityProfiles([]);
        setRootFolders([]);

        if (server.hostname && server.apiKey) {
            setIsTestingServer(true);
            try {
                const baseReq = {
                    providerId: server.providerId,
                    hostname: server.hostname,
                    port: server.port,
                    useSsl: server.useSsl,
                    apiKey: server.apiKey,
                    urlBase: server.urlBase,
                };
                const profiles = await requestAdminService.getProviderOptions({ ...baseReq, optionType: 'qualityProfiles' }, serverId);
                const folders = await requestAdminService.getProviderOptions({ ...baseReq, optionType: 'rootFolders' }, serverId);
                setQualityProfiles(profiles);
                setRootFolders(folders);
            } catch (err: unknown) {
                let errorMsg = "Could not auto-fetch options. Click 'Test connection' to see why.";
                if (isAxiosError(err)) errorMsg = err.response?.data?.message || errorMsg;
                else if (err instanceof Error) errorMsg = err.message;
                showModal('Warning', errorMsg, true);
            } finally {
                setIsTestingServer(false);
            }
        }
    };

    const handleDeleteRequestServer = async (id: string) => {
        if (!await dialog.confirm('Are you sure you want to remove this request server?')) return;
        try {
            await requestAdminService.deleteServer(id, serverId);
            loadRequestServers();
        } catch {
            showModal('Error', 'Failed to delete server.', true);
        }
    };

    const handleTestRequestServer = async () => {
        if (!editingRequestServer) return;
        setIsTestingServer(true);
        try {
            const baseReq = {
                providerId: editingRequestServer.providerId,
                hostname: editingRequestServer.hostname,
                port: editingRequestServer.port,
                useSsl: editingRequestServer.useSsl,
                apiKey: editingRequestServer.apiKey,
                urlBase: editingRequestServer.urlBase,
            };

            const profiles = await requestAdminService.getProviderOptions({ ...baseReq, optionType: 'qualityProfiles' }, serverId);
            const folders = await requestAdminService.getProviderOptions({ ...baseReq, optionType: 'rootFolders' }, serverId);

            setQualityProfiles(profiles);
            setRootFolders(folders);

            setDynamicSettings(prev => {
                let newProfileId = typeof prev.qualityProfileId === 'number' ? prev.qualityProfileId : 0;
                let newRootPath = typeof prev.rootFolderPath === 'string' ? prev.rootFolderPath : '';

                if (!profiles.some(p => parseInt(p.id) === newProfileId)) {
                    newProfileId = profiles.length > 0 ? parseInt(profiles[0].id) : 0;
                }
                if (!folders.some(f => f.name === newRootPath)) {
                    newRootPath = folders.length > 0 ? folders[0].name : '';
                }

                return { ...prev, qualityProfileId: newProfileId, rootFolderPath: newRootPath };
            });

            showModal('Success', 'Connection successful — settings populated.');
        } catch (err: unknown) {
            let errorMsg = 'Failed to connect. Please check your URL and API Key.';
            if (isAxiosError(err)) errorMsg = err.response?.data?.message || errorMsg;
            else if (err instanceof Error) errorMsg = err.message;
            showModal('Connection failed', errorMsg, true);
        } finally {
            setIsTestingServer(false);
        }
    };

    const handleSaveRequestServer = async (e: React.SyntheticEvent) => {
        e.preventDefault();
        if (!editingRequestServer) return;
        setIsSaving(true);
        try {
            const serverToSave = {
                ...editingRequestServer,
                providerSettingsJson: JSON.stringify(dynamicSettings),
            };
            await requestAdminService.saveServer(serverToSave, serverId);
            setEditingRequestServer(null);
            loadRequestServers();
            showModal('Success', 'Request server saved.');
        } catch {
            showModal('Error', 'Failed to save request server.', true);
        } finally {
            setIsSaving(false);
        }
    };

    if (!editingRequestServer) {
        return (
            <div className="pt-2">
                <div className="flex items-end justify-between mb-4">
                    <p className="text-sm text-[var(--vora-text-muted)]">
                        Connect Radarr or Sonarr to process user watchlist requests automatically.
                    </p>
                    <button type="button" onClick={handleAddRequestServer} className="vora-button-primary text-sm">
                        Add server
                    </button>
                </div>

                {requestServers.length === 0 ? (
                    <div className="vora-card">
                        <EmptyState
                            title="No request servers configured"
                            description='Click "Add server" above to connect Radarr or Sonarr.'
                        />
                    </div>
                ) : (
                    <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                        {requestServers.map(server => (
                            <EntityCard
                                key={server.id}
                                title={server.name}
                                subtitle={`${server.useSsl ? 'https://' : 'http://'}${server.hostname}:${server.port}`}
                                badge={
                                    <div className="flex gap-1.5 flex-wrap justify-end">
                                        <HealthBadge tone="neutral" showDot={false}>{server.mediaType}</HealthBadge>
                                        {server.isDefault && <HealthBadge tone="info" showDot={false}>Default</HealthBadge>}
                                        {server.is4K && <HealthBadge tone="warn" showDot={false}>4K</HealthBadge>}
                                        {server.providesReleaseCalendar && <HealthBadge tone="info" showDot={false}>Calendar</HealthBadge>}
                                        <HealthBadge tone={server.isEnabled ? 'ok' : 'error'}>{server.isEnabled ? 'On' : 'Off'}</HealthBadge>
                                    </div>
                                }
                                footer={
                                    <div className="flex justify-end gap-3 text-xs font-semibold">
                                        <button type="button" onClick={() => handleEditRequestServer(server)} className="text-[var(--vora-accent-text)] hover:text-[var(--vora-accent-active)] cursor-pointer">Edit</button>
                                        <button type="button" onClick={() => handleDeleteRequestServer(server.id!)} className="text-[var(--vora-danger-text)] hover:text-[var(--vora-danger-500)] cursor-pointer">Delete</button>
                                    </div>
                                }
                            />
                        ))}
                    </div>
                )}
            </div>
        );
    }

    return (
        <div className="pt-2">
            <div className="flex items-center gap-3 mb-6">
                <button type="button" onClick={() => setEditingRequestServer(null)} className="text-sm font-semibold text-[var(--vora-text-secondary)] hover:text-[var(--vora-text-primary)] transition-colors cursor-pointer flex items-center gap-1">
                    <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15 19l-7-7 7-7" /></svg>
                    Back
                </button>
                <span className="text-sm font-semibold text-[var(--vora-text-primary)]">
                    {editingRequestServer.id ? 'Edit request server' : 'Add request server'}
                </span>
            </div>

            <form onSubmit={handleSaveRequestServer} className="space-y-6">
                <section className="vora-card p-6">
                    <h3 className="text-base font-semibold text-[var(--vora-text-primary)] mb-4">Connection</h3>
                    <div className="space-y-4">
                        <div className="flex gap-4">
                            <div className="flex-1">
                                <FieldLabel>Server name *</FieldLabel>
                                <input
                                    required
                                    type="text"
                                    value={editingRequestServer.name}
                                    onChange={e => setEditingRequestServer({ ...editingRequestServer, name: e.target.value })}
                                    className="vora-input"
                                    placeholder="e.g. Movies 1080p"
                                />
                            </div>
                            <div className="w-48">
                                <FieldLabel>Provider *</FieldLabel>
                                <select
                                    value={editingRequestServer.providerId}
                                    onChange={e => setEditingRequestServer({ ...editingRequestServer, providerId: e.target.value, mediaType: e.target.value === 'sonarr_requester' ? 'TvShow' : 'Movie' })}
                                    className="vora-input cursor-pointer"
                                >
                                    <option value="radarr_requester">Radarr</option>
                                    <option value="sonarr_requester">Sonarr</option>
                                </select>
                            </div>
                        </div>

                        <div className="flex gap-4">
                            <div className="w-24">
                                <FieldLabel>Protocol</FieldLabel>
                                <select
                                    value={editingRequestServer.useSsl ? 'https' : 'http'}
                                    onChange={e => setEditingRequestServer({ ...editingRequestServer, useSsl: e.target.value === 'https' })}
                                    className="vora-input cursor-pointer"
                                >
                                    <option value="http">HTTP</option>
                                    <option value="https">HTTPS</option>
                                </select>
                            </div>
                            <div className="flex-1">
                                <FieldLabel>Hostname or IP *</FieldLabel>
                                <input
                                    required
                                    type="text"
                                    value={editingRequestServer.hostname}
                                    onChange={e => setEditingRequestServer({ ...editingRequestServer, hostname: e.target.value })}
                                    className="vora-input font-mono text-sm"
                                    placeholder="192.168.1.50"
                                />
                            </div>
                            <div className="w-24">
                                <FieldLabel>Port *</FieldLabel>
                                <input
                                    required
                                    type="number"
                                    value={editingRequestServer.port}
                                    onChange={e => setEditingRequestServer({ ...editingRequestServer, port: parseInt(e.target.value) || 0 })}
                                    className="vora-input font-mono text-sm"
                                />
                            </div>
                        </div>

                        <div className="flex gap-4">
                            <div className="flex-1">
                                <FieldLabel>API Key *</FieldLabel>
                                <input
                                    required
                                    type="password"
                                    value={editingRequestServer.apiKey}
                                    onChange={e => setEditingRequestServer({ ...editingRequestServer, apiKey: e.target.value })}
                                    className="vora-input font-mono text-sm tracking-widest"
                                />
                            </div>
                            <div className="flex-1">
                                <FieldLabel>URL Base</FieldLabel>
                                <input
                                    type="text"
                                    value={editingRequestServer.urlBase}
                                    onChange={e => setEditingRequestServer({ ...editingRequestServer, urlBase: e.target.value })}
                                    className="vora-input font-mono text-sm"
                                    placeholder="/radarr"
                                />
                            </div>
                        </div>

                        <button
                            type="button"
                            onClick={handleTestRequestServer}
                            disabled={isTestingServer}
                            className="vora-button-secondary text-sm"
                        >
                            {isTestingServer ? 'Testing…' : 'Test connection & fetch options'}
                        </button>
                    </div>
                </section>

                <section className="vora-card p-6">
                    <h3 className="text-base font-semibold text-[var(--vora-text-primary)] mb-4">Request defaults</h3>
                    <div className="space-y-5">
                        <label className="flex items-center gap-3 cursor-pointer group select-none pb-5 border-b border-[var(--vora-border-subtle)]">
                            <input
                                type="checkbox"
                                checked={editingRequestServer.isEnabled}
                                onChange={e => setEditingRequestServer({ ...editingRequestServer, isEnabled: e.target.checked })}
                                className="w-4 h-4 accent-[var(--vora-accent-500)] cursor-pointer"
                            />
                            <span className="text-sm font-semibold text-[var(--vora-text-primary)]">Enable server connection</span>
                        </label>

                        <div className="grid grid-cols-2 gap-4">
                            <label className="flex items-center gap-3 cursor-pointer group select-none">
                                <input
                                    type="checkbox"
                                    checked={editingRequestServer.isDefault}
                                    onChange={e => setEditingRequestServer({ ...editingRequestServer, isDefault: e.target.checked })}
                                    className="w-4 h-4 accent-[var(--vora-accent-500)] cursor-pointer"
                                />
                                <span className="text-sm text-[var(--vora-text-primary)]">Default server</span>
                            </label>
                            <label className="flex items-center gap-3 cursor-pointer group select-none">
                                <input
                                    type="checkbox"
                                    checked={editingRequestServer.is4K}
                                    onChange={e => setEditingRequestServer({ ...editingRequestServer, is4K: e.target.checked })}
                                    className="w-4 h-4 accent-[var(--vora-accent-500)] cursor-pointer"
                                />
                                <span className="text-sm text-[var(--vora-text-primary)]">4K server</span>
                            </label>
                            <label className="flex items-center gap-3 cursor-pointer group select-none col-span-2">
                                <input
                                    type="checkbox"
                                    checked={editingRequestServer.providesReleaseCalendar}
                                    onChange={e => setEditingRequestServer({ ...editingRequestServer, providesReleaseCalendar: e.target.checked })}
                                    className="w-4 h-4 accent-[var(--vora-accent-500)] cursor-pointer mt-0.5"
                                />
                                <span className="min-w-0">
                                    <span className="block text-sm text-[var(--vora-text-primary)]">Use for Release Calendar</span>
                                    <span className="block text-xs text-[var(--vora-text-muted)]">When on, the Release Calendar pulls upcoming releases from this server. You can leave “Enable server connection” off if you only want this server for calendar data.</span>
                                </span>
                            </label>
                        </div>

                        {(qualityProfiles.length > 0 || dynamicSettings.qualityProfileId !== undefined) && (
                            <div>
                                <FieldLabel>Quality profile *</FieldLabel>
                                <select
                                    value={(dynamicSettings.qualityProfileId as number) || ''}
                                    onChange={e => setDynamicSettings({ ...dynamicSettings, qualityProfileId: parseInt(e.target.value) })}
                                    className="vora-input max-w-md cursor-pointer"
                                >
                                    {qualityProfiles.length === 0 && (
                                        <option value={(dynamicSettings.qualityProfileId as number) || ''}>
                                            Saved ID: {dynamicSettings.qualityProfileId as number} (test to load names)
                                        </option>
                                    )}
                                    {qualityProfiles.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}
                                </select>
                            </div>
                        )}

                        {(rootFolders.length > 0 || dynamicSettings.rootFolderPath !== undefined) && (
                            <div>
                                <FieldLabel>Root folder *</FieldLabel>
                                <select
                                    value={(dynamicSettings.rootFolderPath as string) || ''}
                                    onChange={e => setDynamicSettings({ ...dynamicSettings, rootFolderPath: e.target.value })}
                                    className="vora-input max-w-md cursor-pointer"
                                >
                                    {rootFolders.length === 0 && (
                                        <option value={(dynamicSettings.rootFolderPath as string) || ''}>
                                            Saved path: {dynamicSettings.rootFolderPath as string} (test to load names)
                                        </option>
                                    )}
                                    {rootFolders.map(f => <option key={f.id} value={f.name}>{f.name}</option>)}
                                </select>
                            </div>
                        )}

                        {editingRequestServer.providerId === 'radarr_requester' && (
                            <div>
                                <FieldLabel>Minimum availability *</FieldLabel>
                                <select
                                    value={(dynamicSettings.minimumAvailability as string) || 'announced'}
                                    onChange={e => setDynamicSettings({ ...dynamicSettings, minimumAvailability: e.target.value })}
                                    className="vora-input max-w-md cursor-pointer"
                                >
                                    <option value="announced">Announced</option>
                                    <option value="inCinemas">In Cinemas</option>
                                    <option value="released">Released</option>
                                    <option value="preDB">PreDB</option>
                                </select>
                            </div>
                        )}

                        <label className="flex items-center gap-3 cursor-pointer group select-none pt-2">
                            <input
                                type="checkbox"
                                checked={(dynamicSettings.searchOnAdd as boolean) ?? true}
                                onChange={e => setDynamicSettings({ ...dynamicSettings, searchOnAdd: e.target.checked })}
                                className="w-4 h-4 accent-[var(--vora-accent-500)] cursor-pointer"
                            />
                            <span className="text-sm text-[var(--vora-text-primary)]">Enable automatic search</span>
                        </label>
                    </div>
                </section>

                <button type="submit" disabled={isSaving} className="vora-button-primary">
                    {isSaving ? 'Saving…' : 'Save request server'}
                </button>
            </form>
        </div>
    );
}
