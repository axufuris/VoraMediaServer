import { useEffect, useState } from 'react';
import { Modal, ModalHeader } from '../../Common/Modal';
import { profileService, type ClearedViewingHistoryVM, type UserProfileVM, type ViewingHistoryVM } from '../../../api/Users/profileService';
import { resolveReason } from '../../../utils/apiError';

interface ClearViewingHistoryModalProps {
    profile: UserProfileVM;
    serverId?: string;
    onClose: () => void;
}

const counted = (count: number, one: string, many: string) => `${count.toLocaleString()} ${count === 1 ? one : many}`;

function watchSummary(history: ViewingHistoryVM): string {
    if (history.watchedCount === 0 && history.inProgressCount === 0) return 'Nothing watched yet';
    return `${counted(history.watchedCount, 'title', 'titles')} watched, ${history.inProgressCount.toLocaleString()} in progress`;
}

function clearedSummary(name: string, cleared: ClearedViewingHistoryVM, picked: { watchHistory: boolean; ratings: boolean }): string {
    const parts: string[] = [];
    if (picked.watchHistory) parts.push(`the watch history of ${counted(cleared.watchStatesRemoved, 'title', 'titles')}`);
    if (picked.ratings) parts.push(counted(cleared.ratingsRemoved, 'rating', 'ratings'));
    return `Removed ${parts.join(' and ')} from ${name}.`;
}

export default function ClearViewingHistoryModal({ profile, serverId, onClose }: ClearViewingHistoryModalProps) {
    const [history, setHistory] = useState<ViewingHistoryVM | null>(null);
    const [watchHistory, setWatchHistory] = useState(true);
    const [ratings, setRatings] = useState(true);
    const [clearing, setClearing] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [cleared, setCleared] = useState<string | null>(null);

    useEffect(() => {
        let cancelled = false;
        profileService.adminGetViewingHistory(profile.id, serverId)
            .then(result => { if (!cancelled) setHistory(result); })
            .catch(err => { if (!cancelled) setError(resolveReason(err) ?? "Could not read this profile's history."); });
        return () => { cancelled = true; };
    }, [profile.id, serverId]);

    const nothingToClear = history !== null && history.watchedCount + history.inProgressCount + history.ratingCount === 0;
    const canClear = history !== null && (watchHistory || ratings) && !clearing;

    const handleClear = async () => {
        if (!canClear) return;
        setClearing(true);
        setError(null);
        try {
            const result = await profileService.adminClearViewingHistory(profile.id, { watchHistory, ratings }, serverId);
            setCleared(clearedSummary(profile.name, result, { watchHistory, ratings }));
        } catch (err) {
            setError(resolveReason(err) ?? 'Could not clear the history.');
        } finally {
            setClearing(false);
        }
    };

    return (
        <Modal isOpen onClose={onClose} size="md" surface="light" cardClassName="p-6">
            <ModalHeader title="Clear viewing history" subtitle={profile.name} onClose={onClose} closeDisabled={clearing} bordered={false} surface="light" />

            {cleared ? (
                <div className="space-y-4 px-5">
                    <p role="status" className="text-sm text-[var(--vora-text-primary)]">{cleared}</p>
                    <p className="text-xs text-[var(--vora-text-muted)]">They can import their own history again from Account settings, signed in to their own Plex account.</p>
                    <div className="flex justify-end">
                        <button type="button" onClick={onClose} className="vora-button-primary">Done</button>
                    </div>
                </div>
            ) : (
                <div className="space-y-4 px-5">
                    <p className="text-sm text-[var(--vora-text-secondary)]">
                        Removes what this profile has watched and rated in movies and TV, for example after an import brought in someone else's. Music, podcasts, playlists and the watchlist stay as they are. This can't be undone.
                    </p>

                    {history === null && !error ? (
                        <div className="vora-skeleton h-24" />
                    ) : history && (
                        <fieldset className="space-y-3 rounded-[var(--vora-radius-md)] border border-[var(--vora-border-subtle)] bg-[var(--vora-bg-sunken)] p-4">
                            <legend className="sr-only">What to clear</legend>
                            <label className="flex items-start gap-3 cursor-pointer">
                                <input type="checkbox" checked={watchHistory} onChange={e => setWatchHistory(e.target.checked)} className="mt-0.5 h-4 w-4 shrink-0 cursor-pointer" />
                                <span className="flex flex-col">
                                    <span className="text-sm text-[var(--vora-text-primary)]">Watch history</span>
                                    <span className="text-xs text-[var(--vora-text-muted)]">{watchSummary(history)}. Clears played marks, resume points and Continue Watching.</span>
                                </span>
                            </label>
                            <label className="flex items-start gap-3 cursor-pointer">
                                <input type="checkbox" checked={ratings} onChange={e => setRatings(e.target.checked)} className="mt-0.5 h-4 w-4 shrink-0 cursor-pointer" />
                                <span className="flex flex-col">
                                    <span className="text-sm text-[var(--vora-text-primary)]">Ratings</span>
                                    <span className="text-xs text-[var(--vora-text-muted)]">{history.ratingCount === 0 ? 'Nothing rated yet' : counted(history.ratingCount, 'rating', 'ratings')}.</span>
                                </span>
                            </label>
                        </fieldset>
                    )}

                    {nothingToClear && <p className="text-xs text-[var(--vora-text-muted)]">This profile has no watch history or ratings to clear.</p>}
                    {error && <p role="alert" className="text-sm text-[var(--vora-danger-text)]">{error}</p>}

                    <div className="flex justify-end gap-2 pt-2">
                        <button type="button" onClick={onClose} disabled={clearing} className="vora-button-secondary">Cancel</button>
                        <button
                            type="button"
                            onClick={handleClear}
                            disabled={!canClear || nothingToClear}
                            className="rounded-[var(--vora-radius-md)] border border-transparent px-4 py-2 text-sm font-semibold bg-[var(--vora-danger-soft)] text-[var(--vora-danger-text)] enabled:hover:bg-[var(--vora-danger-500)] enabled:hover:text-[var(--vora-text-primary)] transition-colors cursor-pointer disabled:opacity-50 disabled:cursor-not-allowed"
                        >
                            {clearing ? 'Clearing…' : 'Clear history'}
                        </button>
                    </div>
                </div>
            )}
        </Modal>
    );
}
