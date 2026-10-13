import { mediaService, type MediaItem } from '../../../api/Media/mediaService';
import { watchlistService } from '../../../api/Watchlist/watchlistService';
import type { useDialog } from '../../../dialogs';
import { affectedEpisodeCount, isFullyWatched } from '../../../utils/watchState';

type Dialog = ReturnType<typeof useDialog>;

// Flips a title between watched and unwatched. Marking a show or a season fans
// out to every episode under it, so this says how many before doing it. Returns
// whether anything changed.
export async function toggleWatched(media: MediaItem, dialog: Dialog, serverId?: string): Promise<boolean> {
    const next = !isFullyWatched(media);

    const episodeCount = affectedEpisodeCount(media);
    if (episodeCount > 1) {
        const scope = media.type === 'TvShow' ? 'this show' : 'this season';
        const confirmed = await dialog.confirm({
            title: next ? `Mark ${episodeCount} episodes as watched?` : `Mark ${episodeCount} episodes as unwatched?`,
            message: next
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
