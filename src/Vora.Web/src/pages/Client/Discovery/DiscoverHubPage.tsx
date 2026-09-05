import { useEffect, useMemo, useState } from 'react';
import { useFeatureFlags } from '../../../hooks/useFeatureFlags';
import PageHeader from '../../../components/Client/Primitives/PageHeader';
import Tabs from '../../../components/Client/Primitives/Tabs';
import DiscoveryPage from './DiscoveryPage';
import CalendarPage from '../CalendarPage';

type DiscoverTab = 'discover' | 'calendar';

const ACTIVE_TAB_STORAGE_KEY = 'discover_active_tab';

const TAB_LABELS: Record<DiscoverTab, string> = {
    discover: 'Discover',
    calendar: 'Calendar',
};

const TAB_SUBTITLES: Record<DiscoverTab, string> = {
    discover: "What's out there — trending, popular, and curated picks from external sources.",
    calendar: 'Track upcoming movies, episodes, and watchlist drops.',
};

const readSavedTab = (): DiscoverTab => {
    const saved = sessionStorage.getItem(ACTIVE_TAB_STORAGE_KEY);
    return saved === 'discover' || saved === 'calendar'
        ? saved
        : 'discover';
};

export default function DiscoverHubPage() {
    const flags = useFeatureFlags();

    const visibleTabs = useMemo((): DiscoverTab[] => {
        const tabs: DiscoverTab[] = [];
        if (flags.discover) tabs.push('discover');
        if (flags.releaseCalendar) tabs.push('calendar');
        return tabs;
    }, [flags.discover, flags.releaseCalendar]);

    const [activeTab, setActiveTab] = useState<DiscoverTab>(readSavedTab);

    useEffect(() => {
        if (visibleTabs.length > 0 && !visibleTabs.includes(activeTab)) {
            setActiveTab(visibleTabs[0]);
            sessionStorage.setItem(ACTIVE_TAB_STORAGE_KEY, visibleTabs[0]);
        }
    }, [visibleTabs, activeTab]);

    const handleTabChange = (tab: DiscoverTab) => {
        setActiveTab(tab);
        sessionStorage.setItem(ACTIVE_TAB_STORAGE_KEY, tab);
    };

    const tabDefinitions = visibleTabs.map(tab => ({ key: tab, label: TAB_LABELS[tab] }));

    return (
        <div className="flex h-full min-h-0 flex-col pb-16">
            <PageHeader
                title={TAB_LABELS[activeTab]}
                subtitle={TAB_SUBTITLES[activeTab]}
            />

            <div className="px-8">
                <Tabs<DiscoverTab>
                    tabs={tabDefinitions}
                    active={activeTab}
                    onChange={handleTabChange}
                />
            </div>

            {activeTab === 'discover' && <DiscoveryPage embedded />}
            {activeTab === 'calendar' && (
                <div className="flex min-h-0 flex-1 flex-col">
                    <CalendarPage embedded />
                </div>
            )}
        </div>
    );
}
