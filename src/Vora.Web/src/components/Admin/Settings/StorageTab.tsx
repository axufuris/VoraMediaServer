import { useState } from 'react';
import { Link } from 'react-router-dom';
import { maintenanceService, type UnusedFileGroupVM, type UnusedFileKind, type UnusedFilesReportVM } from '../../../api/System/maintenanceService';
import { useDialog } from '../../../dialogs';
import { formatBytes } from '../../../utils/formatBytes';
import { resolveAdminPath } from '../Shell/adminNavData';

interface StorageTabProps {
    serverId?: string;
}

const KIND_TEXT: Record<UnusedFileKind, { label: string; detail: string }> = {
    Artwork: { label: 'Artwork and covers', detail: 'Uploaded artwork, poster overlays, collection and playlist covers, music art and episode stills.' },
    ProfilePictures: { label: 'Profile pictures', detail: 'Pictures uploaded for profiles.' },
    ScrubThumbnails: { label: 'Scrub-bar thumbnails', detail: 'Preview images shown while seeking.' },
    DownloadedSubtitles: { label: 'Downloaded subtitles', detail: 'Subtitles fetched from a subtitle provider.' },
    OriginalArtworkCache: { label: 'Original artwork', detail: 'Full-size posters kept to draw poster overlays on.' },
    SubtitleCache: { label: 'Subtitle cache', detail: 'Subtitles extracted from video files for streaming.' },
    RemovedPlugins: { label: 'Uninstalled plugins', detail: 'Plugin files left after an uninstall.' },
    UnfinishedFiles: { label: 'Unfinished files', detail: 'Temporary files over a day old, left by a restart or crash.' },
    Recordings: { label: 'Recordings Vora doesn’t know about', detail: 'Never removed here. Move or delete them yourself.' },
};

function fileCount(n: number): string {
    return `${n.toLocaleString()} ${n === 1 ? 'file' : 'files'}`;
}

export default function StorageTab({ serverId }: StorageTabProps) {
    const dialog = useDialog();
    const [report, setReport] = useState<UnusedFilesReportVM | null>(null);
    const [scanning, setScanning] = useState(false);
    const [removing, setRemoving] = useState(false);
    const [removalQueued, setRemovalQueued] = useState(false);

    const scan = async () => {
        setScanning(true);
        setRemovalQueued(false);
        try {
            setReport(await maintenanceService.scanUnusedFiles(serverId));
        } catch {
            await dialog.alert({ title: 'Scan failed', message: 'Vora couldn’t check its folders. Try again in a moment.', tone: 'danger' });
        } finally {
            setScanning(false);
        }
    };

    const remove = async () => {
        if (!report || report.removableFiles === 0) return;
        const ok = await dialog.confirm({
            title: 'Remove unused files?',
            message: `Remove ${fileCount(report.removableFiles)} (${formatBytes(report.removableBytes)}) that nothing in Vora uses? They can’t be brought back. A file only an old backup points to is removed too, so restore that backup first if you want it.`,
            confirmText: 'Remove files',
            tone: 'danger',
        });
        if (!ok) return;

        setRemoving(true);
        try {
            await maintenanceService.removeUnusedFiles(serverId);
            setRemovalQueued(true);
        } catch {
            await dialog.alert({ title: 'Could not start the clean-up', message: 'Try again in a moment.', tone: 'danger' });
        } finally {
            setRemoving(false);
        }
    };

    const found = report?.groups.filter(g => g.files > 0) ?? [];

    return (
        <section aria-labelledby="unused-files-heading" className="vora-card p-6">
            <div className="flex flex-wrap items-start justify-between gap-4">
                <div className="min-w-0">
                    <h2 id="unused-files-heading" className="text-base font-semibold text-[var(--vora-text-primary)]">Unused files</h2>
                    <p className="mt-1 max-w-2xl text-sm text-[var(--vora-text-muted)]">
                        Files Vora made that nothing in its database points to any more: left behind by deleted items and libraries, or by a reinstall that kept the data folder. Anything changed in the last hour is left alone.
                    </p>
                </div>
                <button type="button" onClick={scan} disabled={scanning} className="vora-button-secondary shrink-0 text-sm">
                    {scanning ? 'Scanning…' : report ? 'Scan again' : 'Scan for unused files'}
                </button>
            </div>

            {report && (
                <div className="mt-5">
                    {found.length === 0 ? (
                        <p className="text-sm text-[var(--vora-text-secondary)]">Nothing unused. Every file in Vora’s folders is in use.</p>
                    ) : (
                        <ul className="divide-y divide-[var(--vora-border-subtle)] rounded-[var(--vora-radius-md)] border border-[var(--vora-border-subtle)]">
                            {found.map(group => <GroupRow key={group.kind} group={group} />)}
                        </ul>
                    )}

                    {report.removableFiles > 0 && !removalQueued && (
                        <div className="mt-4 flex flex-wrap items-center justify-between gap-3">
                            <p className="text-sm text-[var(--vora-text-secondary)]">
                                {fileCount(report.removableFiles)} can be removed, freeing {formatBytes(report.removableBytes)}.
                            </p>
                            <button type="button" onClick={remove} disabled={removing} className="vora-button-primary text-sm">
                                {removing ? 'Starting…' : `Remove ${fileCount(report.removableFiles)}`}
                            </button>
                        </div>
                    )}

                    {removalQueued && (
                        <p role="status" className="mt-4 text-sm text-[var(--vora-text-secondary)]">
                            Removing unused files in the background. Follow it in{' '}
                            <Link to={resolveAdminPath('/admin/tasks', serverId)} className="text-[var(--vora-accent-text)] underline">Background Tasks</Link>, then scan again to check.
                        </p>
                    )}
                </div>
            )}
        </section>
    );
}

function GroupRow({ group }: { group: UnusedFileGroupVM }) {
    const text = KIND_TEXT[group.kind];
    return (
        <li className="flex flex-wrap items-start justify-between gap-3 px-4 py-3">
            <div className="min-w-0">
                <div className="text-sm font-medium text-[var(--vora-text-primary)]">{text.label}</div>
                <div className="text-xs text-[var(--vora-text-muted)]">{text.detail}</div>
                {group.folder && <div className="mt-1 truncate font-mono text-xs text-[var(--vora-text-disabled)]" title={group.folder}>{group.folder}</div>}
                {group.examples.length > 0 && (
                    <div className="mt-1 truncate text-xs text-[var(--vora-text-disabled)]" title={group.examples.join('\n')}>
                        e.g. {group.examples.join(', ')}
                    </div>
                )}
            </div>
            <div className="shrink-0 text-right text-sm tabular-nums">
                <div className="text-[var(--vora-text-primary)]">{fileCount(group.files)}</div>
                <div className="text-xs text-[var(--vora-text-muted)]">{formatBytes(group.bytes)}{group.removable ? '' : ' · left in place'}</div>
            </div>
        </li>
    );
}
