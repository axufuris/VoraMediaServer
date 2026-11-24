import { useEffect, useState } from 'react';
import { useParams } from 'react-router-dom';
import PageHeader from '../../components/Admin/Primitives/PageHeader';
import { useActionToast } from '../../components/Admin/Primitives/ActionToast';
import ClientSettingsEditor from '../../components/Common/ClientSettings/ClientSettingsEditor';
import { clientDefaultsService } from '../../api/System/clientDefaultsService';
import type { ClientSettingsVM, SaveClientSettingsRequest } from '../../api/Users/profileService';
import { resolveReason } from '../../utils/apiError';

export default function ClientDefaultsPage() {
    const { serverId } = useParams<{ serverId?: string }>();
    const { toast, showToast } = useActionToast();
    const [settings, setSettings] = useState<ClientSettingsVM | null>(null);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        let cancelled = false;
        clientDefaultsService.get(serverId)
            .then(result => { if (!cancelled) setSettings(result); })
            .catch(err => { if (!cancelled) setError(resolveReason(err) ?? 'Could not read the client defaults.'); });
        return () => { cancelled = true; };
    }, [serverId]);

    const save = (request: SaveClientSettingsRequest) => clientDefaultsService.save(request, serverId);

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
                    <div className="vora-card p-6">
                        <ClientSettingsEditor
                            settings={settings}
                            onSave={save}
                            onSaved={() => showToast('Saved the client defaults.')}
                            saveLabel="Save defaults"
                        />
                    </div>
                )}
            </div>
            {toast}
        </div>
    );
}
