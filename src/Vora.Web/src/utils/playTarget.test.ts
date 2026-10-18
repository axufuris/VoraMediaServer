import { describe, expect, it } from 'vitest';
import type { MediaItem } from '../api/Media/mediaService';
import { playButtonFor, playTargetFor } from './playTarget';

const item = (fields: Partial<MediaItem>): MediaItem => ({ id: 'id', title: 'Title', type: 'Movie', ...fields } as MediaItem);

const show = item({ id: 'show', title: 'Haunted Hotel', type: 'TvShow' });
const season = item({ id: 'season', title: 'Season 1', type: 'Season', seasonNumber: 1 });
const episode = item({ id: 'episode', title: 'The Concierge', type: 'Episode', seasonNumber: 1, episodeNumber: 3, resumePositionSeconds: 612 });

describe('what Play starts', () => {
    it('plays the up-next episode from where it was left on a show page', () => {
        const target = playTargetFor(show, episode, true);
        expect(target.item.id).toBe('episode');
        expect(target.startSeconds).toBe(612);
        expect(target.contextType).toBe('tvshow');
    });

    it('starts the up-next episode from the beginning when asked to', () => {
        expect(playTargetFor(show, episode, false).startSeconds).toBe(0);
    });

    it('sends the show while its up-next episode is unknown, still as a show', () => {
        const target = playTargetFor(show, null, true);
        expect(target.item.id).toBe('show');
        expect(target.startSeconds).toBe(0);
        expect(target.contextType).toBe('tvshow');
    });

    it('plays an episode or a movie page as itself', () => {
        expect(playTargetFor(episode, null, true)).toEqual({ item: episode, startSeconds: 612, contextType: 'tvshow' });
        const film = item({ id: 'film', resumePositionSeconds: 30 });
        expect(playTargetFor(film, null, true)).toEqual({ item: film, startSeconds: 30, contextType: 'movie' });
    });

    it("plays a season page's up-next episode, the way a show page does", () => {
        expect(playTargetFor(season, episode, true)).toEqual({ item: episode, startSeconds: 612, contextType: 'tvshow' });
    });

    it('sends the season while its up-next episode is unknown, still as television', () => {
        expect(playTargetFor(season, null, true)).toEqual({ item: season, startSeconds: 0, contextType: 'tvshow' });
    });
});

describe('what the Play button says', () => {
    it('says Resume with the episode on a season or show whose next episode was left part-way', () => {
        expect(playButtonFor(season, episode)).toEqual({ label: 'Resume', episode: 'S1 E3', resumeSeconds: 612 });
        expect(playButtonFor(show, episode)).toEqual({ label: 'Resume', episode: 'S1 E3', resumeSeconds: 612 });
    });

    it('names the episode it will start when it is not part-way', () => {
        const fresh = item({ ...episode, resumePositionSeconds: 0 });
        expect(playButtonFor(season, fresh)).toEqual({ label: 'Play', episode: 'S1 E3', resumeSeconds: 0 });
        expect(playButtonFor(show, fresh)).toEqual({ label: 'Play next', episode: 'S1 E3', resumeSeconds: 0 });
    });

    it('does not offer to resume an episode watched to the end', () => {
        expect(playButtonFor(episode, null)).toEqual({ label: 'Resume', episode: undefined, resumeSeconds: 612 });
        expect(playButtonFor(item({ ...episode, isPlayed: true }), null)).toEqual({ label: 'Play', episode: undefined, resumeSeconds: 0 });
    });

    it('keeps a film plain', () => {
        expect(playButtonFor(item({ id: 'film' }), null)).toEqual({ label: 'Play', episode: undefined, resumeSeconds: 0 });
    });
});
