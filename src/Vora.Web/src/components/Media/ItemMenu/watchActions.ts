import { mediaService, type MediaItem } from '../../../api/Media/mediaService';
import { watchlistService } from '../../../api/Watchlist/watchlistService';
import type { useDialog } from '../../../dialogs';
import { affectedEpisodeCount, changedEpisodeCount, isFullyWatched } from '../../../utils/watchState';

type Dialog = ReturnType<typeof useDialog>;

// Flips a title between watched and unwatched. Marking a show or a season fans
// out to the episodes under it, so this says how many it changes before doing
// it. Returns whether anything changed.
export async function toggleWatched(media: MediaItem, dialog: Dialog, serverId?: string): Promise<boolean> {
    const next = !isFullyWatched(media);

    const total = affectedEpisodeCount(media);
    if (total > 1) {
        const changed = changedEpisodeCount(media, next);
        const episodes = changed === 1 ? '1 episode' : `${changed} episodes`;
        const scope = media.type === 'TvShow' ? 'this show' : 'this season';
        const confirmed = await dialog.confirm({
            title: next ? `Mark ${episodes} as watched?` : `Mark ${episodes} as unwatched?`,
            message: changed < total
                ? next
                    ? `The ${episodes} in ${scope} you haven't watched will be marked watched, and any playback progress on ${changed === 1 ? 'it' : 'them'} will be cleared.`
                    : `The ${episodes} in ${scope} you've watched will be marked unwatched.`
                : next
                    ? `Every episode in ${scope} will be marked watched, and any playback progress on them will be cleared.`
                    : `Every episode in ${scope} will be marked unwatched.`,
            confirmText: next ? 'Mark watched' : 'Mark unwatched',
            cancelText: 'Cancel',
        });
        if (!confirmed) return false;
    }

    try {
        await mediaService.markAsPlayed(media.id, next, serverId);
        return true;
    } catch {
        await dialog.alert('Failed to update play state.');
        return false;
    }
}

// Adds a title to the watchlist or takes it off. Returns where it ended up, or
// null when it could not be changed.
export async function toggleWatchlist(media: MediaItem, dialog: Dialog, serverId?: string): Promise<boolean | null> {
    try {
        return await watchlistService.toggle(
            { mediaItemId: media.id },
            { type: media.type, title: media.title, posterUrl: media.posterUrl },
            serverId,
        );
    } catch {
        await dialog.alert('Failed to update watchlist.');
        return null;
    }
}
