import type { MediaItem } from '../api/Media/mediaService';
import { seasonEpisodeLabel } from './seasonLabel';

export interface PlayTarget {
    item: MediaItem;
    startSeconds: number;
    contextType: 'tvshow' | 'movie';
}

export interface PlayButton {
    label: 'Play' | 'Play next' | 'Resume';
    episode?: string;
    resumeSeconds: number;
}

const playsAnEpisode = (media: MediaItem) => media.type === 'TvShow' || media.type === 'Season';

// Play on a show's or a season's page starts its up-next episode. The player
// looks up skip markers, track menus, next-episode autoplay and scrub
// thumbnails by the id it is given, so it gets the episode, with that
// episode's resume position, rather than the show or the season (which have no
// parts and no resume position of their own). Without an up-next episode the
// title itself is sent and the server picks the episode.
export function playTargetFor(media: MediaItem, upNextEpisode: MediaItem | null, resume: boolean): PlayTarget {
    const item = playsAnEpisode(media) && upNextEpisode ? upNextEpisode : media;
    return {
        item,
        startSeconds: resume ? item.resumePositionSeconds || 0 : 0,
        contextType: item.type === 'Episode' || playsAnEpisode(item) ? 'tvshow' : 'movie',
    };
}

// What the Play button says. A show or a season names the episode it will
// start, and says Resume, with the time, when that episode was left part-way,
// as an episode's own page does.
export function playButtonFor(media: MediaItem, upNextEpisode: MediaItem | null): PlayButton {
    const { item } = playTargetFor(media, upNextEpisode, true);
    const resumeSeconds = !item.isPlayed && (item.resumePositionSeconds ?? 0) > 0 ? item.resumePositionSeconds ?? 0 : 0;
    const episode = item !== media && item.seasonNumber != null && item.episodeNumber != null
        ? seasonEpisodeLabel(item.seasonNumber, item.episodeNumber, { endEpisodeNumber: item.endEpisodeNumber })
        : undefined;
    const label = resumeSeconds > 0 ? 'Resume' : media.type === 'TvShow' ? 'Play next' : 'Play';
    return { label, episode, resumeSeconds };
}
