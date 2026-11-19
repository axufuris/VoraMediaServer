import { useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { systemSettingsAdminService, type HardwareCapabilities } from '../../api/System/systemSettingsAdminService';
import { apiClient } from '../../api/client';

import CoreSettingsTab from '../../components/Admin/Settings/CoreSettingsTab';
import RemoteAccessTab from '../../components/Admin/Settings/RemoteAccessTab';
import EmailTab from '../../components/Admin/Settings/EmailTab';
import StorageTab from '../../components/Admin/Settings/StorageTab';
import PageHeader from '../../components/Admin/Primitives/PageHeader';
import { useNoticeModal } from '../../components/Admin/Primitives/NoticeModal';
import FeatureTabs from '../../components/Admin/Features/FeatureTabs';
import { resolveAdminPath } from '../../components/Admin/Shell/adminNavData';

type SettingsTabKey = 'core' | 'remote' | 'email' | 'storage';

export default function SettingsPage() {
    const { serverId } = useParams<{ serverId?: string }>();
    const storageKey = `admin_settings_tab_${serverId || 'global'}`;
    const [activeTab, setActiveTab] = useState<SettingsTabKey>(() => {
        const saved = localStorage.getItem(storageKey);
        // A tab that has moved or no longer exists (`requests`, now under External Connections) falls back to the first one.
        return (saved === 'core' || saved === 'remote' || saved === 'email' || saved === 'storage') ? saved : 'core';
    });
    const [scanners, setScanners] = useState<{ id: string, name: string }[]>([]);
    const [hardware, setHardware] = useState<HardwareCapabilities | null>(null);

    const { noticeModal, showModal } = useNoticeModal();

    useEffect(() => {
        localStorage.setItem(storageKey, activeTab);
    }, [activeTab, storageKey]);

    useEffect(() => {
        let cancelled = false;
        apiClient.get<{ id: string, name: string }[]>('/plugins/options?type=LocalScanner', { serverId })
            .then(res => { if (!cancelled) setScanners(res.data); })
            .catch(console.error);
        systemSettingsAdminService.getHardware(serverId)
            .then(data => { if (!cancelled) setHardware(data); })
            .catch(console.error);
        return () => { cancelled = true; };
    }, [serverId]);

    return (
        <div data-vora-page="">
            <PageHeader
                title="System Settings"
                description="Server name, transcoder behavior, remote access, email and storage clean-up."
            />

            <div className="px-8 max-md:px-4 pt-2 pb-10 max-w-6xl mx-auto">
                <section aria-labelledby="setup-guide-card-heading" className="vora-card mb-6 flex flex-wrap items-center justify-between gap-4 p-5">
                    <div className="min-w-0">
                        <h2 id="setup-guide-card-heading" className="text-base font-semibold text-[var(--vora-text-primary)]">Setup guide</h2>
                        <p className="mt-0.5 max-w-2xl text-sm text-[var(--vora-text-muted)]">
                            Walk through the main settings one step at a time: playback, remote access, sign-ups and email, plugins, AI and backups. Each step shows what the server has now.
                        </p>
                    </div>
                    <Link to={resolveAdminPath('/admin/setup', serverId)} className="vora-button-secondary shrink-0 text-sm">Run setup guide</Link>
                </section>

                <FeatureTabs
                    tabs={[
                        { key: 'core', label: 'Core' },
                        { key: 'remote', label: 'Remote Access' },
                        { key: 'email', label: 'Email' },
                        { key: 'storage', label: 'Storage' },
                    ]}
                    activeKey={activeTab}
                    onChange={k => setActiveTab(k as SettingsTabKey)}
                />

                {activeTab === 'core' && (
                    <CoreSettingsTab serverId={serverId} scanners={scanners} hardware={hardware} showModal={showModal} />
                )}
                {activeTab === 'remote' && (
                    <RemoteAccessTab serverId={serverId} showModal={showModal} />
                )}
                {activeTab === 'email' && (
                    <EmailTab serverId={serverId} />
                )}
                {activeTab === 'storage' && (
                    <StorageTab serverId={serverId} />
                )}
            </div>

            {noticeModal}
        </div>
    );
}
