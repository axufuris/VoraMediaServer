import { useEffect, useState, useCallback, useRef } from 'react';
import { isAxiosError } from 'axios';
import { useParams, useNavigate } from 'react-router-dom';
import { collectionService, type CollectionDetails, type CollectionSortOrder } from '../../../api/Collections/collectionService';
import { collectionAdminService } from '../../../api/Collections/collectionAdminService';
import EditCollectionModal from '../../../components/Collections/EditCollectionModal';
import ReorderCollectionModal from '../../../components/Collections/ReorderCollectionModal';
import MediaCard from '../../../components/Client/Primitives/MediaCard';
import MediaGrid from '../../../components/Client/Primitives/MediaGrid';
import { useSignalREvent } from '../../../hooks/useSignalREvent';
import { useDialog } from '../../../dialogs';
import CinematicBackdrop from '../../../components/Client/Primitives/CinematicBackdrop';
import { StorageKeys } from '../../../utils/storageKeys';

const SORT_LABELS: Record<CollectionSortOrder, string> = {
    Chronological: 'Chronological (Timeline)',
    ReleaseDateAsc: 'Release Date (Oldest First)',
    ReleaseDateDesc: 'Release Date (Newest First)',
    DateAddedDesc: 'Date Added',
    Alphabetical: 'Alphabetical (A–Z)',
};

const USER_SORT_OPTIONS: CollectionSortOrder[] = ['ReleaseDateAsc', 'ReleaseDateDesc', 'DateAddedDesc', 'Alphabetical'];

function getSortOptions(defaultSort: CollectionSortOrder, hasChronological: boolean): CollectionSortOrder[] {
    const order: CollectionSortOrder[] = ['Chronological', 'ReleaseDateAsc', 'ReleaseDateDesc', 'DateAddedDesc', 'Alphabetical'];
    return order.filter(s =>
        USER_SORT_OPTIONS.includes(s)
        || s === defaultSort
        || (s === 'Chronological' && hasChronological)
    );
}

export default function CollectionDetailsPage() {
    const dialog = useDialog();
    const { serverId, id } = useParams<{ serverId?: string, id: string }>();
    const navigate = useNavigate();
    const [collection, setCollection] = useState<CollectionDetails | null>(null);
    const [loading, setLoading] = useState(true);
    const [sort, setSort] = useState<CollectionSortOrder | null>(null);
    const sortRef = useRef<CollectionSortOrder | null>(null);

    const [isSyncing, setIsSyncing] = useState(false);
    const [isEditModalOpen, setIsEditModalOpen] = useState(false);
    const [isReorderModalOpen, setIsReorderModalOpen] = useState(false);
    const isAdmin = localStorage.getItem(StorageKeys.isServerAdmin) === 'true';

    const fetchCollection = useCallback((silent = false) => {
        if (!id) return;
        if (!silent) setLoading(true);
        collectionService.getCollectionDetails(id, serverId, sortRef.current ?? undefined)
            .then(data => {
                setCollection(data);
                if (sortRef.current === null) {
                    sortRef.current = data.defaultSort;
                    setSort(data.defaultSort);
                }
            })
            .catch(console.error)
            .finally(() => {
                if (!silent) setLoading(false);
            });
    }, [id, serverId]);

    useEffect(() => {
        sortRef.current = null;
        setSort(null);
    }, [id]);

    useEffect(() => {
        fetchCollection();
    }, [fetchCollection]);

    const handleSortChange = (newSort: CollectionSortOrder) => {
        if (newSort === sortRef.current) return;
        sortRef.current = newSort;
        setSort(newSort);
        fetchCollection(true);
    };

    useSignalREvent("CollectionUpdated", useCallback((updatedId: string) => {
        if (id && updatedId.toLowerCase() === id.toLowerCase()) {
            console.log("Collection background task finished! Silently reloading data...");
            fetchCollection(true);
        }
    }, [id, fetchCollection]));

    const handleSyncTimeline = async () => {
        if (!collection || !collection.sortProviderId) return;

        // Syncing re-derives the story year of every unlocked item from
        // scratch — it is not an incremental top-up — so say what it will do
        // before spending the provider call and overwriting hand-checked order.
        const lockedCount = collection.items?.filter(i => i.inUniverseYearLocked).length ?? 0;
        const ok = await dialog.confirm({
            title: 'Re-check the timeline?',
            message: lockedCount > 0
                ? `Every item's place in the story order will be worked out again from scratch. ${lockedCount} locked ${lockedCount === 1 ? 'item keeps its' : 'items keep their'} current position; everything else may move.`
                : "Every item's place in the story order will be worked out again from scratch, and anything may move. Lock an item first if you want to keep where you put it.",
            confirmText: 'Re-check timeline',
            cancelText: 'Cancel',
        });
        if (!ok) return;

        setIsSyncing(true);
        try {
            await collectionAdminService.syncChronology(collection.id);
            fetchCollection();
        } catch (err) {
            let message = 'Failed to sync timeline. Check the provider settings and try again.';
            if (isAxiosError(err)) {
                const data = err.response?.data as { error?: string; message?: string } | undefined;
                if (data?.error) message = data.error;
                else if (data?.message) message = data.message;
            }
            await dialog.alert(message);
        } finally {
            setIsSyncing(false);
        }
    };

    const handleRemoveItem = async (e: React.MouseEvent, mediaId: string) => {
        e.stopPropagation();

        const message = collection?.contentSyncProviderId
            ? "Remove this item from the collection? It will also be excluded from future syncs, so the list sync won't add it back."
            : "Are you sure you want to remove this item from the collection?";
        if (!await dialog.confirm(message)) {
            return;
        }

        try {
            await collectionAdminService.removeFromCollection(collection!.id, mediaId);
            fetchCollection(true);
        } catch (error) {
            console.error(error);
            await dialog.alert("Failed to remove item.");
        }
    };

    if (loading) return <div className="p-12 text-center text-[var(--vora-text-muted)] mt-16">Loading collection...</div>;
    if (!collection) return <div className="p-12 text-center text-[var(--vora-danger-500)] mt-16">Collection not found.</div>;

    return (
        <div className="relative min-h-full pb-16">

            {collection && (
                <EditCollectionModal
                    isOpen={isEditModalOpen}
                    onClose={() => setIsEditModalOpen(false)}
                    onSaved={fetchCollection}
                    onDeleted={() => navigate(serverId ? `/server/${serverId}` : '/')}
                    collection={collection}
                />
            )}

            {collection && (
                <ReorderCollectionModal
                    isOpen={isReorderModalOpen}
                    onClose={() => setIsReorderModalOpen(false)}
                    onSaved={fetchCollection}
                    collection={collection}
                />
            )}

            <div className="absolute inset-x-0 top-0 z-0">
                <CinematicBackdrop src={collection.backdropUrl || collection.posterUrl} intensity="detail" parallax transitionKey={collection.id} />
            </div>

            <div className="relative z-10 w-full flex-1 px-12 pt-8">
                <button
                    type="button"
                    onClick={() => navigate(-1)}
                    className="mb-8 inline-flex cursor-pointer items-center gap-2 rounded-full px-3 py-1.5 text-sm font-medium backdrop-blur-md transition-colors hover:bg-[rgba(20,20,28,0.85)]"
                    style={{ background: 'rgba(20, 20, 28, 0.65)', border: '1px solid rgba(255, 255, 255, 0.14)', color: '#fafafa' }}
                >
                    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><polyline points="15 18 9 12 15 6" /></svg>
                    Back
                </button>

                <div className="flex flex-col md:flex-row gap-10 mb-16">
                    <div className="w-64 shrink-0">
                        <div className="aspect-[2/3] rounded-lg overflow-hidden shadow-2xl border border-[var(--vora-border-subtle)] bg-[var(--vora-bg-sunken)]">
                            {collection.posterUrl ? (
                                <img src={collection.posterUrl} alt={collection.title} className="w-full h-full object-cover" />
                            ) : (
                                <div className="w-full h-full flex items-center justify-center text-[var(--vora-text-muted)]">No Image</div>
                            )}
                        </div>
                    </div>

                    <div className="flex-1 pt-4">
                        <div className="flex items-center gap-4 mb-4">
                            <h1 className="text-5xl font-bold text-[var(--vora-text-primary)] drop-shadow-lg">{collection.title}</h1>

                            {isAdmin && (
                                <div className="flex gap-2">
                                    <button
                                        onClick={() => setIsEditModalOpen(true)}
                                        className="p-2 bg-[var(--vora-bg-sunken)] hover:bg-[var(--vora-accent-hover)] rounded-full text-[var(--vora-text-muted)] hover:text-[var(--vora-text-primary)] transition-colors shadow-lg cursor-pointer"
                                        title="Edit Metadata"
                                    >
                                        <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15.232 5.232l3.536 3.536m-2.036-5.036a2.5 2.5 0 113.536 3.536L6.5 21.036H3v-3.572L16.732 3.732z" /></svg>
                                    </button>

                                    {isAdmin && collection.defaultSort === 'Chronological' && (
                                        <button
                                            onClick={() => setIsReorderModalOpen(true)}
                                            className="p-2 bg-[var(--vora-bg-sunken)] hover:bg-[var(--vora-accent-hover)] rounded-full text-[var(--vora-text-muted)] hover:text-[var(--vora-text-primary)] transition-colors shadow-lg cursor-pointer"
                                            title="Manual Sort Order"
                                        >
                                            <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 6h16M4 12h16M4 18h7" /></svg>
                                        </button>
                                    )}

                                    {collection.sortProviderId && (
                                        <button
                                            onClick={handleSyncTimeline}
                                            disabled={isSyncing}
                                            className="px-4 py-2 bg-blue-600 hover:bg-blue-500 disabled:opacity-50 rounded-full text-sm font-bold text-[var(--vora-text-primary)] transition-colors shadow-lg flex items-center gap-2 cursor-pointer"
                                        >
                                            {isSyncing ? (
                                                <span className="animate-pulse">Syncing...</span>
                                            ) : (
                                                <>
                                                    <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" /></svg>
                                                    Sync Timeline
                                                </>
                                            )}
                                        </button>
                                    )}
                                </div>
                            )}
                        </div>

                        <p className="text-lg text-[var(--vora-text-secondary)] leading-relaxed max-w-4xl shadow-sm">
                            {collection.description || "No description available."}
                        </p>
                    </div>
                </div>

                <div>
                    <div className="flex items-center justify-between gap-4 mb-6 border-b border-[var(--vora-border-subtle)] pb-2">
                        <h2 className="text-2xl font-bold text-[var(--vora-text-primary)]">
                            Items in Collection ({collection.itemCount})
                        </h2>

                        {collection.itemCount > 1 && (
                            <div className="flex items-center gap-2 shrink-0">
                                <label htmlFor="collection-sort" className="text-sm font-medium text-[var(--vora-text-muted)]">Sort by</label>
                                <select
                                    id="collection-sort"
                                    value={sort ?? collection.defaultSort}
                                    onChange={e => handleSortChange(e.target.value as CollectionSortOrder)}
                                    className="cursor-pointer rounded-md border border-[var(--vora-border-subtle)] bg-[var(--vora-bg-raised)] p-2 text-sm text-[var(--vora-text-primary)] outline-none focus:border-[var(--vora-accent-500)]"
                                >
                                    {getSortOptions(collection.defaultSort, !!collection.sortProviderId || collection.defaultSort === 'Chronological').map(option => (
                                        <option key={option} value={option}>{SORT_LABELS[option]}</option>
                                    ))}
                                </select>
                            </div>
                        )}
                    </div>

                    <MediaGrid>
                        {collection.items?.map(item => (
                            <MediaCard
                                key={item.id}
                                item={item}
                                imageUrl={item.posterUrl}
                                isPlayed={item.isPlayed}
                                unplayedCount={item.unplayedItemCount}
                                onClick={() => navigate(serverId ? `/server/${serverId}/media/${item.id}` : `/media/${item.id}`)}
                                onRemove={isAdmin && !collection.systemGenerated ? (e) => handleRemoveItem(e, item.id) : undefined}
                                fill
                            />
                        ))}
                    </MediaGrid>
                </div>
            </div>
        </div>
    );
}