import { describe, expect, it } from 'vitest';
import { buildSetupSteps, isSetupStepId, nearestStep, type SetupContent, type SetupStepOptions } from './setupSteps';

const content = (overrides: Partial<SetupContent> = {}): SetupContent => ({ moviesAndShows: false, music: false, liveTv: false, internetRadio: false, podcasts: false, ...overrides });
const ids = (c: SetupContent, options: Partial<SetupStepOptions> = {}) => buildSetupSteps(c, { discover: false, email: false, ...options }).map(s => s.id);

describe('setup guide steps', () => {
    it('always starts with the basics and access, and ends with AI, backups and done', () => {
        expect(ids(content())).toEqual(['welcome', 'server', 'playback', 'content', 'remote', 'signup', 'ai', 'backups', 'done']);
    });

    it('asks about email right after sign-ups when it is needed', () => {
        expect(ids(content(), { email: true })).toEqual(['welcome', 'server', 'playback', 'content', 'remote', 'signup', 'email', 'ai', 'backups', 'done']);
    });

    it('asks about intro detection and thumbnails inside Movies & TV, after the plugins', () => {
        expect(ids(content({ moviesAndShows: true }))).toEqual([
            'welcome', 'server', 'playback', 'content', 'remote', 'signup',
            'metadata', 'artwork', 'ratings', 'detection', 'thumbnails', 'requests', 'subtitles', 'ai', 'backups', 'done',
        ]);
    });

    it('adds Discover only when a Discover plugin has rows to offer', () => {
        expect(ids(content({ moviesAndShows: true }), { discover: true })).toContain('discover');
        expect(ids(content({ moviesAndShows: true }))).not.toContain('discover');
        expect(ids(content(), { discover: true })).not.toContain('discover');
    });

    it('adds a section for each other kind of content picked', () => {
        expect(ids(content({ liveTv: true, internetRadio: true, podcasts: true, music: true }))).toEqual([
            'welcome', 'server', 'playback', 'content', 'remote', 'signup', 'livetv', 'radio', 'podcasts', 'lastfm', 'lyrics', 'ai', 'backups', 'done',
        ]);
    });

    it('resumes at the next step that still exists when the saved one was dropped', () => {
        const steps = buildSetupSteps(content({ music: true }), { discover: false, email: false });

        expect(nearestStep(steps, 'lastfm')).toBe('lastfm');
        expect(nearestStep(steps, 'ratings')).toBe('lastfm');
        expect(nearestStep(steps, 'podcasts')).toBe('lastfm');
        expect(nearestStep(steps, 'email')).toBe('lastfm');
    });

    it('only accepts known step names from the server', () => {
        expect(isSetupStepId('artwork')).toBe(true);
        expect(isSetupStepId('backups')).toBe(true);
        expect(isSetupStepId('nope')).toBe(false);
        expect(isSetupStepId(null)).toBe(false);
    });
});
