import { describe, expect, it } from 'vitest';
import type { MediaItem } from '../api/Media/mediaService';
import { playTargetFor } from './playTarget';

const item = (fields: Partial<MediaItem>): MediaItem => ({ id: 'id', title: 'Title', type: 'Movie', ...fields } as MediaItem);

const show = item({ id: 'show', title: 'Haunted Hotel', type: 'TvShow' });
const episode = item({ id: 'episode', title: 'The Concierge', type: 'Episode', resumePositionSeconds: 612 });

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
});
