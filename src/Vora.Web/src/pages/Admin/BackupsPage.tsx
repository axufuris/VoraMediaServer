import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useParams } from 'react-router-dom';
import {
    backupsService,
    type BackupSummaryVM,
    type BackupManifestVM,
    type BackupSettingsVM,
    type BackupCadence,
    type DayOfWeekName,
    type RestoreBackupResult,
    type AvailableSectionVM
} from '../../api/System/backupsService';
import { useSignalREvent } from '../../hooks/useSignalREvent';
import { useDialog } from '../../dialogs';
import PageHeader from '../../components/Admin/Primitives/PageHeader';
import EmptyState from '../../components/Admin/Primitives/EmptyState';

type TabId = 'backups' | 'settings';

const CADENCE_OPTIONS: BackupCadence[] = ['Off', 'Daily', 'Weekly', 'Monthly'];
const DAYS: DayOfWeekName[] = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];

function formatBytes(bytes: number): string {
    if (bytes === 0) return '0 B';
    const units = ['B', 'KB', 'MB', 'GB'];
    const i = Math.min(units.length - 1, Math.floor(Math.log(bytes) / Math.log(1024)));
    return `${(bytes / Math.pow(1024, i)).toFixed(i === 0 ? 0 : 2)} ${units[i]}`;
}

function formatDate(iso: string | null | undefined): string {
    if (!iso) return '—';
    return new Date(iso).toLocaleString();
}

export default function BackupsPage() {
    const { serverId } = useParams<{ serverId?: string }>();
    const [activeTab, setActiveTab] = useState<TabId>('backups');

    return (
        <div data-vora-page="">
            <PageHeader
                title="Backup & Restore"
                description="Create configuration snapshots, schedule automatic backups, and restore from a backup file. Configuration backups stay small; user-data sections can be large and are flagged accordingly."
            />

            <div className="px-8 pt-2 max-w-7xl mx-auto">
                <div className="flex gap-2 border-b border-[var(--vora-border-subtle)] mb-2">
                    <TabButton id="backups" activeTab={activeTab} setActiveTab={setActiveTab}>Backups</TabButton>
                    <TabButton id="settings" activeTab={activeTab} setActiveTab={setActiveTab}>Settings</TabButton>
                </div>
            </div>

            <div className="px-8 pb-10 max-w-7xl mx-auto pt-6">
                {activeTab === 'backups' && <BackupsTab serverId={serverId} />}
                {activeTab === 'settings' && <SettingsTab serverId={serverId} />}
            </div>
        </div>
    );
}

function TabButton({ id, activeTab, setActiveTab, children }: { id: TabId; activeTab: TabId; setActiveTab: (t: TabId) => void; children: React.ReactNode }) {
    const active = activeTab === id;
    return (
        <button
            type="button"
            onClick={() => setActiveTab(id)}
            className={`px-4 py-2.5 text-sm font-semibold border-b-2 -mb-px transition-colors cursor-pointer ${
                active
                    ? 'border-[var(--vora-accent-500)] text-[var(--vora-text-primary)]'
                    : 'border-transparent text-[var(--vora-text-muted)] hover:text-[var(--vora-text-primary)]'
            }`}
        >
            {children}
        </button>
    );
}

function BackupsTab({ serverId }: { serverId?: string }) {
    const dialog = useDialog();
    const [items, setItems] = useState<BackupSummaryVM[]>([]);
    const [loading, setLoading] = useState(true);
    const [busy, setBusy] = useState(false);
    const [restoreTarget, setRestoreTarget] = useState<string | null>(null);
    const fileInputRef = useRef<HTMLInputElement | null>(null);

    const reload = useCallback(async () => {
        setLoading(true);
        try {
            const list = await backupsService.list(serverId);
            setItems(list);
        } catch (err) {
            console.error('Failed to load backups', err);
        } finally {
            setLoading(false);
        }
    }, [serverId]);

    useEffect(() => { reload(); }, [reload]);

    useSignalREvent<string>('BackupCreated', useCallback(() => { reload(); }, [reload]));
    useSignalREvent<{ fileName: string; sectionKeys: string[] }>('BackupRestored', useCallback(() => { reload(); }, [reload]));

    const handleCreate = async () => {
        setBusy(true);
        try {
            await backupsService.create('manual', serverId);
            await reload();
        } catch (err) {
            console.error('Backup creation failed', err);
            await dialog.alert('Backup creation failed. Check the server logs for details.');
        } finally {
            setBusy(false);
        }
    };

    const handleDelete = async (fileName: string) => {
        if (!await dialog.confirm(`Delete backup "${fileName}"? This cannot be undone.`)) return;
        try {
            await backupsService.deleteBackup(fileName, serverId);
            await reload();
        } catch (err) {
            console.error('Delete failed', err);
            await dialog.alert('Failed to delete backup.');
        }
    };

    const handleDownload = async (fileName: string) => {
        try {
            const blob = await backupsService.download(fileName, serverId);
            const url = URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = fileName;
            document.body.appendChild(a);
            a.click();
            a.remove();
            URL.revokeObjectURL(url);
        } catch (err) {
            console.error('Download failed', err);
            await dialog.alert('Failed to download backup.');
        }
    };

    const handleUpload = async (file: File) => {
        setBusy(true);
        try {
            await backupsService.upload(file, serverId);
            await reload();
        } catch (err: unknown) {
            const e = err as { response?: { data?: { detail?: string; message?: string } } };
            await dialog.alert(e.response?.data?.detail || e.response?.data?.message || 'Upload failed.');
        } finally {
            setBusy(false);
            if (fileInputRef.current) fileInputRef.current.value = '';
        }
    };

    return (
        <>
            <div className="flex flex-wrap gap-2 justify-between items-center mb-4">
                <div className="text-sm text-[var(--vora-text-secondary)]">
                    {items.length} backup{items.length === 1 ? '' : 's'} stored on the server.
                </div>
                <div className="flex gap-2">
                    <input
                        ref={fileInputRef}
                        type="file"
                        accept=".zip"
                        className="hidden"
                        onChange={e => {
                            const file = e.target.files?.[0];
                            if (file) handleUpload(file);
                        }}
                    />
                    <button
                        type="button"
                        onClick={() => fileInputRef.current?.click()}
                        disabled={busy}
                        className="vora-button-secondary text-xs"
                    >
                        Upload Backup
                    </button>
                    <button
                        type="button"
                        onClick={handleCreate}
                        disabled={busy}
                        className="vora-button-primary text-xs"
                    >
                        {busy ? 'Working…' : 'Create Backup Now'}
                    </button>
                </div>
            </div>

            {loading ? (
                <div className="vora-skeleton h-48" />
            ) : items.length === 0 ? (
                <div className="vora-card">
                    <EmptyState
                        title="No backups yet"
                        description="Create your first backup to capture the current server configuration."
                        icon={<svg className="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M3 7a2 2 0 012-2h14a2 2 0 012 2v10a2 2 0 01-2 2H5a2 2 0 01-2-2V7z M12 11v4 M10 13h4" /></svg>}
                    />
                </div>
            ) : (
                <div className="space-y-2">
                    {items.map(item => (
                        <BackupRow
                            key={item.fileName}
                            item={item}
                            onRestore={() => setRestoreTarget(item.fileName)}
                            onDownload={() => handleDownload(item.fileName)}
                            onDelete={() => handleDelete(item.fileName)}
                        />
                    ))}
                </div>
            )}

            {restoreTarget && (
                <RestoreDrawer
                    fileName={restoreTarget}
                    serverId={serverId}
                    onClose={() => setRestoreTarget(null)}
                    onDone={() => { setRestoreTarget(null); reload(); }}
                />
            )}
        </>
    );
}

interface BackupRowProps {
    item: BackupSummaryVM;
    onRestore: () => void;
    onDownload: () => void;
    onDelete: () => void;
}

function BackupRow({ item, onRestore, onDownload, onDelete }: BackupRowProps) {
    return (
        <div className="vora-card p-4 flex flex-wrap items-center gap-3 justify-between">
            <div className="min-w-0 flex-1">
                <div className="font-mono text-sm text-[var(--vora-text-primary)] truncate">{item.fileName}</div>
                <div className="text-xs text-[var(--vora-text-muted)] mt-1 flex flex-wrap gap-3">
                    <span>{formatDate(item.createdAtUtc)}</span>
                    <span>{formatBytes(item.fileSizeBytes)}</span>
                    <span>{item.sectionCount} section{item.sectionCount === 1 ? '' : 's'}</span>
                    <span className="px-1.5 py-0.5 rounded bg-[var(--vora-bg-sunken)]">{item.reason}</span>
                    {!item.manifestReadable && (
                        <span className="px-1.5 py-0.5 rounded bg-[var(--vora-warning-soft)] text-[var(--vora-warning-text)]">
                            Manifest unreadable
                        </span>
                    )}
                </div>
            </div>
            <div className="flex gap-2 shrink-0">
                <button type="button" onClick={onRestore} className="vora-button-primary text-xs" disabled={!item.manifestReadable}>Restore…</button>
                <button type="button" onClick={onDownload} className="vora-button-secondary text-xs">Download</button>
                <button type="button" onClick={onDelete} className="text-xs font-semibold px-3 py-1.5 rounded-[var(--vora-radius-md)] cursor-pointer border border-[var(--vora-border-subtle)] text-[var(--vora-danger-text)] hover:bg-[var(--vora-danger-soft)]">Delete</button>
            </div>
        </div>
    );
}

interface RestoreDrawerProps {
    fileName: string;
    serverId?: string;
    onClose: () => void;
    onDone: () => void;
}

function RestoreDrawer({ fileName, serverId, onClose, onDone }: RestoreDrawerProps) {
    const dialog = useDialog();
    const [manifest, setManifest] = useState<BackupManifestVM | null>(null);
    const [loading, setLoading] = useState(true);
    const [selected, setSelected] = useState<Record<string, boolean>>({});
    const [acknowledgeAdminLoss, setAcknowledgeAdminLoss] = useState(false);
    const [typedConfirm, setTypedConfirm] = useState('');
    const [busy, setBusy] = useState(false);
    const [result, setResult] = useState<RestoreBackupResult | null>(null);

    useEffect(() => {
        (async () => {
            setLoading(true);
            try {
                const m = await backupsService.getManifest(fileName, serverId);
                setManifest(m);
                const initial: Record<string, boolean> = {};
                m.sections.forEach(s => { initial[s.key] = !s.requiresExplicitConfirm; });
                setSelected(initial);
            } catch (err) {
                console.error('Failed to load manifest', err);
                await dialog.alert('Failed to load backup manifest.');
                onClose();
            } finally {
                setLoading(false);
            }
        })();
    }, [fileName, serverId, dialog, onClose]);

    const grouped = useMemo(() => {
        if (!manifest) return {};
        const out: Record<string, typeof manifest.sections> = {};
        manifest.sections.forEach(s => {
            if (!out[s.group]) out[s.group] = [];
            out[s.group].push(s);
        });
        return out;
    }, [manifest]);

    const chosenCount = Object.values(selected).filter(Boolean).length;
    const hasDestructive = manifest?.sections.some(s => s.requiresExplicitConfirm && selected[s.key]) ?? false;
    const usersSelected = !!selected['users.profiles'];

    const canSubmit = chosenCount > 0 && typedConfirm.trim().toLowerCase() === 'restore' && !busy;

    const handleRestore = async () => {
        if (!manifest) return;
        setBusy(true);
        try {
            const res = await backupsService.restore(
                manifest.fileName,
                {
                    sectionKeys: manifest.sections.filter(s => selected[s.key]).map(s => s.key),
                    acknowledgeAdminLoss
                },
                serverId
            );
            setResult(res);
            if (res.success) {
                setTimeout(onDone, 1500);
            }
        } catch (err: unknown) {
            const e = err as { response?: { data?: { detail?: string } } };
            await dialog.alert(e.response?.data?.detail || 'Restore failed.');
        } finally {
            setBusy(false);
        }
    };

    return (
        <div className="fixed inset-0 z-[200] flex">
            <div className="flex-1 bg-black/40" onClick={onClose} />
            <div className="w-[640px] max-w-full h-full bg-[var(--vora-bg-canvas)] border-l border-[var(--vora-border-subtle)] flex flex-col">
                <div className="p-5 border-b border-[var(--vora-border-subtle)] flex items-start justify-between gap-4">
                    <div className="min-w-0">
                        <h2 className="text-base font-semibold text-[var(--vora-text-primary)]">Restore Backup</h2>
                        <p className="text-xs text-[var(--vora-text-muted)] mt-0.5 font-mono truncate">{fileName}</p>
                    </div>
                    <button
                        type="button"
                        onClick={onClose}
                        className="text-[var(--vora-text-muted)] hover:text-[var(--vora-text-primary)] cursor-pointer text-lg leading-none"
                        aria-label="Close"
                    >
                        ✕
                    </button>
                </div>

                <div className="flex-1 overflow-auto p-5 space-y-5">
                    {loading || !manifest ? (
                        <div className="vora-skeleton h-40" />
                    ) : result ? (
                        <RestoreResultView result={result} />
                    ) : (
                        <>
                            <div className="text-xs text-[var(--vora-text-secondary)]">
                                Created {formatDate(manifest.createdAtUtc)} · Vora {manifest.voraServerVersion} · {formatBytes(manifest.totalSizeBytes)} · {manifest.sections.length} sections
                            </div>

                            {Object.entries(grouped).map(([group, sections]) => (
                                <div key={group}>
                                    <div className="text-xs font-semibold text-[var(--vora-text-muted)] uppercase tracking-wide mb-2">{group}</div>
                                    <div className="space-y-2">
                                        {sections.map(s => (
                                            <label
                                                key={s.key}
                                                className={`block vora-card p-3 cursor-pointer transition-colors ${
                                                    s.requiresExplicitConfirm
                                                        ? 'border border-[var(--vora-danger-500)]/30'
                                                        : ''
                                                }`}
                                            >
                                                <div className="flex items-start gap-3">
                                                    <input
                                                        type="checkbox"
                                                        checked={!!selected[s.key]}
                                                        onChange={e => setSelected(prev => ({ ...prev, [s.key]: e.target.checked }))}
                                                        className="mt-0.5"
                                                    />
                                                    <div className="flex-1 min-w-0">
                                                        <div className="text-sm font-semibold text-[var(--vora-text-primary)]">{s.displayName}</div>
                                                        <div className="text-[11px] text-[var(--vora-text-muted)] font-mono">{s.key} · {formatBytes(s.sizeBytes)}</div>
                                                        {s.destructiveWarning && (
                                                            <div className="mt-2 text-[11px] text-[var(--vora-danger-text)] bg-[var(--vora-danger-soft)]/40 p-2 rounded">
                                                                <span className="font-semibold">Destructive — </span>{s.destructiveWarning}
                                                            </div>
                                                        )}
                                                    </div>
                                                </div>
                                            </label>
                                        ))}
                                    </div>
                                </div>
                            ))}

                            {usersSelected && (
                                <label className="flex items-start gap-2 text-xs text-[var(--vora-warning-text)] bg-[var(--vora-warning-soft)]/50 p-3 rounded-[var(--vora-radius-md)]">
                                    <input
                                        type="checkbox"
                                        checked={acknowledgeAdminLoss}
                                        onChange={e => setAcknowledgeAdminLoss(e.target.checked)}
                                        className="mt-0.5"
                                    />
                                    <span>
                                        I understand that if my admin account is not in this backup, restoring the Users &amp; Profiles section will lock me out of the server.
                                    </span>
                                </label>
                            )}

                            {hasDestructive && (
                                <div className="text-xs text-[var(--vora-danger-text)] bg-[var(--vora-danger-soft)]/30 p-3 rounded-[var(--vora-radius-md)]">
                                    Type <span className="font-mono font-bold">restore</span> to confirm.
                                    <input
                                        type="text"
                                        value={typedConfirm}
                                        onChange={e => setTypedConfirm(e.target.value)}
                                        className="vora-input text-xs w-full mt-2"
                                        placeholder="restore"
                                    />
                                </div>
                            )}
                            {!hasDestructive && chosenCount > 0 && (
                                <div className="text-xs text-[var(--vora-text-secondary)] bg-[var(--vora-bg-sunken)] p-3 rounded-[var(--vora-radius-md)]">
                                    Type <span className="font-mono font-bold">restore</span> to confirm.
                                    <input
                                        type="text"
                                        value={typedConfirm}
                                        onChange={e => setTypedConfirm(e.target.value)}
                                        className="vora-input text-xs w-full mt-2"
                                        placeholder="restore"
                                    />
                                </div>
                            )}
                        </>
                    )}
                </div>

                {!result && (
                    <div className="p-5 border-t border-[var(--vora-border-subtle)] flex items-center justify-between">
                        <div className="text-xs text-[var(--vora-text-muted)]">
                            {chosenCount} section{chosenCount === 1 ? '' : 's'} selected
                        </div>
                        <div className="flex gap-2">
                            <button type="button" onClick={onClose} className="vora-button-secondary text-xs">Cancel</button>
                            <button
                                type="button"
                                onClick={handleRestore}
                                disabled={!canSubmit}
                                className="vora-button-primary text-xs disabled:opacity-50 disabled:cursor-not-allowed"
                            >
                                {busy ? 'Restoring…' : 'Restore Selected'}
                            </button>
                        </div>
                    </div>
                )}
            </div>
        </div>
    );
}

function RestoreResultView({ result }: { result: RestoreBackupResult }) {
    return (
        <div className="space-y-3">
            <div className={`p-3 rounded-[var(--vora-radius-md)] text-sm font-semibold ${
                result.success
                    ? 'bg-[var(--vora-info-soft)] text-[var(--vora-info-text)]'
                    : 'bg-[var(--vora-danger-soft)] text-[var(--vora-danger-text)]'
            }`}>
                {result.success ? 'Restore completed successfully.' : `Restore failed: ${result.error || 'see section results'}`}
            </div>
            <div className="space-y-1">
                {result.sections.map(s => (
                    <div key={s.key} className="text-xs flex items-center justify-between bg-[var(--vora-bg-sunken)] px-3 py-2 rounded">
                        <span className="font-mono">{s.key}</span>
                        <span className={s.restored ? 'text-[var(--vora-info-text)]' : 'text-[var(--vora-danger-text)]'}>
                            {s.restored ? `${s.rowsImported} imported` : s.error || 'failed'}
                        </span>
                    </div>
                ))}
            </div>
        </div>
    );
}

function SettingsTab({ serverId }: { serverId?: string }) {
    const dialog = useDialog();
    const [settings, setSettings] = useState<BackupSettingsVM | null>(null);
    const [loading, setLoading] = useState(true);
    const [saving, setSaving] = useState(false);

    const load = useCallback(async () => {
        setLoading(true);
        try {
            const s = await backupsService.getSettings(serverId);
            setSettings(s);
        } catch (err) {
            console.error('Failed to load settings', err);
        } finally {
            setLoading(false);
        }
    }, [serverId]);

    useEffect(() => { load(); }, [load]);

    const save = async () => {
        if (!settings) return;
        setSaving(true);
        try {
            const updated = await backupsService.updateSettings(settings, serverId);
            setSettings(updated);
        } catch {
            await dialog.alert('Failed to save settings.');
        } finally {
            setSaving(false);
        }
    };

    if (loading || !settings) return <div className="vora-skeleton h-64" />;

    const update = <K extends keyof BackupSettingsVM>(key: K, value: BackupSettingsVM[K]) => {
        setSettings(prev => prev ? { ...prev, [key]: value } : prev);
    };

    return (
        <div className="space-y-5">
            <div className="vora-card p-5 flex flex-wrap items-center justify-between gap-4">
                <label className="flex items-center gap-3 cursor-pointer min-w-0">
                    <input
                        type="checkbox"
                        checked={settings.autoBackupEnabled}
                        onChange={e => update('autoBackupEnabled', e.target.checked)}
                        className="w-4 h-4"
                    />
                    <span className="min-w-0">
                        <span className="block text-sm font-semibold text-[var(--vora-text-primary)]">Enable automatic backups</span>
                        <span className="block text-xs text-[var(--vora-text-muted)] mt-0.5">
                            Vora will create a backup on the schedule below and prune the oldest entries above the retention count.
                        </span>
                    </span>
                </label>
                <div className="flex items-center gap-6">
                    <div className="text-xs text-[var(--vora-text-muted)] text-right">
                        <div><span className="text-[var(--vora-text-secondary)]">Last:</span> {formatDate(settings.lastSuccessfulRunUtc)}</div>
                        <div><span className="text-[var(--vora-text-secondary)]">Next:</span> {formatDate(settings.nextScheduledRunUtc)}</div>
                    </div>
                    <button type="button" onClick={save} disabled={saving} className="vora-button-primary text-xs">
                        {saving ? 'Saving…' : 'Save Settings'}
                    </button>
                </div>
            </div>

            <div className="grid grid-cols-1 lg:grid-cols-5 gap-5 items-start">
                <div className="lg:col-span-2 vora-card p-5 space-y-4">
                    <div className="text-xs font-semibold text-[var(--vora-text-muted)] uppercase tracking-wide">Schedule</div>

                    <div>
                        <label className="block text-xs font-semibold text-[var(--vora-text-muted)] uppercase tracking-wide mb-1">Cadence</label>
                        <select
                            value={settings.cadence}
                            onChange={e => update('cadence', e.target.value as BackupCadence)}
                            className="vora-input text-sm w-full"
                        >
                            {CADENCE_OPTIONS.map(c => <option key={c} value={c}>{c}</option>)}
                        </select>
                    </div>

                    {settings.cadence !== 'Off' && (
                        <div className="grid grid-cols-2 gap-3">
                            <div>
                                <label className="block text-xs font-semibold text-[var(--vora-text-muted)] uppercase tracking-wide mb-1">Hour (local)</label>
                                <input type="number" min={0} max={23} value={settings.hour} onChange={e => update('hour', Math.max(0, Math.min(23, Number(e.target.value))))} className="vora-input text-sm w-full" />
                            </div>
                            <div>
                                <label className="block text-xs font-semibold text-[var(--vora-text-muted)] uppercase tracking-wide mb-1">Minute</label>
                                <input type="number" min={0} max={59} value={settings.minute} onChange={e => update('minute', Math.max(0, Math.min(59, Number(e.target.value))))} className="vora-input text-sm w-full" />
                            </div>
                            {settings.cadence === 'Weekly' && (
                                <div className="col-span-2">
                                    <label className="block text-xs font-semibold text-[var(--vora-text-muted)] uppercase tracking-wide mb-1">Day of week</label>
                                    <select value={settings.dayOfWeek} onChange={e => update('dayOfWeek', e.target.value as DayOfWeekName)} className="vora-input text-sm w-full">
                                        {DAYS.map(d => <option key={d} value={d}>{d}</option>)}
                                    </select>
                                </div>
                            )}
                            {settings.cadence === 'Monthly' && (
                                <div className="col-span-2">
                                    <label className="block text-xs font-semibold text-[var(--vora-text-muted)] uppercase tracking-wide mb-1">Day of month (1–28)</label>
                                    <input type="number" min={1} max={28} value={settings.dayOfMonth} onChange={e => update('dayOfMonth', Math.max(1, Math.min(28, Number(e.target.value))))} className="vora-input text-sm w-full" />
                                </div>
                            )}
                        </div>
                    )}

                    <div className="pt-3 border-t border-[var(--vora-border-subtle)]">
                        <label className="block text-xs font-semibold text-[var(--vora-text-muted)] uppercase tracking-wide mb-1">Backups to keep</label>
                        <input
                            type="number"
                            min={1}
                            max={100}
                            value={settings.maxToKeep}
                            onChange={e => update('maxToKeep', Math.max(1, Number(e.target.value)))}
                            className="vora-input text-sm w-full"
                        />
                        <p className="text-xs text-[var(--vora-text-muted)] mt-1">Older backups beyond this count are auto-pruned after each new backup.</p>
                    </div>

                    <div>
                        <label className="block text-xs font-semibold text-[var(--vora-text-muted)] uppercase tracking-wide mb-1">Override backup directory</label>
                        <input
                            type="text"
                            value={settings.overrideDirectory || ''}
                            onChange={e => update('overrideDirectory', e.target.value)}
                            placeholder={`Default: ${settings.effectiveDirectory}`}
                            className="vora-input text-sm w-full"
                        />
                        <p className="text-xs text-[var(--vora-text-muted)] mt-1">
                            Must be a path the container can write to. Leave blank to use the default.
                        </p>
                    </div>

                    <div className="text-xs bg-[var(--vora-warning-soft)]/40 text-[var(--vora-warning-text)] p-3 rounded-[var(--vora-radius-md)]">
                        Backup files include DataProtection keys, which decrypt your stored SMTP password. Store backup files like passwords.
                    </div>
                </div>

                <div className="lg:col-span-3 vora-card p-5">
                    <SectionPicker
                        available={settings.availableSections}
                        includedKeys={settings.includedSectionKeys}
                        onChange={keys => update('includedSectionKeys', keys)}
                    />
                </div>
            </div>
        </div>
    );
}

interface SectionPickerProps {
    available: AvailableSectionVM[];
    includedKeys: string[] | null | undefined;
    onChange: (keys: string[] | null) => void;
}

function SectionPicker({ available, includedKeys, onChange }: SectionPickerProps) {
    const allKeys = useMemo(() => available.map(s => s.key), [available]);
    const isAll = includedKeys === null || includedKeys === undefined;
    const includedSet = useMemo(() => new Set(isAll ? allKeys : includedKeys), [isAll, includedKeys, allKeys]);

    const grouped = useMemo(() => {
        const out: Record<string, AvailableSectionVM[]> = {};
        available.forEach(s => {
            if (!out[s.group]) out[s.group] = [];
            out[s.group].push(s);
        });
        return out;
    }, [available]);

    const toggle = (key: string) => {
        const next = new Set(includedSet);
        if (next.has(key)) next.delete(key); else next.add(key);
        if (next.size === allKeys.length) {
            onChange(null);
        } else {
            onChange(allKeys.filter(k => next.has(k)));
        }
    };

    const selectAll = () => onChange(null);
    const selectNone = () => onChange([]);

    const selectedCount = includedSet.size;
    const totalCount = allKeys.length;

    return (
        <div>
            <div className="flex items-center justify-between mb-1">
                <div>
                    <div className="text-xs font-semibold text-[var(--vora-text-muted)] uppercase tracking-wide">
                        Sections to include
                    </div>
                    <p className="text-xs text-[var(--vora-text-muted)] mt-0.5">
                        Unchecked sections are skipped by both scheduled and manual backups (e.g. skip Watch History to keep backups small).
                    </p>
                </div>
                <div className="flex items-center gap-3 shrink-0">
                    <span className="text-[11px] text-[var(--vora-text-muted)] tabular-nums">{selectedCount}/{totalCount}</span>
                    <div className="flex gap-2 text-[11px]">
                        <button type="button" onClick={selectAll} className="text-[var(--vora-accent-text)] hover:underline cursor-pointer">All</button>
                        <span className="text-[var(--vora-text-muted)]">·</span>
                        <button type="button" onClick={selectNone} className="text-[var(--vora-accent-text)] hover:underline cursor-pointer">None</button>
                    </div>
                </div>
            </div>
            <div className="mt-3 space-y-4">
                {Object.entries(grouped).map(([group, sections]) => (
                    <div key={group}>
                        <div className="text-[11px] font-semibold text-[var(--vora-text-muted)] uppercase tracking-wide mb-2 pb-1 border-b border-[var(--vora-border-subtle)]">{group}</div>
                        <div className="grid grid-cols-1 sm:grid-cols-2 xl:grid-cols-3 gap-x-4 gap-y-2">
                            {sections.map(s => (
                                <label key={s.key} className="flex items-start gap-2 cursor-pointer text-sm text-[var(--vora-text-primary)] py-1">
                                    <input
                                        type="checkbox"
                                        checked={includedSet.has(s.key)}
                                        onChange={() => toggle(s.key)}
                                        className="mt-0.5 shrink-0"
                                    />
                                    <span className="min-w-0">
                                        <span>{s.displayName}</span>
                                        {s.requiresExplicitConfirm && (
                                            <span className="ml-1.5 text-[10px] px-1 py-0.5 rounded bg-[var(--vora-warning-soft)] text-[var(--vora-warning-text)] align-middle">
                                                large
                                            </span>
                                        )}
                                    </span>
                                </label>
                            ))}
                        </div>
                    </div>
                ))}
            </div>
        </div>
    );
}
