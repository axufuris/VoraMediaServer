import { useState } from 'react';
import { Link, useLocation } from 'react-router-dom';

// Sharing controls shared by the manual and smart playlist pages, so the two
// cannot drift into different ideas of what an owner or a viewer can do.
//
//   owner  -> a Share toggle. Shared means every profile on the server can see
//             it, read-only.
//   viewer -> whose it is, and Save a copy. Nothing here edits someone else's
//             playlist; every edit endpoint answers 404 to anyone but the owner.
interface PlaylistSharingControlsProps {
    isOwner: boolean;
    isShared: boolean;
    ownerName: string;
    onToggleShared: (next: boolean) => Promise<void>;
    onSaveCopy: () => Promise<void>;
}

export function PlaylistSharingControls({ isOwner, isShared, ownerName, onToggleShared, onSaveCopy }: PlaylistSharingControlsProps) {
    const [busy, setBusy] = useState(false);

    const run = async (action: () => Promise<void>) => {
        if (busy) return;
        setBusy(true);
        try { await action(); } finally { setBusy(false); }
    };

    if (isOwner) {
        return (
            <button
                type="button"
                onClick={() => run(() => onToggleShared(!isShared))}
                disabled={busy}
                aria-pressed={isShared}
                data-active={isShared}
                title={isShared
                    ? 'Everyone on this server can see and play this. Click to stop sharing.'
                    : 'Let everyone on this server see and play this. They can save a copy, but not change yours.'}
                className="vora-pill inline-flex cursor-pointer items-center gap-1.5 rounded-full px-3 py-1.5 text-xs font-semibold disabled:opacity-60"
            >
                <ShareIcon />
                {isShared ? 'Shared' : 'Share'}
            </button>
        );
    }

    return (
        <div className="flex flex-wrap items-center gap-3">
            {ownerName && (
                <span className="text-sm" style={{ color: 'var(--vora-text-muted)' }}>
                    by <span className="font-semibold" style={{ color: 'var(--vora-text-secondary)' }}>{ownerName}</span>
                </span>
            )}
            <button
                type="button"
                onClick={() => run(onSaveCopy)}
                disabled={busy}
                title="Add a copy to your own playlists. It's yours to change, and starts unshared."
                className="vora-pill inline-flex cursor-pointer items-center gap-1.5 rounded-full px-3 py-1.5 text-xs font-semibold disabled:opacity-60"
            >
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true">
                    <line x1="12" y1="5" x2="12" y2="19" /><line x1="5" y1="12" x2="19" y2="12" />
                </svg>
                {busy ? 'Saving…' : 'Save a copy'}
            </button>
        </div>
    );
}

// What a viewer sees when a shared playlist they had open has been deleted or
// unshared. Nothing is kept around to spare them this: tracks already queued
// keep playing, and the playlist itself is simply gone.
export function PlaylistUnavailable({ backTo }: { backTo: string }) {
    return (
        <div className="mx-auto mt-24 max-w-md px-6 text-center">
            <h1 className="text-xl font-bold" style={{ color: 'var(--vora-text-primary)' }}>This playlist isn't available anymore</h1>
            <p className="mt-2 text-sm" style={{ color: 'var(--vora-text-muted)' }}>
                Its owner deleted it or stopped sharing it.
            </p>
            <Link to={backTo} className="vora-button-primary mt-6 inline-block">Back to playlists</Link>
        </div>
    );
}

// Router state a copy is opened with, so the new playlist can confirm it was
// saved without the app needing a toast system.
export interface SavedCopyState {
    savedCopy?: boolean;
}

export function SavedCopyBanner() {
    const location = useLocation();
    const [dismissed, setDismissed] = useState(false);
    const state = location.state as SavedCopyState | null;
    if (!state?.savedCopy || dismissed) return null;

    return (
        <div
            role="status"
            className="mb-6 flex items-center justify-between gap-4 rounded-lg border px-4 py-2.5 text-sm"
            style={{ borderColor: 'var(--vora-border-subtle)', background: 'var(--vora-accent-soft)', color: 'var(--vora-text-primary)' }}
        >
            <span>Saved to your playlists. This copy is yours — change it however you like.</span>
            <button type="button" onClick={() => setDismissed(true)} className="vora-icon-button cursor-pointer rounded px-2" aria-label="Dismiss">×</button>
        </div>
    );
}

function ShareIcon() {
    return (
        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true">
            <circle cx="18" cy="5" r="3" />
            <circle cx="6" cy="12" r="3" />
            <circle cx="18" cy="19" r="3" />
            <line x1="8.59" y1="13.51" x2="15.42" y2="17.49" />
            <line x1="15.41" y1="6.51" x2="8.59" y2="10.49" />
        </svg>
    );
}
