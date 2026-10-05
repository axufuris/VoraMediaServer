import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { backupsService, type BackupCadence, type BackupSettingsVM, type DayOfWeekName } from '../../../../api/System/backupsService';
import { resolveAdminPath } from '../../../../components/Admin/Shell/adminNavData';
import type { SetupStepProps } from '../setupContext';
import { useStepSaver } from '../setupSaver';
import { FieldLabel, Hint, SetupCard, StatusMessage, StepHeading, SwitchRow, WhyNote } from '../SetupParts';

const CADENCES: { value: BackupCadence; label: string }[] = [
    { value: 'Daily', label: 'Every day' },
    { value: 'Weekly', label: 'Every week' },
    { value: 'Monthly', label: 'Every month' },
];

const DAYS: DayOfWeekName[] = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];

const pad = (value: number) => String(value).padStart(2, '0');

export function BackupsStep({ serverId }: SetupStepProps) {
    const [backup, setBackup] = useState<BackupSettingsVM | null>(null);
    const [loadFailed, setLoadFailed] = useState(false);
    const [dirty, setDirty] = useState(false);

    useEffect(() => {
        let cancelled = false;
        backupsService.getSettings(serverId)
            .then(loaded => { if (!cancelled) setBackup(loaded); })
            .catch(() => { if (!cancelled) setLoadFailed(true); });
        return () => { cancelled = true; };
    }, [serverId]);

    useStepSaver(useCallback(async () => {
        if (!dirty || !backup) return;
        setBackup(await backupsService.updateSettings(backup, serverId));
        setDirty(false);
    }, [dirty, backup, serverId]));

    const patch = (next: Partial<BackupSettingsVM>) => {
        setBackup(current => current ? { ...current, ...next } : current);
        setDirty(true);
    };

    const setTime = (value: string) => {
        const [hour, minute] = value.split(':').map(part => parseInt(part, 10));
        if (Number.isNaN(hour) || Number.isNaN(minute)) return;
        patch({ hour, minute });
    };

    return (
        <>
            <StepHeading
                eyebrow="Backups"
                title="Backups"
                lead="A backup is a zip of Vora's settings, plugins, users and their history, so a failed disk or a bad change doesn't mean setting everything up again. Your media files aren't in it."
            />
            {loadFailed && <StatusMessage tone="error">Couldn't load the backup settings. You can set them later under Backup & Restore.</StatusMessage>}
            {!backup && !loadFailed && <div className="vora-skeleton h-32" />}
            {backup && (
                <>
                    <SetupCard>
                        <SwitchRow
                            id="setup-backups-on"
                            label="Back up automatically"
                            description="Vora makes a new backup on a schedule and deletes the oldest once there are more than you keep."
                            checked={backup.autoBackupEnabled}
                            onChange={on => patch({ autoBackupEnabled: on, cadence: on && backup.cadence === 'Off' ? 'Daily' : backup.cadence })}
                        />
                        {backup.autoBackupEnabled && (
                            <div className="grid gap-4 border-t border-dashed border-[var(--vora-border-strong)] pt-4 sm:grid-cols-2 xl:grid-cols-4">
                                <div>
                                    <FieldLabel htmlFor="setup-backups-cadence">How often</FieldLabel>
                                    <select id="setup-backups-cadence" value={backup.cadence} onChange={e => patch({ cadence: e.target.value as BackupCadence })} className="vora-input cursor-pointer">
                                        {CADENCES.map(c => <option key={c.value} value={c.value}>{c.label}</option>)}
                                    </select>
                                </div>
                                {backup.cadence === 'Weekly' && (
                                    <div>
                                        <FieldLabel htmlFor="setup-backups-day">On</FieldLabel>
                                        <select id="setup-backups-day" value={backup.dayOfWeek} onChange={e => patch({ dayOfWeek: e.target.value as DayOfWeekName })} className="vora-input cursor-pointer">
                                            {DAYS.map(d => <option key={d} value={d}>{d}</option>)}
                                        </select>
                                    </div>
                                )}
                                {backup.cadence === 'Monthly' && (
                                    <div>
                                        <FieldLabel htmlFor="setup-backups-date">Day of the month</FieldLabel>
                                        <input id="setup-backups-date" type="number" min={1} max={28} value={backup.dayOfMonth} onChange={e => patch({ dayOfMonth: Math.min(28, Math.max(1, parseInt(e.target.value, 10) || 1)) })} className="vora-input" />
                                    </div>
                                )}
                                <div>
                                    <FieldLabel htmlFor="setup-backups-time">At</FieldLabel>
                                    <input id="setup-backups-time" type="time" value={`${pad(backup.hour)}:${pad(backup.minute)}`} onChange={e => setTime(e.target.value)} className="vora-input" />
                                </div>
                                <div>
                                    <FieldLabel htmlFor="setup-backups-keep">Backups to keep</FieldLabel>
                                    <input id="setup-backups-keep" type="number" min={1} max={100} value={backup.maxToKeep} onChange={e => patch({ maxToKeep: Math.min(100, Math.max(1, parseInt(e.target.value, 10) || 1)) })} className="vora-input" />
                                </div>
                            </div>
                        )}
                    </SetupCard>
                    <WhyNote>
                        Backups are saved to <span className="font-mono text-[var(--vora-text-primary)]">{backup.effectiveDirectory}</span> inside the container. Make sure that folder is on a mounted volume, or the backups disappear when the container is rebuilt.
                    </WhyNote>
                    <Hint>
                        Choose what goes into each backup, make one now or restore one under <Link to={resolveAdminPath('/admin/backups', serverId)} className="font-semibold text-[var(--vora-accent-text)] hover:underline">Backup & Restore</Link>. Times follow the time zone set in Your server.
                    </Hint>
                </>
            )}
        </>
    );
}
