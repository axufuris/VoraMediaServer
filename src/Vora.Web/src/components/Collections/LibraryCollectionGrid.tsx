import { useState, type ReactNode } from 'react';
import type { CollectionHiddenReason, CollectionSummary } from '../../api/Collections/collectionService';
import MediaCard from '../Client/Primitives/MediaCard';
import MediaGrid from '../Client/Primitives/MediaGrid';
import { CornerChip } from '../Client/Primitives/WatchedBadge';

const HIDDEN_LABEL: Record<CollectionHiddenReason, string> = {
    Empty: 'Empty',
    AutomaticCollectionsHidden: 'Automatic off',
    BelowMinimumSize: 'Below minimum',
    OutOfSeason: 'Out of season',
    Hidden: 'Hidden',
};

interface LibraryCollectionGridProps {
    collections: CollectionSummary[];
    isAdmin: boolean;
    onOpen: (collection: CollectionSummary) => void;
    empty: ReactNode;
}

// A library's collections, or the global ones. Viewers only ever receive the
// visible ones; an admin receives the hidden ones too (hidden by an admin, out
// of season, below the library's minimum, automatic collections switched off,
// or empty) and can show them, each marked with why it is hidden.
export default function LibraryCollectionGrid({ collections, isAdmin, onOpen, empty }: LibraryCollectionGridProps) {
    const [showHidden, setShowHidden] = useState(false);
    const hiddenCount = collections.filter(c => c.hiddenReason).length;
    const shown = showHidden ? collections : collections.filter(c => !c.hiddenReason);

    return (
        <>
            {isAdmin && hiddenCount > 0 && (
                <div className="mb-4 flex flex-wrap items-center justify-end gap-3">
                    <span className="text-xs text-[var(--vora-text-muted)]">Marked collections are hidden from viewers.</span>
                    <button
                        type="button"
                        role="switch"
                        aria-checked={showHidden}
                        data-active={showHidden}
                        onClick={() => setShowHidden(v => !v)}
                        className="vora-pill cursor-pointer rounded-full px-3 py-1 text-xs font-medium"
                    >
                        {showHidden ? 'Hide hidden' : `Show hidden (${hiddenCount})`}
                    </button>
                </div>
            )}
            {shown.length === 0 ? empty : (
                <MediaGrid>
                    {shown.map(collection => (
                        <MediaCard
                            key={collection.id}
                            item={{ type: 'Collection', title: collection.title, itemCount: collection.itemCount }}
                            imageUrl={collection.posterUrl}
                            badge={collection.hiddenReason ? <CornerChip label={HIDDEN_LABEL[collection.hiddenReason]} hiddenFromViewers /> : undefined}
                            onClick={() => onOpen(collection)}
                            fill
                        />
                    ))}
                </MediaGrid>
            )}
        </>
    );
}
