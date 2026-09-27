import { useEffect, useState, useCallback } from 'react';
import { Modal, ModalHeader, ModalBody, ModalFooter } from '../../Common/Modal';
import { filesystemService, type FileSystemRoot, type FileSystemListing } from '../../../api/System/filesystemService';
import { useDialog } from '../../../dialogs';

interface FolderBrowserModalProps {
    isOpen: boolean;
    onClose: () => void;
    onSelect: (path: string) => void;
    initialPath?: string;
    serverId?: string;
    title?: string;
}

export default function FolderBrowserModal({
    isOpen,
    onClose,
    onSelect,
    initialPath,
    serverId,
    title = 'Select a folder'
}: FolderBrowserModalProps) {
    const dialog = useDialog();
    const [roots, setRoots] = useState<FileSystemRoot[]>([]);
    const [listing, setListing] = useState<FileSystemListing | null>(null);
    const [loadingRoots, setLoadingRoots] = useState(false);
    const [loadingListing, setLoadingListing] = useState(false);
    const [selectedPath, setSelectedPath] = useState<string>('');

    const loadRoots = useCallback(async () => {
        setLoadingRoots(true);
        try {
            const data = await filesystemService.getRoots(serverId);
            setRoots(data);
            return data;
        } catch (err) {
            console.error('Failed to load filesystem roots', err);
            await dialog.alert('Failed to load filesystem roots.');
            return [];
        } finally {
            setLoadingRoots(false);
        }
    }, [serverId, dialog]);

    const loadListing = useCallback(async (path: string, options?: { silent?: boolean }): Promise<boolean> => {
        setLoadingListing(true);
        try {
            const data = await filesystemService.list(path, serverId);
            setListing(data);
            setSelectedPath(data.path);
            return true;
        } catch (err: unknown) {
            const e = err as { response?: { data?: { message?: string }, status?: number } };
            if (!options?.silent) {
                const message = e.response?.data?.message
                    || (e.response?.status === 403 ? 'That folder is outside the allowed media roots.' : 'Failed to list folder.');
                await dialog.alert(message);
            }
            return false;
        } finally {
            setLoadingListing(false);
        }
    }, [serverId, dialog]);

    useEffect(() => {
        if (!isOpen) return;
        let cancelled = false;
        (async () => {
            setListing(null);
            setSelectedPath('');

            const fetchedRoots = await loadRoots();
            if (cancelled) return;

            const startingPath = initialPath?.trim();
            if (startingPath) {
                const ok = await loadListing(startingPath, { silent: true });
                if (!ok && !cancelled && fetchedRoots.length === 1) {
                    await loadListing(fetchedRoots[0].path, { silent: true });
                }
            } else if (fetchedRoots.length === 1) {
                await loadListing(fetchedRoots[0].path, { silent: true });
            }
        })();
        return () => { cancelled = true; };
    }, [isOpen, initialPath, loadRoots, loadListing]);

    const handleConfirm = () => {
        if (!selectedPath) return;
        onSelect(selectedPath);
        onClose();
    };

    const breadcrumbs = (() => {
        if (!listing) return [];
        const matchedRoot = roots.find(r => listing.path === r.path || listing.path.startsWith(r.path + '/') || listing.path.startsWith(r.path + '\\'));
        if (!matchedRoot) return [{ label: listing.path, path: listing.path }];

        const rootPath = matchedRoot.path;
        const remainder = listing.path.slice(rootPath.length).replace(/^[/\\]+/, '');
        const crumbs: { label: string, path: string }[] = [{ label: matchedRoot.label, path: rootPath }];

        if (remainder.length > 0) {
            const segments = remainder.split(/[/\\]/);
            let acc = rootPath;
            const sep = rootPath.includes('\\') ? '\\' : '/';
            for (const seg of segments) {
                if (!seg) continue;
                acc = acc.endsWith(sep) ? `${acc}${seg}` : `${acc}${sep}${seg}`;
                crumbs.push({ label: seg, path: acc });
            }
        }
        return crumbs;
    })();

    return (
        <Modal
            isOpen={isOpen}
            onClose={onClose}
            size="2xl"
            surface="light"
            closeOnBackdropClick
            zIndex="z-[210]"
        >
            <ModalHeader
                title={title}
                subtitle="Pick a folder inside one of the allowed media roots."
                onClose={onClose}
                surface="light"
            />
            <ModalBody className="space-y-4">
                {!listing ? (
                    loadingRoots ? (
                        <div className="vora-skeleton h-48" />
                    ) : roots.length === 0 ? (
                        <p className="text-sm text-[var(--vora-text-muted)]">
                            No allowed media roots are configured for this server. Check the <code className="font-mono">FileSystemBrowser:AllowedRoots</code> config.
                        </p>
                    ) : (
                        <div className="space-y-2">
                            <p className="text-xs font-bold uppercase tracking-widest text-[var(--vora-text-muted)]">Mounted roots</p>
                            <div className="space-y-1.5">
                                {roots.map(r => (
                                    <button
                                        key={r.path}
                                        type="button"
                                        onClick={() => loadListing(r.path)}
                                        className="w-full flex items-center justify-between gap-3 px-3 py-2.5 rounded-[var(--vora-radius-md)] bg-[var(--vora-bg-sunken)] hover:bg-[var(--vora-border-subtle)] border border-[var(--vora-border-subtle)] cursor-pointer transition-colors text-left"
                                    >
                                        <span className="flex items-center gap-2 min-w-0">
                                            <FolderIcon className="w-4 h-4 text-[var(--vora-accent-text)] shrink-0" />
                                            <span className="font-medium text-[var(--vora-text-primary)]">{r.label}</span>
                                        </span>
                                        <span className="font-mono text-xs text-[var(--vora-text-muted)] truncate">{r.path}</span>
                                    </button>
                                ))}
                            </div>
                        </div>
                    )
                ) : (
                    <>
                        <div className="flex items-center gap-1.5 text-xs flex-wrap">
                            <button
                                type="button"
                                onClick={() => { setListing(null); setSelectedPath(''); }}
                                className="text-[var(--vora-accent-text)] hover:text-[var(--vora-accent-active)] font-semibold cursor-pointer"
                            >
                                Roots
                            </button>
                            {breadcrumbs.map((c, i) => (
                                <span key={c.path} className="flex items-center gap-1.5">
                                    <span className="text-[var(--vora-text-muted)]">/</span>
                                    {i === breadcrumbs.length - 1 ? (
                                        <span className="text-[var(--vora-text-primary)] font-semibold">{c.label}</span>
                                    ) : (
                                        <button
                                            type="button"
                                            onClick={() => loadListing(c.path)}
                                            className="text-[var(--vora-accent-text)] hover:text-[var(--vora-accent-active)] font-semibold cursor-pointer"
                                        >
                                            {c.label}
                                        </button>
                                    )}
                                </span>
                            ))}
                        </div>

                        <div className="font-mono text-[11px] text-[var(--vora-text-muted)] px-3 py-2 bg-[var(--vora-bg-sunken)] rounded-[var(--vora-radius-md)] break-all">
                            {listing.path}
                        </div>

                        {loadingListing ? (
                            <div className="vora-skeleton h-48" />
                        ) : (
                            <div className="border border-[var(--vora-border-subtle)] rounded-[var(--vora-radius-md)] overflow-hidden">
                                {listing.parentPath && (
                                    <button
                                        type="button"
                                        onClick={() => listing.parentPath && loadListing(listing.parentPath)}
                                        className="w-full flex items-center gap-2 px-3 py-2 text-sm text-[var(--vora-text-secondary)] hover:bg-[var(--vora-bg-sunken)] border-b border-[var(--vora-border-subtle)] cursor-pointer transition-colors"
                                    >
                                        <UpIcon className="w-4 h-4" />
                                        <span>.. (parent folder)</span>
                                    </button>
                                )}
                                {listing.folders.length === 0 ? (
                                    <div className="px-3 py-6 text-center text-sm text-[var(--vora-text-muted)] italic">
                                        This folder has no subfolders.
                                    </div>
                                ) : (
                                    <div className="max-h-72 overflow-y-auto">
                                        {listing.folders.map(folder => (
                                            <button
                                                key={folder.path}
                                                type="button"
                                                onDoubleClick={() => folder.hasChildren && loadListing(folder.path)}
                                                onClick={() => setSelectedPath(folder.path)}
                                                className={`w-full flex items-center justify-between gap-2 px-3 py-2 text-sm cursor-pointer transition-colors text-left border-b border-[var(--vora-border-subtle)] last:border-b-0 ${
                                                    selectedPath === folder.path
                                                        ? 'bg-[var(--vora-accent-soft)] text-[var(--vora-accent-text)]'
                                                        : 'hover:bg-[var(--vora-bg-sunken)] text-[var(--vora-text-primary)]'
                                                }`}
                                            >
                                                <span className="flex items-center gap-2 min-w-0">
                                                    <FolderIcon className="w-4 h-4 shrink-0 text-[var(--vora-accent-text)]" />
                                                    <span className="truncate">{folder.name}</span>
                                                </span>
                                                {folder.hasChildren && (
                                                    <button
                                                        type="button"
                                                        onClick={(e) => { e.stopPropagation(); loadListing(folder.path); }}
                                                        className="text-xs text-[var(--vora-text-muted)] hover:text-[var(--vora-accent-text)] px-2 py-0.5 rounded cursor-pointer shrink-0"
                                                    >
                                                        open ›
                                                    </button>
                                                )}
                                            </button>
                                        ))}
                                    </div>
                                )}
                            </div>
                        )}
                    </>
                )}
            </ModalBody>
            <ModalFooter surface="light" className="flex justify-between items-center gap-3">
                <div className="text-xs text-[var(--vora-text-muted)] truncate min-w-0 flex-1">
                    {selectedPath ? <>Selected: <span className="font-mono text-[var(--vora-text-secondary)]">{selectedPath}</span></> : 'Click a folder to select it. Double-click to open.'}
                </div>
                <div className="flex gap-2 shrink-0">
                    <button type="button" onClick={onClose} className="vora-button-secondary">Cancel</button>
                    <button
                        type="button"
                        onClick={handleConfirm}
                        disabled={!selectedPath}
                        className="vora-button-primary disabled:opacity-50 disabled:cursor-not-allowed"
                    >
                        Select folder
                    </button>
                </div>
            </ModalFooter>
        </Modal>
    );
}

function FolderIcon({ className }: { className?: string }) {
    return (
        <svg className={className} fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M3 7a2 2 0 012-2h4l2 2h8a2 2 0 012 2v8a2 2 0 01-2 2H5a2 2 0 01-2-2V7z" />
        </svg>
    );
}

function UpIcon({ className }: { className?: string }) {
    return (
        <svg className={className} fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M5 15l7-7 7 7" />
        </svg>
    );
}
