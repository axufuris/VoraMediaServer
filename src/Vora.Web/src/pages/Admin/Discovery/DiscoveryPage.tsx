import { useEffect, useState, useRef, useMemo } from 'react';
import { useParams } from 'react-router-dom';
import { discoveryService, type DiscoveryRowConfig } from '../../../api/Discovery/discoveryService';
import { useDialog } from '../../../dialogs';
import FeatureToggle from '../../../components/Admin/Features/FeatureToggle';
import FeaturePluginList from '../../../components/Admin/Features/FeaturePluginList';
import FeatureTabs from '../../../components/Admin/Features/FeatureTabs';
import PageHeader from '../../../components/Admin/Primitives/PageHeader';
import EmptyState from '../../../components/Admin/Primitives/EmptyState';

export default function DiscoveryPage() {
    const dialog = useDialog();
    const { serverId } = useParams<{ serverId?: string }>();
    const [configs, setConfigs] = useState<DiscoveryRowConfig[]>([]);
    const [isLoading, setIsLoading] = useState(true);
    const [isSaving, setIsSaving] = useState(false);
    const [activeTab, setActiveTab] = useState<'layout' | 'plugins'>('layout');

    const dragItem = useRef<number | null>(null);
    const dragOverItem = useRef<number | null>(null);

    useEffect(() => {
        const loadConfigs = async () => {
            try {
                const data = await discoveryService.getAdminConfigs(serverId);
                setConfigs(data);
            } catch (error) {
                console.error('Failed to load discovery configs', error);
            } finally {
                setIsLoading(false);
            }
        };
        loadConfigs();
    }, [serverId]);

    const handleSort = () => {
        if (dragItem.current === null || dragOverItem.current === null) return;
        const _configs = [...configs];
        const draggedItemContent = _configs.splice(dragItem.current, 1)[0];
        _configs.splice(dragOverItem.current, 0, draggedItemContent);
        dragItem.current = null;
        dragOverItem.current = null;
        setConfigs(_configs);
    };

    const toggleEnabled = (index: number) => {
        const newConfigs = [...configs];
        newConfigs[index].isEnabled = !newConfigs[index].isEnabled;
        setConfigs(newConfigs);
    };

    const handleSave = async () => {
        setIsSaving(true);
        try {
            await discoveryService.updateAdminConfigs(configs, serverId);
            await dialog.alert('Discovery layout saved.');
        } catch {
            await dialog.alert('Failed to save layout.');
        } finally {
            setIsSaving(false);
        }
    };

    const pluginTypes = useMemo(() => ['Discovery', 'Theater'], []);

    return (
        <div data-vora-page="">
            <PageHeader
                title="Discover"
                description="Curate the dynamic rows that appear on the client Discover page."
            />

            <div className="px-8 pt-6 pb-10 max-w-6xl mx-auto">
                <FeatureToggle
                    featureKey="discover"
                    label="Enable Discover"
                    description="When off, the Discover nav entry is hidden from clients and all Discover endpoints return 403. Existing settings below stay editable so you can configure ahead of time."
                    serverId={serverId}
                />

                <FeatureTabs
                    tabs={[
                        { key: 'layout', label: 'Page Layout' },
                        { key: 'plugins', label: 'Plugins' },
                    ]}
                    activeKey={activeTab}
                    onChange={k => setActiveTab(k as 'layout' | 'plugins')}
                />

                {activeTab === 'layout' && (
                    <section>
                        <p className="text-sm text-[var(--vora-text-muted)] mb-4">
                            Drag rows to reorder. Toggle to show or hide each row on the client Discover page.
                        </p>

                        {isLoading ? (
                            <div className="space-y-3">
                                {[1, 2, 3].map(i => <div key={i} className="vora-skeleton h-16" />)}
                            </div>
                        ) : configs.length === 0 ? (
                            <div className="vora-card">
                                <EmptyState
                                    title="No discovery rows available"
                                    description="Make sure a Discovery or Theater plugin is installed and enabled."
                                />
                            </div>
                        ) : (
                            <>
                                <div className="space-y-2 mb-4">
                                    {configs.map((config, index) => (
                                        <div
                                            key={config.id}
                                            draggable
                                            onDragStart={() => (dragItem.current = index)}
                                            onDragEnter={() => (dragOverItem.current = index)}
                                            onDragEnd={handleSort}
                                            onDragOver={(e) => e.preventDefault()}
                                            className={`flex items-center justify-between p-4 vora-card vora-card-interactive cursor-move ${config.isEnabled ? '' : 'opacity-60'}`}
                                        >
                                            <div className="flex items-center gap-3 min-w-0">
                                                <svg className="w-4 h-4 text-[var(--vora-text-disabled)] shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 8h16M4 16h16" /></svg>
                                                <div className="min-w-0">
                                                    <h3 className="text-sm font-semibold text-[var(--vora-text-primary)] truncate">{config.name}</h3>
                                                    <p className="text-xs text-[var(--vora-text-muted)] truncate">Provider: {config.providerName || config.providerId}</p>
                                                </div>
                                            </div>

                                            <button
                                                type="button"
                                                onClick={() => toggleEnabled(index)}
                                                className={`relative inline-flex h-6 w-11 shrink-0 items-center rounded-full transition-colors cursor-pointer ${config.isEnabled ? 'bg-[var(--vora-accent-500)]' : 'bg-[var(--vora-border-strong)]'}`}
                                                aria-pressed={config.isEnabled}
                                            >
                                                <span className={`inline-block h-4 w-4 transform rounded-full bg-white transition-transform shadow-sm ${config.isEnabled ? 'translate-x-6' : 'translate-x-1'}`} />
                                            </button>
                                        </div>
                                    ))}
                                </div>

                                <button
                                    type="button"
                                    onClick={handleSave}
                                    disabled={isSaving}
                                    className="vora-button-primary"
                                >
                                    {isSaving ? 'Saving…' : 'Save layout'}
                                </button>
                            </>
                        )}
                    </section>
                )}

                {activeTab === 'plugins' && (
                    <section>
                        <p className="text-sm text-[var(--vora-text-muted)] mb-4">
                            Discovery and Theater plugins that power this feature.
                        </p>
                        <FeaturePluginList
                            serverId={serverId}
                            pluginTypes={pluginTypes}
                            emptyLabel="No Discovery or Theater plugins are installed."
                        />
                    </section>
                )}
            </div>
        </div>
    );
}
