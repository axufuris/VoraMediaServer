import { useEffect, useState } from 'react';
import { useParams } from 'react-router-dom';
import PageHeader from '../../components/Admin/Primitives/PageHeader';
import { useActionToast } from '../../components/Admin/Primitives/ActionToast';
import ClientSettingsEditor from '../../components/Common/ClientSettings/ClientSettingsEditor';
import DeviceSettingsEditor from '../../components/Common/ClientSettings/DeviceSettingsEditor';
import { clientDefaultsService, type ClientDefaultsVM } from '../../api/System/clientDefaultsService';
import type { ClientSettingsVM, DeviceSettingsVM, SaveClientSettingsRequest } from '../../api/Users/profileService';
import { resolveReason } from '../../utils/apiError';

export default function ClientDefaultsPage() {
    const { serverId } = useParams<{ serverId?: string }>();
    const { toast, showToast } = useActionToast();
    const [settings, setSettings] = useState<ClientDefaultsVM | null>(null);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        let cancelled = false;
        clientDefaultsService.get(serverId)
            .then(result => { if (!cancelled) setSettings(result); })
            .catch(err => { if (!cancelled) setError(resolveReason(err) ?? 'Could not read the client defaults.'); });
        return () => { cancelled = true; };
    }, [serverId]);

    const save = async (request: SaveClientSettingsRequest): Promise<ClientSettingsVM> => {
        const saved = await clientDefaultsService.save(request, serverId);
        setSettings(saved);
        return saved;
    };

    const saveDevice = async (device: DeviceSettingsVM) => {
        const saved = await clientDefaultsService.save({ device }, serverId);
        setSettings(saved);
        showToast('Saved the device defaults.');
    };

    return (
        <div data-vora-page="">
            <PageHeader
                title="Client Defaults"
                description="The settings a new profile starts with. Each person can change theirs in Settings, and changing these never touches a profile that already exists."
            />
            <div className="p-8 max-md:p-4 max-w-4xl mx-auto space-y-6">
                <p className="m-0 text-sm text-[var(--vora-text-secondary)]">
                    A profile added after you save here starts with these choices, whether someone signs up, you add them, or a profile is created under an account. To change one existing profile, use Client settings on its tile in Users &amp; Access.
                </p>
                {error && <p role="alert" className="m-0 text-sm text-[var(--vora-danger-text)]">{error}</p>}
                {!error && !settings && <div className="vora-skeleton h-64" />}
                {settings && (
                    <>
                        <section className="vora-card p-6">
                            <h2 className="m-0 mb-1 text-base font-semibold text-[var(--vora-text-primary)]">New profiles start with</h2>
                            <p className="m-0 mb-6 text-sm text-[var(--vora-text-muted)]">Copied into a profile when it is created. A profile that already exists keeps what it has.</p>
                            <ClientSettingsEditor
                                settings={settings}
                                onSave={save}
                                onSaved={() => showToast('Saved the client defaults.')}
                                saveLabel="Save defaults"
                            />
                        </section>
                        <section className="vora-card p-6">
                            <h2 className="m-0 mb-1 text-base font-semibold text-[var(--vora-text-primary)]">Devices that have not set their own</h2>
                            <p className="m-0 mb-6 text-sm text-[var(--vora-text-muted)]">
                                Bitrate and resolution limits, the skip and volume steps and the sounds are kept for each device. A device that has not saved its own follows these, so a change here reaches those devices straight away, and a device keeps its own once it saves.
                            </p>
                            <DeviceSettingsEditor settings={settings.device} saveLabel="Save device defaults" onSave={saveDevice} />
                        </section>
                    </>
                )}
            </div>
            {toast}
        </div>
    );
}
