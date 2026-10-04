import { describe, expect, it } from 'vitest';
import { buildSetupSteps, isSetupStepId, nearestStep, type SetupContent } from './setupSteps';

const content = (overrides: Partial<SetupContent> = {}): SetupContent => ({ moviesAndShows: false, music: false, liveTv: false, internetRadio: false, podcasts: false, ...overrides });
const ids = (c: SetupContent, discover = false) => buildSetupSteps(c, discover).map(s => s.id);

describe('setup guide steps', () => {
    it('always starts with the basics and ends with AI and done', () => {
        expect(ids(content())).toEqual(['welcome', 'server', 'playback', 'content', 'ai', 'done']);
    });

    it('asks about intro detection and thumbnails inside Movies & TV, after the plugins', () => {
        expect(ids(content({ moviesAndShows: true }))).toEqual([
            'welcome', 'server', 'playback', 'content', 'metadata', 'artwork', 'ratings', 'detection', 'thumbnails', 'requests', 'subtitles', 'ai', 'done',
        ]);
    });

    it('adds Discover only when a Discover plugin has rows to offer', () => {
        expect(ids(content({ moviesAndShows: true }), true)).toContain('discover');
        expect(ids(content({ moviesAndShows: true }), false)).not.toContain('discover');
        expect(ids(content(), true)).not.toContain('discover');
    });

    it('adds a section for each other kind of content picked', () => {
        expect(ids(content({ liveTv: true, internetRadio: true, podcasts: true, music: true }))).toEqual([
            'welcome', 'server', 'playback', 'content', 'livetv', 'radio', 'podcasts', 'lastfm', 'lyrics', 'ai', 'done',
        ]);
    });

    it('resumes at the next step that still exists when the saved one was dropped', () => {
        const steps = buildSetupSteps(content({ music: true }), false);

        expect(nearestStep(steps, 'lastfm')).toBe('lastfm');
        expect(nearestStep(steps, 'ratings')).toBe('lastfm');
        expect(nearestStep(steps, 'podcasts')).toBe('lastfm');
    });

    it('only accepts known step names from the server', () => {
        expect(isSetupStepId('artwork')).toBe(true);
        expect(isSetupStepId('nope')).toBe(false);
        expect(isSetupStepId(null)).toBe(false);
    });
});
