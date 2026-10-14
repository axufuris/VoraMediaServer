import type { MediaItem } from '../api/Media/mediaService';

export interface PlayTarget {
    item: MediaItem;
    startSeconds: number;
    contextType: 'tvshow' | 'movie';
}

// Play on a show's page starts its up-next episode. The player looks up skip
// markers, track menus, next-episode autoplay and scrub thumbnails by the id it
// is given, so it gets the episode, with that episode's resume position, rather
// than the show (which has no parts and no resume position of its own). Without
// an up-next episode the show itself is sent and the server picks the episode.
export function playTargetFor(media: MediaItem, upNextEpisode: MediaItem | null, resume: boolean): PlayTarget {
    const item = media.type === 'TvShow' && upNextEpisode ? upNextEpisode : media;
    return {
        item,
        startSeconds: resume ? item.resumePositionSeconds || 0 : 0,
        contextType: item.type === 'Episode' || item.type === 'TvShow' ? 'tvshow' : 'movie',
    };
}
