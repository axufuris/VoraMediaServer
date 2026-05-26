import { useState } from 'react';
import { serverVault } from '../../utils/serverVault';
import { useDialog } from '../../dialogs';
import PageHeader from '../../components/Client/Primitives/PageHeader';
import Tabs from '../../components/Client/Primitives/Tabs';
import TemplatesTab from './Settings/TemplatesTab';
import PlaybackTab from './Settings/PlaybackTab';
import ProvidersTab from './Settings/ProvidersTab';
import AccountTab from './Settings/AccountTab';
import AboutTab from './Settings/AboutTab';

type SettingsTabKey = 'templates' | 'playback' | 'providers' | 'account' | 'about';

export default function SettingsPage() {
    const dialog = useDialog();
    const activeServer = serverVault.getActiveServer();
    const activeProfileId = activeServer?.profileId || '';
    const serverId = activeServer?.id;

    const [activeTab, setActiveTab] = useState<SettingsTabKey>('templates');

    const onSaved = () => {
        void dialog.alert({
            title: 'Settings saved',
            message: 'Your preferences have been updated.',
            tone: 'success',
        });
    };

    return (
        <div className="min-h-full pb-20">
            <PageHeader
                title="Settings"
                subtitle="Personalize Vora for this profile. Most changes save automatically."
            />

            <div className="px-8">
                <Tabs<SettingsTabKey>
                    tabs={[
                        { key: 'templates', label: 'Templates' },
                        { key: 'playback', label: 'Playback' },
                        { key: 'providers', label: 'Providers' },
                        { key: 'account', label: 'Account' },
                        { key: 'about', label: 'About' },
                    ]}
                    active={activeTab}
                    onChange={setActiveTab}
                />
            </div>

            <div className="px-8 pt-6">
                {activeTab === 'templates' && <TemplatesTab activeProfileId={activeProfileId} />}
                {activeTab === 'playback' && <PlaybackTab activeProfileId={activeProfileId} serverId={serverId} onSaved={onSaved} />}
                {activeTab === 'providers' && <ProvidersTab activeProfileId={activeProfileId} serverId={serverId} onSaved={onSaved} />}
                {activeTab === 'account' && <AccountTab serverId={serverId} />}
                {activeTab === 'about' && <AboutTab />}
            </div>
        </div>
    );
}
