import { useEffect, useState, useCallback } from 'react';
import { systemSettingsAdminService, type PluginSettingField } from '../../../api/System/systemSettingsAdminService';
import { type PluginVM } from '../../../api/System/pluginAdminService';
import FolderPathInput from '../FolderBrowser/FolderPathInput';

interface PluginSettingsTabProps {
    serverId?: string;
    type: string;
    plugins: PluginVM[];
    showModal: (title: string, message: string, isError?: boolean) => void;
}

export function PluginSection({ serverId, plugin, showModal }: { serverId?: string, plugin: PluginVM, showModal: (title: string, message: string, isError?: boolean) => void }) {
    const [pluginFields, setPluginFields] = useState<PluginSettingField[]>([]);
    const [pluginValues, setPluginValues] = useState<Record<string, string>>({});
    const [isSaving, setIsSaving] = useState(false);
    const [isTogglingEnabled, setIsTogglingEnabled] = useState(false);
    const [isLoading, setIsLoading] = useState(true);
    const [isExpanded, setIsExpanded] = useState(false);

    const loadPluginSettings = useCallback(async () => {
        try {
            const fields = await systemSettingsAdminService.getPluginSettings(plugin.id, serverId);
            setPluginFields(fields);
            const valuesMap: Record<string, string> = {};
            fields.forEach(f => valuesMap[f.key] = f.value);
            setPluginValues(valuesMap);
        } catch {
            showModal('Error', `Failed to load settings for ${plugin.name}.`, true);
        } finally {
            setIsLoading(false);
        }
    }, [plugin.id, plugin.name, serverId, showModal]);

    useEffect(() => {
        loadPluginSettings();
    }, [loadPluginSettings]);

    const configurableFields = pluginFields.filter(f => f.key !== 'is_enabled');
    const isEnabled = pluginValues['is_enabled'] !== 'false';
    const hasMoreSettings = configurableFields.length > 0;

    const handleToggleEnabled = async (next: boolean) => {
        const previous = pluginValues['is_enabled'];
        setPluginValues(prev => ({ ...prev, is_enabled: next ? 'true' : 'false' }));
        setIsTogglingEnabled(true);
        try {
            await systemSettingsAdminService.updatePluginSettings(plugin.id, { is_enabled: next ? 'true' : 'false' }, serverId);
        } catch {
            setPluginValues(prev => ({ ...prev, is_enabled: previous ?? 'true' }));
            showModal('Error', `Failed to toggle ${plugin.name}.`, true);
        } finally {
            setIsTogglingEnabled(false);
        }
    };

    const handleSavePlugin = async (e: React.SyntheticEvent<HTMLFormElement>) => {
        e.preventDefault();
        setIsSaving(true);
        try {
            const payload: Record<string, string> = {};
            configurableFields.forEach(f => { payload[f.key] = pluginValues[f.key] ?? ''; });
            await systemSettingsAdminService.updatePluginSettings(plugin.id, payload, serverId);
            showModal('Success', `${plugin.name} settings saved.`);
        } catch {
            showModal('Error', `Failed to save settings for ${plugin.name}.`, true);
        } finally {
            setIsSaving(false);
        }
    };

    const handlePluginValueChange = (key: string, value: string) => {
        setPluginValues(prev => ({ ...prev, [key]: value }));
    };

    if (isLoading) return <div className="vora-skeleton h-12" />;

    return (
        <div className="bg-[var(--vora-bg-surface)] rounded-[var(--vora-radius-md)] border border-[var(--vora-border-subtle)] overflow-hidden">
            <div
                className={`flex items-center gap-3 px-4 py-3 ${hasMoreSettings ? 'cursor-pointer hover:bg-[var(--vora-bg-sunken)]/50' : ''} transition-colors`}
                onClick={() => hasMoreSettings && setIsExpanded(e => !e)}
            >
                {hasMoreSettings ? (
                    <svg className={`w-3.5 h-3.5 text-[var(--vora-text-muted)] shrink-0 transition-transform ${isExpanded ? 'rotate-90' : ''}`} fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 5l7 7-7 7" /></svg>
                ) : (
                    <span className="w-3.5 shrink-0" />
                )}
                <div className="flex-1 min-w-0">
                    <div className="text-sm font-semibold text-[var(--vora-text-primary)] truncate">{plugin.name}</div>
                    <div className="text-[10px] text-[var(--vora-text-muted)] font-mono truncate">{plugin.id}</div>
                </div>
                <button
                    type="button"
                    onClick={(e) => { e.stopPropagation(); handleToggleEnabled(!isEnabled); }}
                    disabled={isTogglingEnabled}
                    className={`relative inline-flex h-6 w-11 shrink-0 items-center rounded-full transition-colors cursor-pointer disabled:opacity-50 ${isEnabled ? 'bg-[var(--vora-accent-500)]' : 'bg-[var(--vora-border-strong)]'}`}
                    aria-pressed={isEnabled}
                    title={isEnabled ? 'Click to disable' : 'Click to enable'}
                >
                    <span className={`inline-block h-4 w-4 transform rounded-full bg-white transition-transform shadow-sm ${isEnabled ? 'translate-x-6' : 'translate-x-1'}`} />
                </button>
            </div>

            {hasMoreSettings && isExpanded && (
                <form onSubmit={handleSavePlugin} className="border-t border-[var(--vora-border-subtle)] p-4 space-y-4 bg-[var(--vora-bg-sunken)]/40">
                    {configurableFields.map(field => (
                        <div key={field.key}>
                            <label className="block text-xs font-bold text-[var(--vora-text-secondary)] mb-0.5">{field.label}</label>
                            {field.description && <p className="text-[11px] text-[var(--vora-text-muted)] mb-1.5">{field.description}</p>}
                            {field.type === 'boolean' || field.type === 'checkbox' ? (
                                <label className="flex items-center gap-2 cursor-pointer w-max">
                                    <input
                                        type="checkbox"
                                        checked={pluginValues[field.key] === 'true'}
                                        onChange={e => handlePluginValueChange(field.key, e.target.checked ? 'true' : 'false')}
                                        className="w-4 h-4 accent-[var(--vora-accent-500)] cursor-pointer"
                                    />
                                    <span className="text-xs text-[var(--vora-text-secondary)] font-medium">{pluginValues[field.key] === 'true' ? 'Enabled' : 'Disabled'}</span>
                                </label>
                            ) : field.type === 'folder' || field.type === 'directory' || field.type === 'path' ? (
                                <FolderPathInput
                                    value={pluginValues[field.key] || ''}
                                    onChange={v => handlePluginValueChange(field.key, v)}
                                    serverId={serverId}
                                    modalTitle={`Select folder for ${field.label}`}
                                />
                            ) : (
                                <input
                                    type={field.type}
                                    value={pluginValues[field.key] || ''}
                                    onChange={e => handlePluginValueChange(field.key, e.target.value)}
                                    className="vora-input text-sm"
                                />
                            )}
                        </div>
                    ))}
                    <div className="pt-1">
                        <button type="submit" disabled={isSaving} className="vora-button-primary text-xs !py-1.5">
                            {isSaving ? 'Saving…' : 'Save settings'}
                        </button>
                    </div>
                </form>
            )}
        </div>
    );
}

export default function PluginSettingsTab({ serverId, type, plugins, showModal }: PluginSettingsTabProps) {
    const sortedPlugins = [...plugins].sort((a, b) => {
        const aIsLocal = a.name.toLowerCase().includes('local');
        const bIsLocal = b.name.toLowerCase().includes('local');

        if (aIsLocal && !bIsLocal) return -1;
        if (!aIsLocal && bIsLocal) return 1;

        return a.name.localeCompare(b.name);
    });

    return (
        <div className="max-w-4xl">
            <h1 className="text-2xl font-semibold text-[var(--vora-text-primary)] mb-1 tracking-tight">{type.replace(/_/g, ' ')} Plugins</h1>
            <p className="text-sm text-[var(--vora-text-secondary)] mb-6 pb-4 border-b border-[var(--vora-border-subtle)]">
                Configure settings and API keys for all {type.replace(/_/g, ' ')} extensions.
            </p>

            <div className="space-y-2">
                {sortedPlugins.map(plugin => (
                    <PluginSection key={plugin.id} serverId={serverId} plugin={plugin} showModal={showModal} />
                ))}
            </div>
        </div>
    );
}
