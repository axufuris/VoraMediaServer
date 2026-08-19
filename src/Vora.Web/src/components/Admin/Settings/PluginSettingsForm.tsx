import { useEffect, useState, useCallback, type ReactNode } from 'react';
import { systemSettingsAdminService, type PluginSettingField } from '../../../api/System/systemSettingsAdminService';
import FolderPathInput from '../FolderBrowser/FolderPathInput';

const URL_PATTERN = /https?:\/\/[^\s)]+[^\s).,;:!?]/g;

function renderDescriptionWithLinks(text: string): ReactNode {
    const parts: ReactNode[] = [];
    let lastIndex = 0;
    let match: RegExpExecArray | null;
    URL_PATTERN.lastIndex = 0;
    while ((match = URL_PATTERN.exec(text)) !== null) {
        if (match.index > lastIndex) {
            parts.push(text.substring(lastIndex, match.index));
        }
        parts.push(
            <a
                key={`link-${match.index}`}
                href={match[0]}
                target="_blank"
                rel="noreferrer"
                className="underline hover:text-[var(--vora-accent-500)]"
            >
                {match[0]}
            </a>
        );
        lastIndex = match.index + match[0].length;
    }
    if (lastIndex < text.length) {
        parts.push(text.substring(lastIndex));
    }
    return parts;
}

interface PluginSettingsFormProps {
    serverId?: string;
    pluginId: string;
    pluginName: string;
    showModal: (title: string, message: string, isError?: boolean) => void;
    onAfterChange?: () => void;
}

export default function PluginSettingsForm({ serverId, pluginId, pluginName, showModal, onAfterChange }: PluginSettingsFormProps) {
    const [fields, setFields] = useState<PluginSettingField[]>([]);
    const [values, setValues] = useState<Record<string, string>>({});
    const [isSaving, setIsSaving] = useState(false);
    const [isLoading, setIsLoading] = useState(true);

    const loadSettings = useCallback(async () => {
        try {
            const loaded = await systemSettingsAdminService.getPluginSettings(pluginId, serverId);
            setFields(loaded);
            const valuesMap: Record<string, string> = {};
            loaded.forEach(f => valuesMap[f.key] = f.value);
            setValues(valuesMap);
        } catch {
            showModal('Error', `Failed to load settings for ${pluginName}.`, true);
        } finally {
            setIsLoading(false);
        }
    }, [pluginId, pluginName, serverId, showModal]);

    useEffect(() => {
        loadSettings();
    }, [loadSettings]);

    const configurableFields = fields.filter(f => f.key.trim().toLowerCase() !== 'is_enabled');

    const handleValueChange = (key: string, value: string) => {
        setValues(prev => ({ ...prev, [key]: value }));
    };

    const handleSave = async (e: React.SyntheticEvent) => {
        e.preventDefault();
        setIsSaving(true);
        try {
            const payload: Record<string, string> = {};
            configurableFields.forEach(f => { payload[f.key] = values[f.key] ?? ''; });
            await systemSettingsAdminService.updatePluginSettings(pluginId, payload, serverId);
            showModal('Success', `${pluginName} settings saved.`);
            onAfterChange?.();
        } catch {
            showModal('Error', `Failed to save settings for ${pluginName}.`, true);
        } finally {
            setIsSaving(false);
        }
    };

    if (isLoading) return <div className="vora-skeleton h-16" />;

    if (configurableFields.length === 0) {
        return <p className="text-xs text-[var(--vora-text-muted)] italic">This plugin has no configurable settings.</p>;
    }

    return (
        <form onSubmit={handleSave} className="space-y-4">
            {configurableFields.map(field => (
                <div key={field.key}>
                    <label className="block text-xs font-bold text-[var(--vora-text-secondary)] mb-0.5">
                        {field.label}
                        {field.required && <span className="text-[var(--vora-danger-text)] ml-0.5" title="Required">*</span>}
                    </label>
                    {field.description && <p className="text-[11px] text-[var(--vora-text-muted)] mb-1.5">{renderDescriptionWithLinks(field.description)}</p>}
                    {field.type === 'boolean' || field.type === 'checkbox' ? (
                        <label className="flex items-center gap-2 cursor-pointer w-max">
                            <input
                                type="checkbox"
                                checked={values[field.key] === 'true'}
                                onChange={e => handleValueChange(field.key, e.target.checked ? 'true' : 'false')}
                                className="w-4 h-4 accent-[var(--vora-accent-500)] cursor-pointer"
                            />
                            <span className="text-xs text-[var(--vora-text-secondary)] font-medium">{values[field.key] === 'true' ? 'On' : 'Off'}</span>
                        </label>
                    ) : field.type === 'folder' || field.type === 'directory' || field.type === 'path' ? (
                        <FolderPathInput
                            value={values[field.key] || ''}
                            onChange={v => handleValueChange(field.key, v)}
                            serverId={serverId}
                            modalTitle={`Select folder for ${field.label}`}
                        />
                    ) : field.type === 'textarea' ? (
                        <textarea
                            value={values[field.key] || ''}
                            onChange={e => handleValueChange(field.key, e.target.value)}
                            rows={4}
                            placeholder={field.placeholder || undefined}
                            className="vora-input text-sm resize-y"
                        />
                    ) : field.type === 'select' ? (
                        <select
                            value={values[field.key] || ''}
                            onChange={e => handleValueChange(field.key, e.target.value)}
                            className="vora-input text-sm cursor-pointer"
                        >
                            {!field.options.includes(values[field.key] || '') && (
                                <option value={values[field.key] || ''}>{values[field.key] || 'Select…'}</option>
                            )}
                            {field.options.map(opt => (
                                <option key={opt} value={opt}>{opt}</option>
                            ))}
                        </select>
                    ) : (
                        <input
                            type={field.type}
                            value={values[field.key] || ''}
                            onChange={e => handleValueChange(field.key, e.target.value)}
                            placeholder={field.placeholder || undefined}
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
    );
}
