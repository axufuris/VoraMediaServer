import type { RestoreBackupResult, RestoreSectionResult } from '../../../api/System/backupsService';

interface RestoreResultViewProps {
    result: RestoreBackupResult;
    sectionNames: Record<string, string>;
}

export default function RestoreResultView({ result, sectionNames }: RestoreResultViewProps) {
    const skipped = result.sections.reduce((sum, s) => sum + (s.restored ? s.rowsSkipped : 0), 0);

    return (
        <div className="space-y-3">
            <div className={`p-3 rounded-[var(--vora-radius-md)] text-sm font-semibold ${
                result.success
                    ? 'bg-[var(--vora-info-soft)] text-[var(--vora-info-text)]'
                    : 'bg-[var(--vora-danger-soft)] text-[var(--vora-danger-text)]'
            }`}>
                {result.success
                    ? skipped > 0
                        ? `Restore completed. ${skipped.toLocaleString('en-US')} row${skipped === 1 ? ' was' : 's were'} skipped — see the notes below.`
                        : 'Restore completed successfully.'
                    : `Restore failed: ${result.error || 'see section results'}`}
            </div>
            <div className="space-y-2">
                {result.sections.map(s => (
                    <SectionResult key={s.key} section={s} name={sectionNames[s.key] ?? s.key} />
                ))}
            </div>
        </div>
    );
}

function SectionResult({ section, name }: { section: RestoreSectionResult; name: string }) {
    return (
        <div className="text-xs bg-[var(--vora-bg-sunken)] px-3 py-2 rounded">
            <div className="flex items-center justify-between gap-3">
                <span className="font-semibold text-[var(--vora-text-primary)] min-w-0 truncate">{name}</span>
                <span className={`shrink-0 tabular-nums ${section.restored ? 'text-[var(--vora-info-text)]' : 'text-[var(--vora-danger-text)]'}`}>
                    {section.restored
                        ? `${section.rowsImported.toLocaleString('en-US')} restored${section.rowsSkipped > 0 ? ` · ${section.rowsSkipped.toLocaleString('en-US')} skipped` : ''}`
                        : section.error || 'not restored'}
                </span>
            </div>
            {section.warnings.length > 0 && (
                <ul className="mt-1.5 space-y-1 text-[11px] text-[var(--vora-warning-text)] list-disc pl-4">
                    {section.warnings.map(warning => <li key={warning}>{warning}</li>)}
                </ul>
            )}
        </div>
    );
}
