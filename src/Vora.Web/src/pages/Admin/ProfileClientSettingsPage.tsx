import { useEffect, useState } from 'react';
import { Link, useParams, useSearchParams } from 'react-router-dom';
import PageHeader from '../../components/Admin/Primitives/PageHeader';
import FeatureTabs from '../../components/Admin/Features/FeatureTabs';
import { useActionToast } from '../../components/Admin/Primitives/ActionToast';
import { resolveAdminPath } from '../../components/Admin/Shell/adminNavData';
import ClientSettingsEditor from '../../components/Common/ClientSettings/ClientSettingsEditor';
import ProfileDevicesPanel from '../../components/Admin/Users/ProfileDevicesPanel';
import { profileService, type ClientSettingsVM, type SaveClientSettingsRequest } from '../../api/Users/profileService';
import { userService } from '../../api/Users/userService';
import { resolveReason } from '../../utils/apiError';

type TabKey = 'profile' | 'devices';

const TABS = [
    { key: 'profile', label: 'Profile' },
    { key: 'devices', label: 'Devices' },
];

interface Loaded {
    profileName: string;
    ownerName: string;
    settings: ClientSettingsVM;
}

export default function ProfileClientSettingsPage() {
    const { serverId, profileId } = useParams<{ serverId?: string; profileId: string }>();
    const [searchParams, setSearchParams] = useSearchParams();
    const { toast, showToast } = useActionToast();
    const [loaded, setLoaded] = useState<Loaded | null>(null);
    const [error, setError] = useState<string | null>(null);

    const tab: TabKey = searchParams.get('tab') === 'devices' ? 'devices' : 'profile';
    const openDeviceId = searchParams.get('device');

    useEffect(() => {
        if (!profileId) return;
        let cancelled = false;
        Promise.all([
            userService.getAllUsers(serverId),
            profileService.adminGetClientSettings(profileId, serverId),
        ])
            .then(([users, settings]) => {
                if (cancelled) return;
                const owner = users.find(user => user.profiles.some(profile => profile.id === profileId));
                const profile = owner?.profiles.find(candidate => candidate.id === profileId);
                if (!owner || !profile) {
                    setError('This profile no longer exists.');
                    return;
                }
                setLoaded({ profileName: profile.name, ownerName: owner.displayName, settings });
            })
            .catch(err => { if (!cancelled) setError(resolveReason(err) ?? "Could not read this profile's settings."); });
        return () => { cancelled = true; };
    }, [profileId, serverId]);

    const chooseTab = (key: string) => setSearchParams(key === 'devices' ? { tab: 'devices' } : {}, { replace: true });

    const save = (request: SaveClientSettingsRequest) => profileService.adminSaveClientSettings(profileId ?? '', request, serverId);

    return (
        <div data-vora-page="">
            <PageHeader
                title={loaded ? `Client settings for ${loaded.profileName}` : 'Client settings'}
                description={loaded && loaded.ownerName !== loaded.profileName
                    ? `A profile of ${loaded.ownerName}. These are its own preferences, the same ones it sets in Settings, and it can change them again.`
                    : "A profile's own preferences, the same ones it sets in Settings. The profile can change them again."}
                actions={<Link to={resolveAdminPath('/admin/users', serverId)} className="vora-button-secondary">Back to Users &amp; Access</Link>}
            />
            <div className="p-8 max-md:p-4 max-w-6xl mx-auto">
                {error && <p role="alert" className="m-0 text-sm text-[var(--vora-danger-text)]">{error}</p>}
                {!error && !loaded && <div className="vora-skeleton h-64" />}
                {loaded && profileId && (
                    <>
                        <FeatureTabs tabs={TABS} activeKey={tab} onChange={chooseTab} />
                        {tab === 'profile' ? (
                            <section className="vora-card max-w-3xl p-6">
                                <p className="m-0 mb-6 text-sm text-[var(--vora-text-muted)]">Playback behaviour, audio and subtitles, and library sort. They apply on every device the profile uses.</p>
                                <ClientSettingsEditor
                                    settings={loaded.settings}
                                    onSave={save}
                                    onSaved={() => showToast(`Saved the client settings for ${loaded.profileName}.`)}
                                />
                            </section>
                        ) : (
                            <section className="flex flex-col gap-5">
                                <p className="m-0 text-sm text-[var(--vora-text-muted)]">
                                    Bitrate and resolution limits, the skip and volume steps and the sounds are kept for each device. A device that has not saved its own follows the server&apos;s defaults.
                                    {' '}Give a device a nickname on the <Link to={resolveAdminPath('/admin/devices', serverId)} className="font-semibold text-[var(--vora-accent-text)] hover:underline">Authorized Devices</Link> page to tell similar ones apart.
                                </p>
                                <ProfileDevicesPanel profileId={profileId} serverId={serverId} openDeviceId={openDeviceId} />
                            </section>
                        )}
                    </>
                )}
            </div>
            {toast}
        </div>
    );
}
