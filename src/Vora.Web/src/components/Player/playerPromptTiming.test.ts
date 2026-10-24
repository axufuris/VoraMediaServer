import { describe, expect, it } from 'vitest';
import { skipPromptKey, skipPromptVisible, upNextCountdownSeconds, upNextStartsNow } from './playerPromptTiming';

describe('the Up next countdown', () => {
    it('follows the time left in the item while that is longer than the card has to wait', () => {
        expect(upNextCountdownSeconds(14.6, 0)).toBe(14.6);
        expect(upNextCountdownSeconds(3, 9_000)).toBe(3);
    });

    it('counts down from ten when Skip Credits leaves two seconds of the item', () => {
        expect(upNextCountdownSeconds(2, 0)).toBe(10);
        expect(upNextCountdownSeconds(0, 2_000)).toBe(8);
        expect(upNextCountdownSeconds(0, 9_500)).toBe(0.5);
    });

    it('never goes below zero', () => {
        expect(upNextCountdownSeconds(0, 12_000)).toBe(0);
    });
});

describe('starting the next item', () => {
    it('waits for the item to finish', () => {
        expect(upNextStartsNow(false, 15_000)).toBe(false);
    });

    it('waits for the card to have been up for ten seconds', () => {
        expect(upNextStartsNow(true, 2_000)).toBe(false);
        expect(upNextStartsNow(true, 9_999)).toBe(false);
        expect(upNextStartsNow(true, 10_000)).toBe(true);
    });
});

describe('the skip prompt', () => {
    it('shows for ten seconds after it appears', () => {
        expect(skipPromptVisible(0, false)).toBe(true);
        expect(skipPromptVisible(9_999, false)).toBe(true);
        expect(skipPromptVisible(10_000, false)).toBe(false);
    });

    it('comes back with the controls', () => {
        expect(skipPromptVisible(30_000, true)).toBe(true);
    });

    it('is a new prompt for each marker it skips', () => {
        expect(skipPromptKey({ label: 'Skip Recap', target: 95, kind: 'intro' }, null))
            .not.toBe(skipPromptKey({ label: 'Skip Intro', target: 180, kind: 'intro' }, null));
        expect(skipPromptKey({ label: 'Skip to Scene', target: 2600, kind: 'credits-to-scene' }, null)).toBe('credits-to-scene:2600.00');
    });

    it('stays the same Skip Credits prompt while its target follows the end of the item', () => {
        expect(skipPromptKey({ label: 'Skip Credits', target: 2698, kind: 'credits-to-end' }, null))
            .toBe(skipPromptKey({ label: 'Skip Credits', target: 2698.6, kind: 'credits-to-end' }, null));
    });

    it('is the commercial while one is playing', () => {
        expect(skipPromptKey({ label: 'Skip Intro', target: 180, kind: 'intro' }, '600-720')).toBe('commercial:600-720');
        expect(skipPromptKey(null, null)).toBeNull();
    });
});
