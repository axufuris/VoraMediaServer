import { useEffect, useMemo, useState, useCallback } from 'react';
import { pluginAdminService, type PluginVM } from '../../../api/System/pluginAdminService';
import { useDialog } from '../../../dialogs';
import { PluginSection } from '../Settings/PluginSettingsTab';

interface FeaturePluginListProps {
    serverId?: string;
    pluginTypes: string[];
    title?: string;
    emptyLabel?: string;
    onAfterChange?: () => void;
}

function formatTypeLabel(type: string): string {
    const withSpaces = type.replace(/_/g, ' ').replace(/([a-z])([A-Z])/g, '$1 $2');
    return withSpaces.replace(/\b\w/g, c => c.toUpperCase());
}

export default function FeaturePluginList({ serverId, pluginTypes, title, emptyLabel, onAfterChange }: FeaturePluginListProps) {
    const [plugins, setPlugins] = useState<PluginVM[]>([]);
    const [isLoading, setIsLoading] = useState(true);
    const dialog = useDialog();

    const showModal = useCallback((modalTitle: string, message: string, isError: boolean = false) => {
        dialog.alert({ title: modalTitle, message, tone: isError ? 'danger' : 'default' });
    }, [dialog]);

    useEffect(() => {
        let cancelled = false;
        pluginAdminService.getPlugins(serverId)
            .then(all => {
                if (cancelled) return;
                const typeSet = new Set(pluginTypes);
                const filtered = all
                    .filter(p => p.hasSettings && typeSet.has(p.type))
                    .sort((a, b) => a.name.localeCompare(b.name));
                setPlugins(filtered);
            })
            .catch(err => console.error('Failed to load plugins', err))
            .finally(() => {
                if (!cancelled) setIsLoading(false);
            });
        return () => { cancelled = true; };
    }, [serverId, pluginTypes]);

    const groups = useMemo(() => {
        const byType = new Map<string, PluginVM[]>();
        for (const p of plugins) {
            const list = byType.get(p.type);
            if (list) list.push(p);
            else byType.set(p.type, [p]);
        }
        const ordered: { type: string, plugins: PluginVM[] }[] = [];
        for (const t of pluginTypes) {
            const list = byType.get(t);
            if (list && list.length > 0) {
                ordered.push({ type: t, plugins: list });
                byType.delete(t);
            }
        }
        for (const [t, list] of byType) {
            ordered.push({ type: t, plugins: list });
        }
        return ordered;
    }, [plugins, pluginTypes]);

    const showGroupHeaders = groups.length > 1;

    return (
        <div className="space-y-4">
            {title && <h2 className="text-xl font-semibold text-[var(--vora-text-primary)]">{title}</h2>}

            {isLoading ? (
                <div className="vora-skeleton h-24" />
            ) : plugins.length === 0 ? (
                <div className="text-sm text-[var(--vora-text-muted)] italic p-6 vora-card">
                    {emptyLabel ?? 'No plugins configured for this feature.'}
                </div>
            ) : (
                <div className="space-y-5">
                    {groups.map(group => (
                        <div key={group.type} className="space-y-2">
                            {showGroupHeaders && (
                                <h3 className="text-xs font-bold text-[var(--vora-text-muted)] uppercase tracking-widest ml-1">
                                    {formatTypeLabel(group.type)}
                                </h3>
                            )}
                            {group.plugins.map(p => (
                                <PluginSection key={p.id} serverId={serverId} plugin={p} showModal={showModal} onAfterChange={onAfterChange} />
                            ))}
                        </div>
                    ))}
                </div>
            )}

        </div>
    );
}
