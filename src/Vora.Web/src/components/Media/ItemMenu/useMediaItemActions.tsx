import { useEffect, useState, type ReactNode } from 'react';
import type { MediaItem } from '../../../api/Media/mediaService';
import { libraryAdminService, type MediaMatchResult } from '../../../api/Media/libraryAdminService';
import { watchlistService } from '../../../api/Watchlist/watchlistService';
import { useDialog } from '../../../dialogs';
import { StorageKeys } from '../../../utils/storageKeys';
import { yearOf } from '../../../utils/serverTime';
import { isFullyWatched } from '../../../utils/watchState';
import EditMetadataModal from '../EditMetadataModal';
import FixMatchModal from '../FixMatchModal';
import MediaInfoDialog from '../MediaInfoDialog';
import MarkerEditorModal from '../../Admin/MarkerEditorModal';
import AddToCollectionModal from '../../Collections/AddToCollectionModal';
import AddToPlaylistModal from '../../Collections/AddToPlaylistModal';
import { menuAction, menuSeparator, menuSubmenu, tidyMenu, type MenuEntry } from '../../Client/Primitives/menuEntries';
import { toggleWatched, toggleWatchlist } from './watchActions';

type OpenDialog = 'info' | 'edit' | 'playlist' | 'collection' | 'match' | 'markers';

// Fix match leaves this in a title's locked fields; while it is there, nothing
// automatic (a scan, a refresh, an NFO file) changes which title this is.
const MATCH_LOCK = 'match';
// Remove metadata leaves this one: the title stays as its files name it and is
// never looked up until it is matched again.
const UNMATCHED_LOCK = 'unmatched';

export interface MediaItemActionsOptions {
    serverId?: string;
    // Leads the menu with Mark as watched and Add to watchlist. The title page
    // has those as buttons beside the menu, so it leaves them out; a poster has
    // nowhere else to put them.
    withWatchActions?: boolean;
    // The item was edited, matched, or had something about it queued.
    onChanged: () => void;
    onDeleted: (media: MediaItem) => void;
    // Replaces the message shown after a match, for a page that has to move to
    // the item the match merged this one into.
    onMatched?: (result: MediaMatchResult) => void | Promise<void>;
}

export interface MediaItemActions {
    entries: MenuEntry[];
    dialogs: ReactNode;
}

// What "Manage …" calls each kind of title in its row.
const MANAGED_NOUN: Partial<Record<string, string>> = { Movie: 'movie', TvShow: 'show', Season: 'season', Episode: 'episode' };

// The actions a title offers, and the dialogs they open. The title page's "more"
// menu and the menu on a poster both come from here, so the two cannot drift.
// Whoever shows the menu renders `dialogs` somewhere that outlives the menu.
export function useMediaItemActions(media: MediaItem | null, options: MediaItemActionsOptions): MediaItemActions {
    const { serverId, withWatchActions = false, onChanged, onDeleted, onMatched } = options;
    const dialog = useDialog();
    const [openDialog, setOpenDialog] = useState<OpenDialog | null>(null);
    const [thumbnailsLock, setThumbnailsLock] = useState<{ id: string; locked: boolean } | null>(null);
    const [watchlist, setWatchlist] = useState<{ id: string; saved: boolean } | null>(null);

    const isAdmin = localStorage.getItem(StorageKeys.isServerAdmin) === 'true';
    const mediaId = media?.id;
    const hasThumbnails = media?.type === 'Movie' || media?.type === 'Episode';

    useEffect(() => {
        if (!mediaId || !isAdmin || !hasThumbnails) return;
        let current = true;
        libraryAdminService.getThumbnailsLock(mediaId, serverId)
            .then(result => { if (current) setThumbnailsLock({ id: mediaId, locked: result.locked }); })
            .catch(() => { if (current) setThumbnailsLock(null); });
        return () => { current = false; };
    }, [mediaId, isAdmin, hasThumbnails, serverId]);

    // Asked again every time the title is loaded, not once per title: the
    // watchlist can be changed from the title page between two openings of the
    // same poster's menu.
    useEffect(() => {
        if (!media || !withWatchActions) return;
        const id = media.id;
        let current = true;
        watchlistService.check({ mediaItemId: id }, serverId)
            .then(saved => { if (current) setWatchlist({ id, saved }); })
            .catch(() => { if (current) setWatchlist(null); });
        return () => { current = false; };
    }, [media, withWatchActions, serverId]);

    if (!media) return { entries: [], dialogs: null };

    const thumbnailsLocked = thumbnailsLock?.id === media.id ? thumbnailsLock.locked : false;
    const inWatchlist = watchlist?.id === media.id ? watchlist.saved : false;
    const hasFileInfo = hasThumbnails && (media.mediaParts?.length ?? 0) > 0;
    const canFixMatch = media.type === 'Movie' || media.type === 'TvShow';
    const matchedByHand = canFixMatch && (media.lockedFields ?? []).some(field => field.toLowerCase() === MATCH_LOCK);
    const unmatched = canFixMatch && (media.lockedFields ?? []).some(field => field.toLowerCase() === UNMATCHED_LOCK);
    const close = () => setOpenDialog(null);

    const scan = async () => {
        try {
            await libraryAdminService.triggerMediaScan(media.id, serverId);
            await dialog.alert('Targeted folder scan started in the background!');
        } catch {
            await dialog.alert('Failed to start scan.');
        }
    };

    const refresh = async () => {
        libraryAdminService.refreshItemMetadata(media.id, true, serverId);
        await dialog.alert('Metadata refresh started in the background!');
    };

    const analyze = async () => {
        libraryAdminService.analyzeMedia(media.id, serverId);
        await dialog.alert('Media analysis started in the background!');
    };

    const regenerateThumbnails = async () => {
        try {
            await libraryAdminService.regenerateMediaItemThumbnails(media.id, serverId);
            await dialog.alert('Thumbnail regeneration started in the background!');
        } catch {
            await dialog.alert('Failed to queue thumbnail regeneration.');
        }
    };

    const toggleThumbnailsLock = async () => {
        try {
            const result = await libraryAdminService.setThumbnailsLock(media.id, !thumbnailsLocked, serverId);
            setThumbnailsLock({ id: media.id, locked: result.locked });
        } catch {
            await dialog.alert('Failed to update thumbnails lock.');
        }
    };

    const remove = async () => {
        const confirmed = await dialog.confirm({
            title: 'Delete media item?',
            message: 'This permanently removes the item from Vora. Your physical files on disk will NOT be touched.',
            confirmText: 'Delete',
            tone: 'danger',
        });
        if (!confirmed) return;

        try {
            await libraryAdminService.deleteMediaItem(media.id, serverId);
            onDeleted(media);
        } catch {
            await dialog.alert('Failed to delete media item.');
        }
    };

    const matched = async (result: MediaMatchResult) => {
        close();
        if (onMatched) {
            await onMatched(result);
            return;
        }
        await dialog.alert({
            title: 'Match applied',
            message: result.mergedDuplicate || result.mediaItemId !== media.id
                ? 'This was a second copy of a show already in your library, so the two have been merged. Its metadata is refreshing now.'
                : 'Metadata is refreshing now.',
        });
        onChanged();
    };

    const clearMatch = async () => {
        const confirmed = await dialog.confirm({
            title: 'Clear manual match?',
            message: 'Vora will match this title again from its file name, and from its NFO file if this library reads them. Details you have locked stay as they are.',
            confirmText: 'Clear match',
        });
        if (!confirmed) return;

        try {
            await libraryAdminService.clearMatch(media.id, serverId);
            await dialog.alert('Manual match cleared. The title is being matched again in the background.');
            onChanged();
        } catch {
            await dialog.alert('Failed to clear the manual match.');
        }
    };

    const removeMetadata = async () => {
        const isShow = media.type === 'TvShow';
        const confirmed = await dialog.confirm({
            title: 'Remove metadata?',
            message: `This clears everything Vora looked up for this ${isShow ? 'show and its seasons and episodes' : 'movie'}: the match, description, artwork, cast, genres, studios, ratings and trailers. ${isShow ? 'Names' : 'The name'} come back from the files. Details you locked and artwork you uploaded stay. Vora won't look it up again until you use Fix match or Match automatically.`,
            confirmText: 'Remove metadata',
            tone: 'danger',
        });
        if (!confirmed) return;

        try {
            await libraryAdminService.removeMetadata(media.id, serverId);
            await dialog.alert('Metadata removed. The title now shows what its files say, with artwork from its folder or a frame of the video.');
            onChanged();
        } catch {
            await dialog.alert('Failed to remove the metadata.');
        }
    };

    const matchAutomatically = async () => {
        const confirmed = await dialog.confirm({
            title: 'Match automatically?',
            message: 'Vora will look this title up again from its file name, and from its NFO file if this library reads them. Details you have locked stay as they are.',
            confirmText: 'Match',
        });
        if (!confirmed) return;

        try {
            await libraryAdminService.clearMatch(media.id, serverId);
            await dialog.alert('The title is being matched in the background.');
            onChanged();
        } catch {
            await dialog.alert('Failed to start matching the title.');
        }
    };

    const markWatched = async () => {
        if (await toggleWatched(media, dialog, serverId)) onChanged();
    };

    const saveToWatchlist = async () => {
        const saved = await toggleWatchlist(media, dialog, serverId);
        if (saved === null) return;
        setWatchlist({ id: media.id, saved });
        onChanged();
    };

    const entries: MenuEntry[] = [];
    if (withWatchActions) {
        entries.push(menuAction('watched', isFullyWatched(media) ? 'Mark as unwatched' : 'Mark as watched', markWatched));
        entries.push(menuAction('watchlist', inWatchlist ? 'Remove from watchlist' : 'Add to watchlist', saveToWatchlist));
        entries.push(menuSeparator('before-info'));
    }
    if (hasFileInfo) entries.push(menuAction('info', 'Media info', () => setOpenDialog('info')));

    entries.push(menuSeparator('before-organise'));
    if (isAdmin) entries.push(menuAction('edit', 'Edit metadata', () => setOpenDialog('edit')));
    entries.push(menuAction('playlist', 'Add to playlist', () => setOpenDialog('playlist')));
    entries.push(menuAction('collection', 'Add to collection', () => setOpenDialog('collection')));

    if (isAdmin) {
        const manage: MenuEntry[] = [];
        if (canFixMatch) manage.push(menuAction('match', 'Fix match…', () => setOpenDialog('match')));
        if (matchedByHand) manage.push(menuAction('clear-match', 'Clear manual match', clearMatch));
        if (unmatched) manage.push(menuAction('match-automatically', 'Match automatically', matchAutomatically));
        if (canFixMatch && !unmatched) manage.push(menuAction('remove-metadata', 'Remove metadata…', removeMetadata));
        manage.push(menuSeparator('before-maintenance'));
        manage.push(menuAction('refresh', 'Refresh metadata', refresh));
        manage.push(menuAction('scan', 'Scan files', scan));
        manage.push(menuAction('analyze', 'Analyze media', analyze));
        if (hasThumbnails) {
            manage.push(menuAction('thumbnails', 'Regenerate thumbnails', regenerateThumbnails));
            manage.push(menuAction('thumbnails-lock', thumbnailsLocked ? 'Unlock thumbnails' : 'Lock thumbnails', toggleThumbnailsLock));
            manage.push(menuAction('markers', 'Edit markers', () => setOpenDialog('markers')));
        }
        manage.push(menuSeparator('before-delete'));
        manage.push(menuAction('delete', 'Delete item', remove, 'danger'));
        entries.push(menuSubmenu('manage', `Manage ${MANAGED_NOUN[media.type] ?? 'title'}…`, manage));
    }

    const dialogs = (
        <>
            {openDialog === 'info' && (
                <MediaInfoDialog parts={media.mediaParts ?? []} placement="fixed" onClose={close} />
            )}
            {openDialog === 'match' && (media.type === 'Movie' || media.type === 'TvShow') && (
                <FixMatchModal
                    mediaItemId={media.id}
                    mediaType={media.type}
                    currentYear={yearOf(media.releaseDate) ?? undefined}
                    serverId={serverId}
                    onClose={close}
                    onMatched={matched}
                />
            )}
            <EditMetadataModal
                isOpen={openDialog === 'edit'}
                onClose={close}
                onSaved={onChanged}
                itemId={media.id}
                type="media"
                libraryType={media.type === 'Movie' ? 'Movie' : 'TvShow'}
                serverId={serverId}
                initialData={{ ...media, lockedFields: media.lockedFields ?? [] }}
            />
            <AddToCollectionModal
                isOpen={openDialog === 'collection'}
                onClose={close}
                mediaId={media.id}
                libraryId={media.libraryId}
                mediaType={media.type}
                initialCollectionIds={media.collectionIds || []}
                onSaved={onChanged}
                serverId={serverId}
            />
            <AddToPlaylistModal isOpen={openDialog === 'playlist'} onClose={close} mediaId={media.id} kind="video" serverId={serverId} />
            <MarkerEditorModal
                isOpen={openDialog === 'markers'}
                onClose={close}
                mediaItemId={media.id}
                mediaItemTitle={media.title}
                durationSeconds={media.durationMinutes ? media.durationMinutes * 60 : undefined}
                serverId={serverId}
                onSaved={onChanged}
            />
        </>
    );

    return { entries: tidyMenu(entries), dialogs };
}
