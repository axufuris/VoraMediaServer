import { useMemo } from 'react';
import type { AvailableSectionVM, BackupSectionEstimateVM, BackupSizeEstimateVM } from '../../../api/System/backupsService';
import { estimateBackupTotal, formatBackupSize, formatRowCount, groupBackupSections } from './backupSections';

interface BackupSectionPickerProps {
    available: AvailableSectionVM[];
    includedKeys: string[] | null | undefined;
    onChange: (keys: string[] | null) => void;
    estimate: BackupSizeEstimateVM | null;
    estimating: boolean;
    estimateFailed: boolean;
    onReestimate: () => void;
}

export default function BackupSectionPicker({
    available,
    includedKeys,
    onChange,
    estimate,
    estimating,
    estimateFailed,
    onReestimate
}: BackupSectionPickerProps) {
    const allKeys = useMemo(() => available.map(s => s.key), [available]);
    const isAll = includedKeys === null || includedKeys === undefined;
    const includedSet = useMemo(() => new Set(isAll ? allKeys : includedKeys), [isAll, includedKeys, allKeys]);
    const groups = useMemo(() => groupBackupSections(available), [available]);
    const estimates = useMemo(() => {
        const byKey = new Map<string, BackupSectionEstimateVM>();
        estimate?.sections.forEach(s => byKey.set(s.key, s));
        return byKey;
    }, [estimate]);
    const total = useMemo(() => (estimate ? estimateBackupTotal(estimate, includedSet) : null), [estimate, includedSet]);

    const toggle = (key: string) => {
        const next = new Set(includedSet);
        if (next.has(key)) next.delete(key); else next.add(key);
        onChange(next.size === allKeys.length ? null : allKeys.filter(k => next.has(k)));
    };

    const selectedCount = includedSet.size;

    return (
        <div>
            <div className="flex items-center justify-between gap-3 mb-1">
                <div className="min-w-0">
                    <div className="text-xs font-semibold text-[var(--vora-text-muted)] uppercase tracking-wide">
                        Sections to include
                    </div>
                    <p className="text-xs text-[var(--vora-text-muted)] mt-0.5">
                        Unchecked sections are skipped by both scheduled and manual backups (e.g. skip Watch History to keep backups small).
                    </p>
                </div>
                <div className="flex items-center gap-3 shrink-0">
                    <span className="text-[11px] text-[var(--vora-text-muted)] tabular-nums">{selectedCount}/{allKeys.length}</span>
                    <div className="flex gap-2 text-[11px]">
                        <button type="button" onClick={() => onChange(null)} className="text-[var(--vora-accent-text)] hover:underline cursor-pointer">All</button>
                        <span className="text-[var(--vora-text-muted)]">·</span>
                        <button type="button" onClick={() => onChange([])} className="text-[var(--vora-accent-text)] hover:underline cursor-pointer">None</button>
                    </div>
                </div>
            </div>

            <div className="mt-3 p-3 rounded-[var(--vora-radius-md)] bg-[var(--vora-bg-sunken)]">
                <div className="flex flex-wrap items-center justify-between gap-2">
                    <div className="text-sm text-[var(--vora-text-primary)]" aria-live="polite">
                        {total ? (
                            selectedCount === 0 ? (
                                <span>No sections selected.</span>
                            ) : (
                                <>
                                    Estimated backup size: <span className="font-semibold">about {formatBackupSize(total.bytes)}</span>
                                    <span className="text-xs text-[var(--vora-text-muted)]"> · {formatRowCount(total.rows)}</span>
                                </>
                            )
                        ) : estimateFailed && !estimating ? (
                            <span className="text-[var(--vora-text-secondary)]">Couldn't estimate the backup size.</span>
                        ) : (
                            <span className="flex items-center gap-2 text-[var(--vora-text-secondary)]">
                                Estimating backup size…
                                <span className="vora-skeleton inline-block h-3 w-16" />
                            </span>
                        )}
                    </div>
                    <button
                        type="button"
                        onClick={onReestimate}
                        disabled={estimating}
                        className="text-[11px] text-[var(--vora-accent-text)] hover:underline cursor-pointer disabled:opacity-50 disabled:cursor-not-allowed"
                    >
                        {estimating && estimate ? 'Estimating…' : estimateFailed && !estimate ? 'Try again' : 'Re-estimate'}
                    </button>
                </div>
                {total?.incomplete && (
                    <p className="text-[11px] text-[var(--vora-warning-text)] mt-1">Some ticked sections couldn't be measured, so the real backup may be larger.</p>
                )}
                <p className="text-[11px] text-[var(--vora-text-muted)] mt-1">
                    An estimate of the compressed zip, measured from your data as it is now. It grows as your libraries, profiles and watch history grow.
                </p>
            </div>

            <div className="mt-4 space-y-4">
                {groups.map(({ group, label, sections }) => (
                    <div key={group}>
                        <div className="text-[11px] font-semibold text-[var(--vora-text-muted)] uppercase tracking-wide mb-2 pb-1 border-b border-[var(--vora-border-subtle)]">{label}</div>
                        <div className="grid grid-cols-1 xl:grid-cols-2 gap-x-6 gap-y-1">
                            {sections.map(s => (
                                <label key={s.key} className="flex items-start gap-2 cursor-pointer text-sm text-[var(--vora-text-primary)] py-1">
                                    <input
                                        type="checkbox"
                                        checked={includedSet.has(s.key)}
                                        onChange={() => toggle(s.key)}
                                        className="mt-0.5 shrink-0 cursor-pointer"
                                    />
                                    <span className="flex-1 min-w-0">
                                        <span>{s.displayName}</span>
                                        {s.canGrowLarge && (
                                            <span className="ml-1.5 text-[10px] px-1 py-0.5 rounded bg-[var(--vora-warning-soft)] text-[var(--vora-warning-text)] align-middle">
                                                large
                                            </span>
                                        )}
                                    </span>
                                    <SectionSize estimate={estimates.get(s.key)} loading={estimating && !estimate} />
                                </label>
                            ))}
                        </div>
                    </div>
                ))}
            </div>
        </div>
    );
}

function SectionSize({ estimate, loading }: { estimate: BackupSectionEstimateVM | undefined; loading: boolean }) {
    if (loading) return <span className="vora-skeleton inline-block h-3 w-10 mt-1 shrink-0" />;
    if (!estimate) return null;
    if (estimate.failed) {
        return <span className="text-[11px] text-[var(--vora-text-muted)] tabular-nums shrink-0 mt-0.5" title="Couldn't measure this section">—</span>;
    }
    return (
        <span className="text-[11px] text-[var(--vora-text-muted)] tabular-nums shrink-0 mt-0.5" title={formatRowCount(estimate.rowCount)}>
            {formatBackupSize(estimate.estimatedBytes)}
        </span>
    );
}
