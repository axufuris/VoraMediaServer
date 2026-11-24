import { useEffect, useState } from 'react';
import { Modal, ModalBody, ModalHeader } from '../../Common/Modal';
import ClientSettingsEditor from '../../Common/ClientSettings/ClientSettingsEditor';
import ProfileDevicesPanel from './ProfileDevicesPanel';
import { profileService, type ClientSettingsVM, type SaveClientSettingsRequest, type UserProfileVM } from '../../../api/Users/profileService';
import { resolveReason } from '../../../utils/apiError';

interface ProfileClientSettingsModalProps {
    profile: UserProfileVM;
    serverId?: string;
    onClose: () => void;
    onSaved: (message: string) => void;
}

export default function ProfileClientSettingsModal({ profile, serverId, onClose, onSaved }: ProfileClientSettingsModalProps) {
    const [settings, setSettings] = useState<ClientSettingsVM | null>(null);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        let cancelled = false;
        profileService.adminGetClientSettings(profile.id, serverId)
            .then(result => { if (!cancelled) setSettings(result); })
            .catch(err => { if (!cancelled) setError(resolveReason(err) ?? "Could not read this profile's settings."); });
        return () => { cancelled = true; };
    }, [profile.id, serverId]);

    const save = (request: SaveClientSettingsRequest) => profileService.adminSaveClientSettings(profile.id, request, serverId);

    return (
        <Modal isOpen onClose={onClose} size="3xl" surface="light">
            <ModalHeader
                title={`Client settings for ${profile.name}`}
                subtitle="These are the profile's own preferences, the same ones it sets in Settings. It can change them again, and they apply on every device it uses."
                onClose={onClose}
                surface="light"
            />
            <ModalBody>
                {error && <p role="alert" className="m-0 text-sm text-[var(--vora-danger-text)]">{error}</p>}
                {!error && !settings && <div className="vora-skeleton h-48" />}
                {settings && (
                    <div className="space-y-8">
                        <ClientSettingsEditor
                            settings={settings}
                            onSave={save}
                            onSaved={() => onSaved(`Saved the client settings for ${profile.name}.`)}
                        />
                        <section className="border-t border-[var(--vora-border-subtle)] pt-6">
                            <h3 className="m-0 mb-1 text-sm font-semibold text-[var(--vora-text-primary)]">Devices</h3>
                            <p className="m-0 mb-4 text-xs text-[var(--vora-text-muted)]">
                                Bitrate and resolution limits, the skip and volume steps and the sounds are kept for each device. A device that has not saved its own follows the server's defaults.
                            </p>
                            <ProfileDevicesPanel profileId={profile.id} serverId={serverId} />
                        </section>
                    </div>
                )}
            </ModalBody>
        </Modal>
    );
}
