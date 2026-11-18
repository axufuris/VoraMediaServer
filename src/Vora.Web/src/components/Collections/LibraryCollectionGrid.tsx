import { useState, type ReactNode } from 'react';
import type { CollectionHiddenReason, CollectionSummary } from '../../api/Collections/collectionService';
import { collectionAdminService } from '../../api/Collections/collectionAdminService';
import { useDialog } from '../../dialogs';
import { errorDetail } from '../../utils/apiError';
import ActionMenu from '../Client/Primitives/ActionMenu';
import MediaCard from '../Client/Primitives/MediaCard';
import MediaGrid from '../Client/Primitives/MediaGrid';
import { menuAction, type MenuEntry, type MenuPoint } from '../Client/Primitives/menuEntries';
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
    serverId?: string;
    onOpen: (collection: CollectionSummary) => void;
    // After an admin hid or showed a collection from its tile's menu.
    onHiddenChanged: (collection: CollectionSummary, hidden: boolean) => void;
    empty: ReactNode;
}

interface OpenMenu {
    at: MenuPoint;
    collection: CollectionSummary;
}

// A library's collections, or the global ones. Viewers only ever receive the
// visible ones; an admin receives the hidden ones too (hidden by an admin, out
// of season, below the library's minimum, automatic collections switched off,
// or empty) and can show them, each marked with why it is hidden. An admin
// right-clicks or holds a tile to hide it from viewers or show it again;
// viewers keep the browser's own menu.
export default function LibraryCollectionGrid({ collections, isAdmin, serverId, onOpen, onHiddenChanged, empty }: LibraryCollectionGridProps) {
    const dialog = useDialog();
    const [showHidden, setShowHidden] = useState(false);
    const [menu, setMenu] = useState<OpenMenu | null>(null);
    const hiddenCount = collections.filter(c => c.hiddenReason).length;
    const shown = showHidden ? collections : collections.filter(c => !c.hiddenReason);

    const setHidden = async (collection: CollectionSummary, hidden: boolean) => {
        try {
            await collectionAdminService.setHidden(collection.id, hidden, serverId);
            onHiddenChanged(collection, hidden);
        } catch (error) {
            await dialog.alert(errorDetail(error, hidden ? `Couldn't hide ${collection.title}` : `Couldn't show ${collection.title}`));
        }
    };

    const menuEntries = (collection: CollectionSummary): MenuEntry[] => {
        const hiddenByAdmin = collection.hiddenReason === 'Hidden';
        return [menuAction(
            'hidden',
            hiddenByAdmin ? 'Show to viewers' : 'Hide from viewers',
            () => { void setHidden(collection, !hiddenByAdmin); },
        )];
    };

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
                            onMenu={isAdmin ? at => setMenu({ at, collection }) : undefined}
                            fill
                        />
                    ))}
                </MediaGrid>
            )}
            {menu && (
                <ActionMenu
                    at={menu.at}
                    title={menu.collection.title}
                    entries={menuEntries(menu.collection)}
                    onClose={() => setMenu(null)}
                />
            )}
        </>
    );
}
