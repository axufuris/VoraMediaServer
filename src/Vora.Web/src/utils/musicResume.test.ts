import { describe, expect, it } from 'vitest';
import type { PlayableMedia } from '../contexts/usePlayer';
import { musicResumeKey, musicResumeSnapshot, resumeWindow, RESUME_MAX_QUEUE } from './musicResume';

const song = (id: string, musicSource?: PlayableMedia['musicSource']): PlayableMedia => ({
    id,
    title: id,
    streamUrl: '',
    playbackContextType: 'Music',
    musicSource,
});

const ids = (count: number) => Array.from({ length: count }, (_, i) => `t${i}`);

describe('the window of a long queue', () => {
    it('is the whole queue when it fits', () => {
        expect(resumeWindow(ids(40), 7)).toEqual({ ids: ids(40), index: 7 });
        expect(resumeWindow(ids(RESUME_MAX_QUEUE), 299).ids).toHaveLength(RESUME_MAX_QUEUE);
    });

    it.each([
        [500, 10, 0, 10],
        [500, 200, 150, 50],
        [500, 499, 200, 299],
        [1000, 0, 0, 0],
    ])('keeps 300 of %i songs around index %i, starting at %i with the current one at %i', (count, index, start, windowIndex) => {
        const all = ids(count);

        const window = resumeWindow(all, index);

        expect(window.ids).toHaveLength(RESUME_MAX_QUEUE);
        expect(window.ids[0]).toBe(all[start]);
        expect(window.index).toBe(windowIndex);
        expect(window.ids[window.index]).toBe(all[index]);
    });
});

describe('the snapshot of a music queue', () => {
    it('is the queue in play order, the current song and the source the current song came from', () => {
        const queue = [song('a', { type: 'Playlist', id: 'p1' }), song('b', { type: 'Playlist', id: 'p1' }), song('c', { type: 'Playlist', id: 'p1' })];

        expect(musicResumeSnapshot(queue, 1)).toEqual({ source: 'Playlist', sourceId: 'p1', trackIds: ['a', 'b', 'c'], index: 1 });
    });

    it('has no source for a queue that came from nowhere in particular', () => {
        expect(musicResumeSnapshot([song('a'), song('b')], 0)).toEqual({ source: 'None', sourceId: undefined, trackIds: ['a', 'b'], index: 0 });
    });

    it('follows the current song when songs from different places were queued', () => {
        const queue = [song('a', { type: 'Album', id: 'al1' }), song('b', { type: 'Playlist', id: 'p1' })];

        expect(musicResumeSnapshot(queue, 1)?.source).toBe('Playlist');
    });

    it('is nothing for an empty queue, an index off the end or a current item that is not a song', () => {
        const episode: PlayableMedia = { id: 'e1', title: 'Pilot', streamUrl: '', playbackContextType: 'Video' };

        expect(musicResumeSnapshot([], 0)).toBeNull();
        expect(musicResumeSnapshot([song('a')], 3)).toBeNull();
        expect(musicResumeSnapshot([episode], 0)).toBeNull();
    });

    it('leaves out anything in the queue that is not a song and counts the index among the songs', () => {
        const episode: PlayableMedia = { id: 'e1', title: 'Pilot', streamUrl: '', playbackContextType: 'Video' };

        expect(musicResumeSnapshot([episode, song('a'), song('b')], 2)).toMatchObject({ trackIds: ['a', 'b'], index: 1 });
    });

    it('is windowed when the queue is long', () => {
        const queue = ids(500).map(id => song(id));

        const snapshot = musicResumeSnapshot(queue, 400);

        expect(snapshot?.trackIds).toHaveLength(RESUME_MAX_QUEUE);
        expect(snapshot?.trackIds[snapshot.index]).toBe('t400');
    });
});

describe('the key of a snapshot', () => {
    const base = { source: 'Mix' as const, sourceId: 'm1', trackIds: ['a', 'b', 'c'], index: 1 };

    it('is the same for the same state and changes with the source, the song or the queue', () => {
        expect(musicResumeKey({ ...base })).toBe(musicResumeKey({ ...base, trackIds: [...base.trackIds] }));
        expect(musicResumeKey({ ...base, index: 2 })).not.toBe(musicResumeKey(base));
        expect(musicResumeKey({ ...base, sourceId: 'm2' })).not.toBe(musicResumeKey(base));
        expect(musicResumeKey({ ...base, source: 'Playlist' })).not.toBe(musicResumeKey(base));
        expect(musicResumeKey({ ...base, trackIds: ['a', 'b', 'c', 'd'] })).not.toBe(musicResumeKey(base));
    });
});
