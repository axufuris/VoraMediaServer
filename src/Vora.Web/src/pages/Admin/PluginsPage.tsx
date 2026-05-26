import { useEffect, useState, useRef, useCallback } from 'react';
import { useParams } from 'react-router-dom';
import { pluginAdminService, type PluginVM } from '../../api/System/pluginAdminService';
import { useDialog } from '../../dialogs';
import PageHeader from '../../components/Admin/Primitives/PageHeader';
import EntityCard from '../../components/Admin/Primitives/EntityCard';
import HealthBadge from '../../components/Admin/Primitives/HealthBadge';
import EmptyState from '../../components/Admin/Primitives/EmptyState';

interface PluginReleaseData {
    tag_name?: string;
    version?: string;
    Version?: string;
}

function PluginCard({ plugin, onUninstall }: { plugin: PluginVM, onUninstall: (id: string, name: string) => void }) {
    const [latestVersion, setLatestVersion] = useState<string | null>(null);
    const [checkingVersion, setCheckingVersion] = useState(!!plugin.latestVersionApiUrl);

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

    const badge = plugin.isSystemPlugin
        ? <HealthBadge tone="info" showDot={false}>System Core</HealthBadge>
        : null;

    return (
        <EntityCard title={plugin.name} badge={badge}>
            <div className="flex flex-wrap items-center gap-2 mb-3">
                <span className="text-[11px] font-mono px-2 py-0.5 rounded border border-[var(--vora-border-subtle)] bg-[var(--vora-bg-sunken)] text-[var(--vora-text-secondary)]">
                    v{plugin.version}
                </span>
                {checkingVersion && <span className="text-[11px] text-[var(--vora-text-muted)] italic">Checking…</span>}
                {isLatest && <HealthBadge tone="ok">Latest</HealthBadge>}
                {hasUpdate && <HealthBadge tone="warn">Update to v{latestVersion}</HealthBadge>}
            </div>

            {plugin.developerName && (
                <p className="text-xs text-[var(--vora-text-muted)] mb-2">
                    By <span className="text-[var(--vora-text-secondary)] font-medium">{plugin.developerName}</span>
                </p>
            )}

            <p className="text-sm text-[var(--vora-text-secondary)] leading-relaxed">{plugin.description}</p>

            <div className="mt-4 pt-3 border-t border-[var(--vora-border-subtle)] flex justify-between items-center">
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
                ) : <span />}

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
        </EntityCard>
    );
}

function formatTypeLabel(type: string): string {
    const withSpaces = type.replace(/_/g, ' ').replace(/([a-z])([A-Z])/g, '$1 $2');
    return withSpaces.replace(/\b\w/g, c => c.toUpperCase());
}

export default function PluginsPage() {
    const dialog = useDialog();
    const { serverId } = useParams<{ serverId?: string }>();
    const [plugins, setPlugins] = useState<PluginVM[]>([]);
    const [loading, setLoading] = useState(true);
    const [isUploading, setIsUploading] = useState(false);
    const fileInputRef = useRef<HTMLInputElement>(null);

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

    const groupedPlugins = plugins.reduce((acc, plugin) => {
        const group = plugin.type || 'Other';
        if (!acc[group]) acc[group] = [];
        acc[group].push(plugin);
        return acc;
    }, {} as Record<string, PluginVM[]>);

    return (
        <div data-vora-page="">
            <PageHeader
                title="Plugin Management"
                description="Extend Vora with third-party metadata agents, scanners, providers, and tools."
                actions={
                    <>
                        <input
                            type="file"
                            ref={fileInputRef}
                            onChange={handleFileUpload}
                            accept=".dll"
                            className="hidden"
                        />
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

            <div className="p-8 max-w-7xl mx-auto space-y-10">
                {loading ? (
                    <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-4">
                        {[1, 2, 3, 4, 5, 6].map(i => <div key={i} className="vora-skeleton h-44" />)}
                    </div>
                ) : plugins.length === 0 ? (
                    <div className="vora-card">
                        <EmptyState
                            title="No plugins installed"
                            description="Upload a .dll plugin to extend Vora with new providers, scanners, or integrations."
                        />
                    </div>
                ) : (
                    Object.entries(groupedPlugins).map(([type, typePlugins]) => (
                        <section key={type} className="space-y-3">
                            <h2 className="text-xs font-bold text-[var(--vora-text-muted)] uppercase tracking-widest">
                                {formatTypeLabel(type)}
                                <span className="ml-2 text-[var(--vora-text-disabled)] font-medium normal-case tracking-normal">· {typePlugins.length}</span>
                            </h2>
                            <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-4">
                                {typePlugins.map(plugin => (
                                    <PluginCard
                                        key={plugin.id}
                                        plugin={plugin}
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
