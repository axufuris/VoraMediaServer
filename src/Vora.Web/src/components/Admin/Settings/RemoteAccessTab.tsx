import { useEffect, useState, useCallback } from 'react';
import { systemSettingsAdminService, type ServerSettings } from '../../../api/System/systemSettingsAdminService';
import { remoteAccessService, type RemoteAccessStatus } from '../../../api/System/remoteAccessService';
import HealthBadge from '../Primitives/HealthBadge';

interface RemoteAccessTabProps {
    serverId?: string;
    showModal: (title: string, message: string, isError?: boolean) => void;
}

function FieldLabel({ children }: { children: React.ReactNode }) {
    return <label className="block text-xs font-bold uppercase tracking-widest text-[var(--vora-text-muted)] mb-1.5">{children}</label>;
}

function FieldHint({ children }: { children: React.ReactNode }) {
    return <p className="text-xs text-[var(--vora-text-muted)] mt-2">{children}</p>;
}

export default function RemoteAccessTab({ serverId, showModal }: RemoteAccessTabProps) {
    const [serverSettings, setServerSettings] = useState<ServerSettings | null>(null);
    const [remoteStatus, setRemoteStatus] = useState<RemoteAccessStatus | null>(null);
    const [isSaving, setIsSaving] = useState(false);
    const [isCheckingRemote, setIsCheckingRemote] = useState(false);

    const loadData = useCallback(async () => {
        setIsCheckingRemote(true);
        try {
            const [settings, status] = await Promise.all([
                systemSettingsAdminService.getServerSettings(serverId),
                remoteAccessService.getRemoteAccessStatus(serverId),
            ]);
            setServerSettings(settings);
            setRemoteStatus(status);
        } catch {
            showModal('Error', 'Failed to load remote access settings.', true);
        } finally {
            setIsCheckingRemote(false);
        }
    }, [serverId, showModal]);

    useEffect(() => {
        loadData();
    }, [loadData]);

    const handleSaveRemote = async (e: React.SyntheticEvent) => {
        e.preventDefault();
        if (!serverSettings || !remoteStatus) return;
        setIsSaving(true);
        setIsCheckingRemote(true);
        try {
            await systemSettingsAdminService.updateServerSettings(serverSettings, serverId);
            const updatedStatus = await remoteAccessService.updateRemoteAccess({
                isEnabled: remoteStatus.isEnabled,
                manuallySpecifyPort: remoteStatus.manuallySpecifyPort,
                publicPort: remoteStatus.publicPort,
            }, serverId);
            setRemoteStatus(updatedStatus);
            showModal('Success', 'Remote access settings applied successfully.');
        } catch {
            showModal('Error', 'Failed to apply remote access settings.', true);
        } finally {
            setIsSaving(false);
            setIsCheckingRemote(false);
        }
    };

    const handleToggleRemoteStatus = async () => {
        if (!serverSettings || !remoteStatus) return;
        setIsSaving(true);
        setIsCheckingRemote(true);
        try {
            const newEnabledState = !remoteStatus.isEnabled;
            const updatedStatus = await remoteAccessService.updateRemoteAccess({
                isEnabled: newEnabledState,
                manuallySpecifyPort: remoteStatus.manuallySpecifyPort,
                publicPort: remoteStatus.publicPort,
            }, serverId);
            setRemoteStatus(updatedStatus);
        } catch {
            showModal('Error', 'Failed to toggle remote access.', true);
        } finally {
            setIsSaving(false);
            setIsCheckingRemote(false);
        }
    };

    if (!serverSettings || !remoteStatus) return <div className="vora-skeleton h-32 mt-6" />;

    let statusTone: 'ok' | 'warn' | 'error' | 'neutral' = 'neutral';
    let statusTitle = 'Remote access is disabled';

    if (remoteStatus.isEnabled) {
        if (remoteStatus.upnpSupported) {
            statusTone = 'ok';
            statusTitle = 'Fully accessible outside your network';
        } else if (remoteStatus.manuallySpecifyPort) {
            statusTone = 'ok';
            statusTitle = 'Manual port forwarding active';
        } else {
            statusTone = 'error';
            statusTitle = 'Not accessible outside your network';
        }
    }

    return (
        <form onSubmit={handleSaveRemote} className="space-y-6 pt-2">
            <section className="vora-card p-6 relative">
                {isCheckingRemote && (
                    <div className="absolute inset-0 bg-[var(--vora-bg-surface)]/85 backdrop-blur-sm flex flex-col items-center justify-center z-10 rounded-[var(--vora-radius-lg)]">
                        <div className="animate-spin rounded-full h-8 w-8 border-t-2 border-b-2 border-[var(--vora-accent-500)] mb-2"></div>
                        <span className="text-[var(--vora-text-secondary)] font-medium text-sm">Checking network status…</span>
                    </div>
                )}

                <div className="flex items-start gap-4 mb-6 pb-6 border-b border-[var(--vora-border-subtle)]">
                    <div className={`p-3 rounded-full shrink-0 ${
                        statusTone === 'ok' ? 'bg-[var(--vora-success-soft)] text-[var(--vora-success-text)]' :
                        statusTone === 'error' ? 'bg-[var(--vora-danger-soft)] text-[var(--vora-danger-text)]' :
                        'bg-[var(--vora-bg-sunken)] text-[var(--vora-text-muted)]'
                    }`}>
                        <svg className="w-7 h-7" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.75} d="M21 12a9 9 0 01-9 9m9-9a9 9 0 00-9-9m9 9H3m9 9a9 9 0 01-9-9m9 9c1.657 0 3-4.03 3-9s-1.343-9-3-9m0 18c-1.657 0-3-4.03-3-9s1.343-9 3-9m-9 9a9 9 0 019-9" /></svg>
                    </div>
                    <div className="flex-1 min-w-0">
                        <h3 className="text-base font-semibold text-[var(--vora-text-primary)] flex items-center gap-2">
                            {statusTitle}
                            <HealthBadge tone={statusTone}>{remoteStatus.isEnabled ? 'On' : 'Off'}</HealthBadge>
                        </h3>
                        {remoteStatus.isEnabled && remoteStatus.errorMessage && !remoteStatus.manuallySpecifyPort && (
                            <p className="text-sm text-[var(--vora-danger-text)] mt-1 max-w-xl">{remoteStatus.errorMessage}</p>
                        )}
                    </div>
                </div>

                <div className="flex flex-wrap items-center gap-x-3 gap-y-2 text-sm">
                    <span className="text-xs uppercase tracking-widest font-semibold text-[var(--vora-text-muted)]">Private</span>
                    <span className="font-mono text-[var(--vora-text-primary)]">{remoteStatus.localIp}:{remoteStatus.localPort}</span>
                    <svg className="w-4 h-4 text-[var(--vora-success-500)]" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M14 5l7 7m0 0l-7 7m7-7H3" /></svg>
                    <span className="text-xs uppercase tracking-widest font-semibold text-[var(--vora-text-muted)]">Public</span>
                    <span className="font-mono text-[var(--vora-text-primary)]">{remoteStatus.publicIp}:{remoteStatus.publicPort}</span>
                    <svg className={`w-4 h-4 ${statusTone === 'ok' ? 'text-[var(--vora-success-500)]' : 'text-[var(--vora-danger-500)]'}`} fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M14 5l7 7m0 0l-7 7m7-7H3" /></svg>
                    <span className="text-xs uppercase tracking-widest font-semibold text-[var(--vora-text-muted)]">Internet</span>
                </div>

                <div className="mt-6 flex items-center gap-4 pt-6 border-t border-[var(--vora-border-subtle)]">
                    <label className="flex items-center gap-3 cursor-pointer group select-none">
                        <input
                            type="checkbox"
                            checked={remoteStatus.manuallySpecifyPort}
                            onChange={e => setRemoteStatus({ ...remoteStatus, manuallySpecifyPort: e.target.checked })}
                            className="w-4 h-4 accent-[var(--vora-accent-500)] cursor-pointer"
                        />
                        <span className="text-sm font-medium text-[var(--vora-text-primary)]">Manually specify public port</span>
                    </label>
                    <input
                        type="number"
                        value={remoteStatus.publicPort}
                        onChange={e => setRemoteStatus({ ...remoteStatus, publicPort: parseInt(e.target.value) || 32080 })}
                        disabled={!remoteStatus.manuallySpecifyPort}
                        className="vora-input w-24 text-center disabled:opacity-50"
                    />
                </div>
                <FieldHint>
                    Vora attempts to automatically map the port via UPnP. If your router requires manual port forwarding, check the box above and configure your router to forward traffic to the private IP and port shown.
                </FieldHint>
            </section>

            <section className="vora-card p-6">
                <h3 className="text-base font-semibold text-[var(--vora-text-primary)] mb-4">Bandwidth Restrictions</h3>
                <div className="space-y-5">
                    <div>
                        <FieldLabel>Internet Upload Speed (Mbps)</FieldLabel>
                        <input
                            type="number"
                            min={0}
                            value={serverSettings.internetUploadSpeedMbps || 0}
                            onChange={e => setServerSettings({ ...serverSettings, internetUploadSpeedMbps: parseInt(e.target.value) || 0 })}
                            className="vora-input w-32"
                        />
                        <FieldHint>Total upload bandwidth available to the server. 0 disables.</FieldHint>
                    </div>
                    <div>
                        <FieldLabel>Limit Remote Stream Bitrate</FieldLabel>
                        <select
                            value={serverSettings.maxRemoteStreamBitrateMbps || 0}
                            onChange={e => setServerSettings({ ...serverSettings, maxRemoteStreamBitrateMbps: parseInt(e.target.value) || 0 })}
                            className="vora-input max-w-md cursor-pointer"
                        >
                            <option value={0}>Original (no limit)</option>
                            <option value={40}>40 Mbps (4K)</option>
                            <option value={20}>20 Mbps (1080p)</option>
                            <option value={15}>15 Mbps (1080p)</option>
                            <option value={10}>10 Mbps (1080p)</option>
                            <option value={8}>8 Mbps (1080p)</option>
                            <option value={4}>4 Mbps (720p)</option>
                            <option value={3}>3 Mbps (720p)</option>
                            <option value={2}>2 Mbps (480p)</option>
                            <option value={1}>1 Mbps (SD)</option>
                        </select>
                        <FieldHint>Caps the maximum bitrate for streams leaving your home network.</FieldHint>
                    </div>
                </div>
            </section>

            <div className="flex items-center gap-3">
                <button type="submit" disabled={isSaving || isCheckingRemote} className="vora-button-primary">
                    {isSaving ? 'Applying…' : 'Apply settings'}
                </button>
                <button
                    type="button"
                    onClick={handleToggleRemoteStatus}
                    disabled={isSaving || isCheckingRemote}
                    className={`px-4 py-2 rounded-[var(--vora-radius-md)] text-sm font-semibold border transition-colors cursor-pointer disabled:opacity-50 ${
                        remoteStatus.isEnabled
                            ? 'text-[var(--vora-danger-text)] border-[var(--vora-danger-soft)] bg-[var(--vora-danger-soft)] hover:bg-[var(--vora-danger-500)] hover:text-[var(--vora-text-primary)] hover:border-[var(--vora-danger-500)]'
                            : 'text-[var(--vora-success-text)] border-[var(--vora-success-soft)] bg-[var(--vora-success-soft)] hover:bg-[var(--vora-success-500)] hover:text-[var(--vora-text-primary)] hover:border-[var(--vora-success-500)]'
                    }`}
                >
                    {remoteStatus.isEnabled ? 'Disable remote access' : 'Enable remote access'}
                </button>
            </div>
        </form>
    );
}
