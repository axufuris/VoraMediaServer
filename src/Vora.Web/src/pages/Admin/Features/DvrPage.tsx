import { useEffect, useState, useCallback } from 'react';
import { useParams } from 'react-router-dom';
import { systemSettingsAdminService, type ServerSettings } from '../../../api/System/systemSettingsAdminService';
import FeatureToggle from '../../../components/Admin/Features/FeatureToggle';
import PageHeader from '../../../components/Admin/Primitives/PageHeader';
import { useDialog } from '../../../dialogs';
import FolderPathInput from '../../../components/Admin/FolderBrowser/FolderPathInput';

function SettingsCard({ title, description, children }: { title: string, description?: string, children: React.ReactNode }) {
    return (
        <section className="vora-card p-6">
            <h2 className="text-base font-semibold text-[var(--vora-text-primary)]">{title}</h2>
            {description && <p className="text-xs text-[var(--vora-text-muted)] mt-0.5 mb-4">{description}</p>}
            {!description && <div className="mb-4" />}
            {children}
        </section>
    );
}

function FieldLabel({ children }: { children: React.ReactNode }) {
    return <label className="block text-xs font-bold uppercase tracking-widest text-[var(--vora-text-muted)] mb-1.5">{children}</label>;
}

function FieldHint({ children }: { children: React.ReactNode }) {
    return <p className="text-xs text-[var(--vora-text-muted)] mt-1.5">{children}</p>;
}

export default function DvrPage() {
    const { serverId } = useParams<{ serverId?: string }>();
    const dialog = useDialog();
    const [serverSettings, setServerSettings] = useState<ServerSettings | null>(null);
    const [isSaving, setIsSaving] = useState(false);

    const load = useCallback(async () => {
        try {
            const data = await systemSettingsAdminService.getServerSettings(serverId);
            setServerSettings(data);
        } catch {
            dialog.alert('Failed to load DVR settings.');
        }
    }, [serverId, dialog]);

    useEffect(() => { load(); }, [load]);

    const handleSave = async (e: React.SyntheticEvent<HTMLFormElement>) => {
        e.preventDefault();
        if (!serverSettings) return;
        setIsSaving(true);
        try {
            await systemSettingsAdminService.updateServerSettings(serverSettings, serverId);
            await dialog.alert('DVR settings saved.');
        } catch {
            await dialog.alert('Failed to save DVR settings.');
        } finally {
            setIsSaving(false);
        }
    };

    const set = <K extends keyof ServerSettings>(key: K, value: ServerSettings[K]) => {
        if (!serverSettings) return;
        setServerSettings({ ...serverSettings, [key]: value });
    };

    return (
        <div data-vora-page="">
            <PageHeader
                title="DVR"
                description="Storage, recording defaults, conflict handling, and admin notifications."
            />

            <div className="px-8 pt-6 pb-10 max-w-6xl mx-auto">
                <FeatureToggle
                    featureKey="dvr"
                    label="Enable DVR"
                    description="When off, the DVR nav entry is hidden, scheduling new recordings is blocked, and the recording session endpoints return 403. DVR is also automatically disabled when Live TV is off. Existing recordings stay accessible to admins."
                    serverId={serverId}
                />

                {!serverSettings ? (
                    <div className="vora-skeleton h-48" />
                ) : (
                    <form onSubmit={handleSave} className="space-y-6">
                        <SettingsCard title="Storage">
                            <div className="space-y-5">
                                <div>
                                    <FieldLabel>Storage path</FieldLabel>
                                    <div className="max-w-xl">
                                        <FolderPathInput
                                            value={serverSettings.dvrStoragePath || ''}
                                            onChange={v => set('dvrStoragePath', v)}
                                            placeholder="/app/data/iptv/dvr"
                                            serverId={serverId}
                                            modalTitle="Select DVR storage folder"
                                        />
                                    </div>
                                    <FieldHint>
                                        Where recordings and post-processed MP4s are written. Leave blank to fall back to the StoragePaths:IptvDvr config value (default <code className="font-mono text-[var(--vora-text-secondary)]">/app/data/iptv/dvr</code>).
                                    </FieldHint>
                                </div>

                                <div className="grid grid-cols-2 gap-4 max-w-xl">
                                    <div>
                                        <FieldLabel>Max server-wide storage (GB)</FieldLabel>
                                        <input
                                            type="number"
                                            min={0}
                                            value={serverSettings.dvrMaxStorageGb}
                                            onChange={e => set('dvrMaxStorageGb', parseInt(e.target.value, 10) || 0)}
                                            className="vora-input"
                                        />
                                        <FieldHint>0 = unlimited. Separate from per-user quotas.</FieldHint>
                                    </div>
                                    <div>
                                        <FieldLabel>Warning threshold (%)</FieldLabel>
                                        <input
                                            type="number"
                                            min={0}
                                            max={100}
                                            value={serverSettings.dvrStorageWarningPercent}
                                            onChange={e => set('dvrStorageWarningPercent', parseInt(e.target.value, 10) || 90)}
                                            className="vora-input"
                                        />
                                        <FieldHint>% of max storage at which to surface a warning.</FieldHint>
                                    </div>
                                </div>

                                <div className="max-w-xl">
                                    <FieldLabel>Auto-delete watched recordings after (days)</FieldLabel>
                                    <input
                                        type="number"
                                        min={0}
                                        value={serverSettings.dvrAutoDeleteWatchedDays}
                                        onChange={e => set('dvrAutoDeleteWatchedDays', parseInt(e.target.value, 10) || 0)}
                                        className="vora-input w-32"
                                    />
                                    <FieldHint>0 = never. A nightly sweep removes recordings that have been fully watched older than this.</FieldHint>
                                </div>
                            </div>
                        </SettingsCard>

                        <SettingsCard title="Recording Defaults">
                            <div className="max-w-xl mb-5">
                                <FieldLabel>Default series retention</FieldLabel>
                                <input
                                    type="number"
                                    min={0}
                                    value={serverSettings.dvrDefaultSeriesRetention}
                                    onChange={e => set('dvrDefaultSeriesRetention', parseInt(e.target.value, 10) || 0)}
                                    className="vora-input w-32"
                                />
                                <FieldHint>Number of episodes to keep when scheduling a series. 0 = keep all. Clients can override per-schedule.</FieldHint>
                            </div>

                            <div className="grid grid-cols-2 gap-4 max-w-xl mb-5">
                                <div>
                                    <FieldLabel>Pre-roll padding (seconds)</FieldLabel>
                                    <input
                                        type="number"
                                        min={0}
                                        value={serverSettings.dvrPreRollSeconds}
                                        onChange={e => set('dvrPreRollSeconds', parseInt(e.target.value, 10) || 0)}
                                        className="vora-input"
                                    />
                                    <FieldHint>Start recording this many seconds before the scheduled program start. Default 120 (2 min).</FieldHint>
                                </div>
                                <div>
                                    <FieldLabel>Post-roll padding (seconds)</FieldLabel>
                                    <input
                                        type="number"
                                        min={0}
                                        value={serverSettings.dvrPostRollSeconds}
                                        onChange={e => set('dvrPostRollSeconds', parseInt(e.target.value, 10) || 0)}
                                        className="vora-input"
                                    />
                                    <FieldHint>Keep recording this many seconds after the scheduled program end. Default 300 (5 min).</FieldHint>
                                </div>
                            </div>

                            <div className="max-w-xl">
                                <FieldLabel>Conflict resolution policy</FieldLabel>
                                <select
                                    value={serverSettings.dvrConflictPolicy}
                                    onChange={e => set('dvrConflictPolicy', e.target.value)}
                                    className="vora-input cursor-pointer"
                                >
                                    <option value="AlwaysRecord">Always record (ignore tuner limits)</option>
                                    <option value="DropOldest">Drop oldest conflicting recording</option>
                                    <option value="DropNewest">Skip the new recording</option>
                                </select>
                                <FieldHint>
                                    Applies when a new session would push the count of overlapping pending/active recordings on a playlist above its Max Concurrent Streams (tuner) limit. Playlists with unlimited tuners are never affected.
                                </FieldHint>
                            </div>
                        </SettingsCard>

                        <SettingsCard title="Post-Processing">
                            <div className="space-y-2 text-sm text-[var(--vora-text-secondary)]">
                                <p>
                                    Recordings are automatically converted to MP4 using your{' '}
                                    <a className="text-[var(--vora-accent-text)] hover:text-[var(--vora-accent-active)] hover:underline" href={serverId ? `/server/${serverId}/admin/settings` : '/admin/settings'}>
                                        global transcoding settings
                                    </a>
                                    . The raw .ts file is deleted on successful conversion.
                                </p>
                                <p>
                                    If <code className="font-mono text-[var(--vora-text-primary)]">comskip</code> is installed in the container, commercial detection runs automatically after transcoding and writes markers to each recording.
                                </p>
                            </div>
                        </SettingsCard>

                        <SettingsCard
                            title="Notifications"
                            description="Alerts appear in the bell at the top of the admin shell and as toasts in real time."
                        >
                            <div className="space-y-3">
                                <label className="flex items-center gap-3 cursor-pointer select-none">
                                    <input
                                        type="checkbox"
                                        checked={serverSettings.dvrNotifyOnFailure}
                                        onChange={e => set('dvrNotifyOnFailure', e.target.checked)}
                                        className="w-4 h-4 accent-[var(--vora-accent-500)] cursor-pointer"
                                    />
                                    <span className="text-sm text-[var(--vora-text-primary)]">Notify admins when a recording fails</span>
                                </label>
                                <label className="flex items-center gap-3 cursor-pointer select-none">
                                    <input
                                        type="checkbox"
                                        checked={serverSettings.dvrNotifyOnStorageThreshold}
                                        onChange={e => set('dvrNotifyOnStorageThreshold', e.target.checked)}
                                        className="w-4 h-4 accent-[var(--vora-accent-500)] cursor-pointer"
                                    />
                                    <span className="text-sm text-[var(--vora-text-primary)]">Notify admins when storage reaches the warning threshold</span>
                                </label>
                            </div>
                        </SettingsCard>

                        <button type="submit" disabled={isSaving} className="vora-button-primary">
                            {isSaving ? 'Saving…' : 'Save DVR settings'}
                        </button>
                    </form>
                )}
            </div>
        </div>
    );
}
