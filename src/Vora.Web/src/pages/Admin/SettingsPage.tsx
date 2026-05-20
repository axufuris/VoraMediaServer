import { useEffect, useState, useCallback } from 'react';
import { useParams } from 'react-router-dom';
import { systemSettingsAdminService } from '../../api/System/systemSettingsAdminService';
import { apiClient } from '../../api/client';

import RequestServersTab from '../../components/Admin/Settings/RequestServersTab';
import CoreSettingsTab from '../../components/Admin/Settings/CoreSettingsTab';
import RemoteAccessTab from '../../components/Admin/Settings/RemoteAccessTab';
import PageHeader from '../../components/Admin/Primitives/PageHeader';
import FeatureTabs from '../../components/Admin/Features/FeatureTabs';

type SettingsTabKey = 'core' | 'remote' | 'requests';

export default function SettingsPage() {
    const { serverId } = useParams<{ serverId?: string }>();
    const storageKey = `admin_settings_tab_${serverId || 'global'}`;
    const [activeTab, setActiveTab] = useState<SettingsTabKey>(() => {
        const saved = localStorage.getItem(storageKey);
        return (saved === 'core' || saved === 'remote' || saved === 'requests') ? saved : 'core';
    });
    const [scanners, setScanners] = useState<{ id: string, name: string }[]>([]);
    const [hardwareDevices, setHardwareDevices] = useState<string[]>([]);

    const [modalConfig, setModalConfig] = useState<{ isOpen: boolean, title: string, message: string, isError: boolean }>({ isOpen: false, title: '', message: '', isError: false });
    const showModal = useCallback((title: string, message: string, isError: boolean = false) => {
        setModalConfig({ isOpen: true, title, message, isError });
    }, []);
    const closeModal = () => setModalConfig(prev => ({ ...prev, isOpen: false }));

    useEffect(() => {
        localStorage.setItem(storageKey, activeTab);
    }, [activeTab, storageKey]);

    useEffect(() => {
        Promise.all([
            apiClient.get<{ id: string, name: string }[]>('/plugins/options?type=LocalScanner', { serverId }).then(res => res.data),
            systemSettingsAdminService.getHardwareDevices(serverId),
        ]).then(([scannersData, hardwareData]) => {
            setScanners(scannersData);
            setHardwareDevices(hardwareData);
        }).catch(console.error);
    }, [serverId]);

    return (
        <div data-vora-page="">
            <PageHeader
                title="System Settings"
                description="Server name, transcoder behavior, remote access, and request providers."
            />

            <div className="px-8 pt-2 pb-10 max-w-6xl mx-auto">
                <FeatureTabs
                    tabs={[
                        { key: 'core', label: 'Core' },
                        { key: 'remote', label: 'Remote Access' },
                        { key: 'requests', label: 'Request Servers' },
                    ]}
                    activeKey={activeTab}
                    onChange={k => setActiveTab(k as SettingsTabKey)}
                />

                {activeTab === 'core' && (
                    <CoreSettingsTab serverId={serverId} scanners={scanners} hardwareDevices={hardwareDevices} showModal={showModal} />
                )}
                {activeTab === 'remote' && (
                    <RemoteAccessTab serverId={serverId} showModal={showModal} />
                )}
                {activeTab === 'requests' && (
                    <RequestServersTab serverId={serverId} showModal={showModal} />
                )}
            </div>

            {modalConfig.isOpen && (
                <div className="fixed inset-0 z-[200] flex items-center justify-center bg-[var(--vora-bg-overlay)] backdrop-blur-sm p-4" onClick={closeModal}>
                    <div className="vora-card shadow-[var(--vora-shadow-overlay)] p-6 max-w-sm w-full text-center" onClick={e => e.stopPropagation()}>
                        <div className={`w-14 h-14 rounded-full flex items-center justify-center mx-auto mb-4 ${modalConfig.isError ? 'bg-[var(--vora-danger-soft)] text-[var(--vora-danger-text)]' : 'bg-[var(--vora-success-soft)] text-[var(--vora-success-text)]'}`}>
                            {modalConfig.isError ? (
                                <svg className="w-7 h-7" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" /></svg>
                            ) : (
                                <svg className="w-7 h-7" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M5 13l4 4L19 7" /></svg>
                            )}
                        </div>
                        <h2 className="text-lg font-semibold text-[var(--vora-text-primary)] mb-2">{modalConfig.title}</h2>
                        <p className="text-sm text-[var(--vora-text-secondary)] mb-6 whitespace-pre-wrap">{modalConfig.message}</p>
                        <button type="button" onClick={closeModal} className="vora-button-primary w-full">OK</button>
                    </div>
                </div>
            )}
        </div>
    );
}
