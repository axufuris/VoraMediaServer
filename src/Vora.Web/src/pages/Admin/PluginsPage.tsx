import { useEffect, useState, useRef, useCallback, useMemo } from 'react';
import { useParams } from 'react-router-dom';
import { pluginAdminService, type PluginVM } from '../../api/System/pluginAdminService';
import { systemSettingsAdminService } from '../../api/System/systemSettingsAdminService';
import { useDialog } from '../../dialogs';
import PageHeader from '../../components/Admin/Primitives/PageHeader';
import HealthBadge from '../../components/Admin/Primitives/HealthBadge';
import EmptyState from '../../components/Admin/Primitives/EmptyState';
import PluginSettingsForm from '../../components/Admin/Settings/PluginSettingsForm';

interface PluginReleaseData {
    tag_name?: string;
    version?: string;
    Version?: string;
}

function formatTypeLabel(type: string): string {
    const withSpaces = type.replace(/_/g, ' ').replace(/([a-z])([A-Z])/g, '$1 $2');
    return withSpaces.replace(/\b\w/g, c => c.toUpperCase());
}

function PluginCard({
    plugin,
    serverId,
    showModal,
    onUninstall,
}: {
    plugin: PluginVM;
    serverId?: string;
    showModal: (title: string, message: string, isError?: boolean) => void;
    onUninstall: (id: string, name: string) => void;
}) {
    const [latestVersion, setLatestVersion] = useState<string | null>(null);
    const [checkingVersion, setCheckingVersion] = useState(!!plugin.latestVersionApiUrl);
    const [enabled, setEnabled] = useState(plugin.isEnabled);
    const [isToggling, setIsToggling] = useState(false);
    const [isExpanded, setIsExpanded] = useState(false);

    useEffect(() => {
        if (!plugin.latestVersionApiUrl) return;
        fetch(plugin.latestVersionApiUrl)
            .then(res => res.json() as Promise<PluginReleaseData>)
            .then(data => {
                const ver = data.tag_name || data.version || data.Version;
                if (ver) setLatestVersion(ver.replace(/^v/, ''));
            })
            .catch(err => console.warn(`Failed to fetch version for ${plugin.name}`, err))
            .finally(() => setCheckingVersion(false));
    }, [plugin.latestVersionApiUrl, plugin.name]);

    const isLatest = latestVersion && latestVersion === plugin.version;
    const hasUpdate = latestVersion && latestVersion !== plugin.version;

    const handleToggle = async () => {
        const next = !enabled;
        setEnabled(next);
        setIsToggling(true);
        try {
            await systemSettingsAdminService.updatePluginSettings(plugin.id, { is_enabled: next ? 'true' : 'false' }, serverId);
        } catch {
            setEnabled(!next);
            showModal('Error', `Failed to ${next ? 'enable' : 'disable'} ${plugin.name}.`, true);
        } finally {
            setIsToggling(false);
        }
    };

    const canExpand = plugin.hasSettings;

    return (
        <div className={`vora-card overflow-hidden transition-opacity ${enabled ? '' : 'opacity-60'}`}>
            <div
                className={`flex items-start gap-3 p-4 ${canExpand ? 'cursor-pointer hover:bg-[var(--vora-bg-sunken)]/40' : ''} transition-colors`}
                onClick={() => canExpand && setIsExpanded(e => !e)}
            >
                {canExpand ? (
                    <svg className={`w-4 h-4 mt-0.5 text-[var(--vora-text-muted)] shrink-0 transition-transform ${isExpanded ? 'rotate-90' : ''}`} fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 5l7 7-7 7" /></svg>
                ) : (
                    <span className="w-4 shrink-0" />
                )}

                <div className="flex-1 min-w-0">
                    <div className="flex flex-wrap items-center gap-2">
                        <span className="text-sm font-semibold text-[var(--vora-text-primary)]">{plugin.name}</span>
                        <span className="text-[11px] font-mono px-1.5 py-0.5 rounded border border-[var(--vora-border-subtle)] bg-[var(--vora-bg-sunken)] text-[var(--vora-text-secondary)]">v{plugin.version}</span>
                        {checkingVersion && <span className="text-[11px] text-[var(--vora-text-muted)] italic">Checking…</span>}
                        {isLatest && <HealthBadge tone="ok">Latest</HealthBadge>}
                        {hasUpdate && <HealthBadge tone="warn">Update to v{latestVersion}</HealthBadge>}
                        {plugin.isSystemPlugin && <HealthBadge tone="info" showDot={false}>System Core</HealthBadge>}
                    </div>
                    <p className="text-xs text-[var(--vora-text-secondary)] leading-relaxed mt-1.5">{plugin.description}</p>
                    {plugin.developerName && (
                        <p className="text-[11px] text-[var(--vora-text-muted)] mt-1">
                            By <span className="text-[var(--vora-text-secondary)] font-medium">{plugin.developerName}</span>
                        </p>
                    )}
                    {canExpand && !isExpanded && (
                        <span className="inline-block text-[11px] font-semibold text-[var(--vora-info-text)] mt-2">Configure settings →</span>
                    )}
                </div>

                <div className="flex flex-col items-end gap-2 shrink-0">
                    <button
                        type="button"
                        onClick={(e) => { e.stopPropagation(); handleToggle(); }}
                        disabled={isToggling}
                        className={`relative inline-flex h-6 w-11 shrink-0 items-center rounded-full transition-colors cursor-pointer disabled:opacity-50 ${enabled ? 'bg-[var(--vora-accent-500)]' : 'bg-[var(--vora-border-strong)]'}`}
                        aria-pressed={enabled}
                        title={enabled ? 'Enabled — click to disable' : 'Disabled — click to enable'}
                    >
                        <span className={`inline-block h-4 w-4 transform rounded-full bg-white transition-transform shadow-sm ${enabled ? 'translate-x-6' : 'translate-x-1'}`} />
                    </button>
                    <span className="text-[10px] font-semibold uppercase tracking-wider text-[var(--vora-text-muted)]">{enabled ? 'Enabled' : 'Disabled'}</span>
                </div>
            </div>

            {canExpand && isExpanded && (
                <div className="border-t border-[var(--vora-border-subtle)] p-4 bg-[var(--vora-bg-sunken)]/40">
                    <PluginSettingsForm
                        serverId={serverId}
                        pluginId={plugin.id}
                        pluginName={plugin.name}
                        showModal={showModal}
                    />
                </div>
            )}

            <div className="px-4 py-2.5 border-t border-[var(--vora-border-subtle)] flex justify-between items-center">
                {plugin.documentationUrl ? (
                    <a
                        href={plugin.documentationUrl}
                        target="_blank"
                        rel="noopener noreferrer"
                        className="text-xs font-semibold text-[var(--vora-info-text)] hover:text-[var(--vora-info-500)] flex items-center gap-1 transition-colors"
                    >
                        <svg className="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M10 6H6a2 2 0 00-2 2v10a2 2 0 002 2h10a2 2 0 002-2v-4M14 4h6m0 0v6m0-6L10 14" /></svg>
                        Docs
                    </a>
                ) : <span className="font-mono text-[10px] text-[var(--vora-text-disabled)] truncate">{plugin.id}</span>}

                {plugin.isSystemPlugin ? (
                    <span className="text-[11px] font-semibold text-[var(--vora-text-disabled)] uppercase tracking-wider">Cannot uninstall</span>
                ) : (
                    <button
                        type="button"
                        onClick={() => onUninstall(plugin.id, plugin.name)}
                        className="text-xs font-semibold text-[var(--vora-danger-text)] bg-[var(--vora-danger-soft)] hover:bg-[var(--vora-danger-500)] hover:text-[var(--vora-text-primary)] px-3 py-1 rounded-[var(--vora-radius-md)] transition-colors cursor-pointer"
                    >
                        Uninstall
                    </button>
                )}
            </div>
        </div>
    );
}

export default function PluginsPage() {
    const dialog = useDialog();
    const { serverId } = useParams<{ serverId?: string }>();
    const [plugins, setPlugins] = useState<PluginVM[]>([]);
    const [loading, setLoading] = useState(true);
    const [isUploading, setIsUploading] = useState(false);
    const [query, setQuery] = useState('');
    const fileInputRef = useRef<HTMLInputElement>(null);

    const showModal = useCallback((title: string, message: string, isError: boolean = false) => {
        dialog.alert({ title, message, tone: isError ? 'danger' : 'default' });
    }, [dialog]);

    const fetchPlugins = useCallback(() => {
        setLoading(true);
        pluginAdminService.getPlugins(serverId)
            .then(setPlugins)
            .catch(console.error)
            .finally(() => setLoading(false));
    }, [serverId]);

    useEffect(() => {
        fetchPlugins();
    }, [fetchPlugins]);

    const handleFileUpload = async (e: React.ChangeEvent<HTMLInputElement>) => {
        const file = e.target.files?.[0];
        if (!file) return;
        if (!file.name.endsWith('.dll')) {
            await dialog.alert('Please select a valid .dll plugin file.');
            return;
        }
        setIsUploading(true);
        try {
            await pluginAdminService.uploadPlugin(file, serverId);
            await dialog.alert('Plugin uploaded successfully. Restart the Vora server to load the new plugin.');
            if (fileInputRef.current) fileInputRef.current.value = '';
            fetchPlugins();
        } catch (error) {
            await dialog.alert('Failed to upload plugin.');
            console.error(error);
        } finally {
            setIsUploading(false);
        }
    };

    const handleUninstall = async (id: string, name: string) => {
        if (!await dialog.confirm(`Are you sure you want to uninstall ${name}?`)) return;
        try {
            await pluginAdminService.uninstallPlugin(id, serverId);
            await dialog.alert('Plugin scheduled for deletion. Restart the Vora server to complete the uninstall.');
            setPlugins(plugins.filter(p => p.id !== id));
        } catch (error) {
            await dialog.alert('Failed to uninstall plugin.');
            console.error(error);
        }
    };

    const filtered = useMemo(() => {
        const q = query.trim().toLowerCase();
        if (!q) return plugins;
        return plugins.filter(p =>
            p.name.toLowerCase().includes(q) ||
            p.id.toLowerCase().includes(q) ||
            p.type.toLowerCase().includes(q) ||
            formatTypeLabel(p.type).toLowerCase().includes(q) ||
            (p.description?.toLowerCase().includes(q) ?? false) ||
            (p.developerName?.toLowerCase().includes(q) ?? false)
        );
    }, [plugins, query]);

    const groupedPlugins = useMemo(() => {
        const acc: Record<string, PluginVM[]> = {};
        for (const plugin of filtered) {
            const group = plugin.type || 'Other';
            (acc[group] ??= []).push(plugin);
        }
        for (const key of Object.keys(acc)) {
            acc[key].sort((a, b) => a.name.localeCompare(b.name));
        }
        return acc;
    }, [filtered]);

    const groupNames = Object.keys(groupedPlugins).sort((a, b) => formatTypeLabel(a).localeCompare(formatTypeLabel(b)));

    return (
        <div data-vora-page="">
            <PageHeader
                title="Plugins"
                description="Every metadata agent, artwork source, scanner, ratings provider, and integration in one place — enable, configure, and manage them all here."
                actions={
                    <>
                        <input type="file" ref={fileInputRef} onChange={handleFileUpload} accept=".dll" className="hidden" />
                        <button
                            type="button"
                            onClick={() => fileInputRef.current?.click()}
                            disabled={isUploading}
                            className="vora-button-primary flex items-center gap-2"
                        >
                            <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 6v6m0 0v6m0-6h6m-6 0H6" /></svg>
                            {isUploading ? 'Uploading…' : 'Upload .dll'}
                        </button>
                    </>
                }
            />

            <div className="p-8 max-w-5xl mx-auto space-y-8">
                {!loading && plugins.length > 0 && (
                    <div className="relative">
                        <svg className="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-[var(--vora-text-muted)] pointer-events-none" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M21 21l-4.35-4.35M11 19a8 8 0 100-16 8 8 0 000 16z" /></svg>
                        <input
                            type="text"
                            value={query}
                            onChange={e => setQuery(e.target.value)}
                            placeholder="Search plugins by name, type, key, or description (e.g. OMDb, ratings, showtimes)…"
                            className="vora-input w-full pl-9"
                        />
                    </div>
                )}

                {loading ? (
                    <div className="space-y-4">
                        {[1, 2, 3, 4, 5].map(i => <div key={i} className="vora-skeleton h-28" />)}
                    </div>
                ) : plugins.length === 0 ? (
                    <div className="vora-card">
                        <EmptyState
                            title="No plugins installed"
                            description="Upload a .dll plugin to extend Vora with new providers, scanners, or integrations."
                        />
                    </div>
                ) : groupNames.length === 0 ? (
                    <div className="vora-card">
                        <EmptyState
                            title="No matching plugins"
                            description={`Nothing matches “${query}”. Try a different search.`}
                        />
                    </div>
                ) : (
                    groupNames.map(type => (
                        <section key={type} className="space-y-3">
                            <h2 className="text-xs font-bold text-[var(--vora-text-muted)] uppercase tracking-widest">
                                {formatTypeLabel(type)}
                                <span className="ml-2 text-[var(--vora-text-disabled)] font-medium normal-case tracking-normal">· {groupedPlugins[type].length}</span>
                            </h2>
                            <div className="space-y-3">
                                {groupedPlugins[type].map(plugin => (
                                    <PluginCard
                                        key={plugin.id}
                                        plugin={plugin}
                                        serverId={serverId}
                                        showModal={showModal}
                                        onUninstall={handleUninstall}
                                    />
                                ))}
                            </div>
                        </section>
                    ))
                )}
            </div>
        </div>
    );
}
