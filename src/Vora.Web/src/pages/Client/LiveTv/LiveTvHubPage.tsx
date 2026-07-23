import { useEffect, useMemo, useState } from 'react';
import { useFeatureFlags } from '../../../hooks/useFeatureFlags';
import PageHeader from '../../../components/Client/Primitives/PageHeader';
import Tabs from '../../../components/Client/Primitives/Tabs';
import LiveTvGuide from './LiveTvGuide';
import DvrDashboard from './DvrDashboard';

type LiveTvTab = 'guide' | 'dvr';

const ACTIVE_TAB_STORAGE_KEY = 'livetv_active_tab';

const readSavedTab = (): LiveTvTab => {
    const saved = sessionStorage.getItem(ACTIVE_TAB_STORAGE_KEY);
    return saved === 'guide' || saved === 'dvr' ? saved : 'guide';
};

export default function LiveTvHubPage() {
    const flags = useFeatureFlags();

    const visibleTabs = useMemo((): LiveTvTab[] => {
        const tabs: LiveTvTab[] = ['guide'];
        if (flags.dvr) tabs.push('dvr');
        return tabs;
    }, [flags.dvr]);

    const [activeTab, setActiveTab] = useState<LiveTvTab>(readSavedTab);

    useEffect(() => {
        if (!visibleTabs.includes(activeTab)) {
            setActiveTab(visibleTabs[0]);
            sessionStorage.setItem(ACTIVE_TAB_STORAGE_KEY, visibleTabs[0]);
        }
    }, [visibleTabs, activeTab]);

    const handleTabChange = (tab: LiveTvTab) => {
        setActiveTab(tab);
        sessionStorage.setItem(ACTIVE_TAB_STORAGE_KEY, tab);
    };

    const tabDefinitions = useMemo(
        () => visibleTabs.map(tab => ({ key: tab, label: tab === 'guide' ? 'Guide' : 'DVR' })),
        [visibleTabs]
    );

    return (
        <div className="flex h-full min-h-0 flex-col">
            <div className="shrink-0">
                <PageHeader
                    title="Live TV"
                    subtitle="Guide, channels, and your recordings."
                />
            </div>

            <div className="shrink-0 px-8">
                <Tabs<LiveTvTab>
                    tabs={tabDefinitions}
                    active={activeTab}
                    onChange={handleTabChange}
                />
            </div>

            {activeTab === 'guide' ? (
                <div className="relative min-h-0 flex-1">
                    <div className="absolute inset-0">
                        <LiveTvGuide isEmbedded />
                    </div>
                </div>
            ) : (
                <div className="min-h-0 flex-1 overflow-auto">
                    <DvrDashboard embedded />
                </div>
            )}
        </div>
    );
}
