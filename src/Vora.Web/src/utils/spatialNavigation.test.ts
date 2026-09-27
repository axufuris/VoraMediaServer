import { describe, it, expect } from 'vitest';
import {
    directionFromKey,
    findNeighbour,
    ownsDirection,
    scoreCandidate,
    type NavRect,
} from './spatialNavigation';

const at = (left: number, top: number, width = 40, height = 40): NavRect =>
    ({ left, top, right: left + width, bottom: top + height });

// The transport row on the now-playing screen, with the full-width seek bar
// above it and the header controls above that.
const seekBar = { id: 'seek', rect: at(100, 200, 600, 20) };
const shuffle = { id: 'shuffle', rect: at(200, 300) };
const previous = { id: 'previous', rect: at(280, 300) };
const playPause = { id: 'play', rect: at(360, 295, 50, 50) };
const next = { id: 'next', rect: at(440, 300) };
const repeat = { id: 'repeat', rect: at(520, 300) };
const closeButton = { id: 'close', rect: at(100, 40) };
const queueButton = { id: 'queue', rect: at(660, 40) };

const controls = [seekBar, shuffle, previous, playPause, next, repeat, closeButton, queueButton];

describe('mapping a D-pad press', () => {
    it.each([
        ['ArrowUp', 'up'],
        ['ArrowDown', 'down'],
        ['ArrowLeft', 'left'],
        ['ArrowRight', 'right'],
    ])('reads %s as %s', (key, expected) => {
        expect(directionFromKey(key)).toBe(expected);
    });

    it.each(['Enter', ' ', 'Escape', 'Tab', 'a'])('leaves %s alone', key => {
        expect(directionFromKey(key)).toBeUndefined();
    });
});

describe('moving along the transport row', () => {
    it('steps right one control at a time', () => {
        expect(findNeighbour(shuffle.rect, controls, 'right')?.id).toBe('previous');
        expect(findNeighbour(previous.rect, controls, 'right')?.id).toBe('play');
        expect(findNeighbour(playPause.rect, controls, 'right')?.id).toBe('next');
    });

    it('steps left one control at a time', () => {
        expect(findNeighbour(next.rect, controls, 'left')?.id).toBe('play');
        expect(findNeighbour(playPause.rect, controls, 'left')?.id).toBe('previous');
    });

    // Nothing past the end of the row: focus should stay put rather than
    // wrapping round to the far side, which loses the viewer's place.
    it('stops at the end of the row', () => {
        expect(findNeighbour(repeat.rect, controls, 'right')).toBeUndefined();
        expect(findNeighbour(shuffle.rect, controls, 'left')).toBeUndefined();
    });
});

describe('moving between rows', () => {
    // The seek bar spans the whole width, so it overlaps every transport
    // button. Scoring cross-axis distance as the gap between rects rather than
    // between centres is what makes it read as directly above all of them.
    it.each([shuffle, previous, playPause, next, repeat])('reaches the seek bar going up from $id', control => {
        expect(findNeighbour(control.rect, controls, 'up')?.id).toBe('seek');
    });

    it('reaches the header from the seek bar', () => {
        expect(findNeighbour(seekBar.rect, controls, 'up')?.id).toBe('close');
    });

    // The whole second half of the complaint: the top menu button has to be
    // reachable at all.
    it('reaches a header button by going up twice', () => {
        const first = findNeighbour(playPause.rect, controls, 'up');
        expect(first?.id).toBe('seek');
        expect(findNeighbour(first!.rect, controls, 'up')).toBeDefined();
    });

    it('comes back down to the transport row', () => {
        expect(findNeighbour(seekBar.rect, controls, 'down')).toBeDefined();
    });

    // Going down from the header must not skip the seek bar entirely.
    it('lands on the seek bar coming down from the header', () => {
        expect(findNeighbour(closeButton.rect, controls, 'down')?.id).toBe('seek');
    });
});

describe('preferring the travelled axis', () => {
    const inRow = { id: 'inRow', rect: at(300, 100) };
    // Clear of the origin's row entirely — one that merely overlapped it would
    // be in the same row by any honest reading, and should win on distance.
    const diagonal = { id: 'diagonal', rect: at(250, 220) };
    const origin = at(200, 100);

    // The diagonal candidate is closer by raw centre distance; staying in the
    // row is what makes a D-pad feel predictable.
    it('picks the control in the same row over a nearer diagonal one', () => {
        expect(findNeighbour(origin, [inRow, diagonal], 'right')?.id).toBe('inRow');
    });

    it('gives no score to a candidate behind the direction travelled', () => {
        expect(scoreCandidate(origin, at(100, 100), 'right')).toBeUndefined();
    });

    it('gives no score to a candidate on top of the current one', () => {
        expect(scoreCandidate(origin, origin, 'right')).toBeUndefined();
    });

    it('scores a nearer candidate lower than a further one', () => {
        const near = scoreCandidate(origin, at(260, 100), 'right')!;
        const far = scoreCandidate(origin, at(400, 100), 'right')!;
        expect(near).toBeLessThan(far);
    });

    it('has nothing to move to among no candidates', () => {
        expect(findNeighbour(origin, [], 'right')).toBeUndefined();
    });

    // Deliberate: a candidate further off the axis than along it is not in the
    // direction pressed. Allowing it is what made Right at the end of the
    // transport row leap up to a header button.
    it('refuses a candidate further off the axis than along it', () => {
        expect(scoreCandidate(origin, at(260, 400), 'right')).toBeUndefined();
    });

    it('allows one within the cone', () => {
        expect(scoreCandidate(origin, at(400, 150), 'right')).toBeDefined();
    });

    it('does not leap from the end of a row to a header button', () => {
        expect(findNeighbour(repeat.rect, controls, 'right')).toBeUndefined();
    });
});

describe('controls that handle their own arrows', () => {
    // Left/Right on the seek bar scrubs, which is what a remote should do;
    // Up/Down still has to move focus off it or the viewer is trapped.
    it('lets a slider keep the axis it scrubs on', () => {
        expect(ownsDirection({ tagName: 'INPUT', type: 'range' }, 'left')).toBe(true);
        expect(ownsDirection({ tagName: 'INPUT', type: 'range' }, 'right')).toBe(true);
    });

    it('does not let a slider trap focus vertically', () => {
        expect(ownsDirection({ tagName: 'INPUT', type: 'range' }, 'up')).toBe(false);
        expect(ownsDirection({ tagName: 'INPUT', type: 'range' }, 'down')).toBe(false);
    });

    it.each(['up', 'down', 'left', 'right'] as const)('leaves caret movement in a text field alone going %s', direction => {
        expect(ownsDirection({ tagName: 'INPUT', type: 'text' }, direction)).toBe(true);
        expect(ownsDirection({ tagName: 'TEXTAREA' }, direction)).toBe(true);
    });

    // An input with no type attribute is a text field.
    it('treats an untyped input as text', () => {
        expect(ownsDirection({ tagName: 'INPUT' }, 'left')).toBe(true);
    });

    it.each(['BUTTON', 'A', 'DIV'])('moves focus off a %s in every direction', tagName => {
        expect(ownsDirection({ tagName }, 'left')).toBe(false);
        expect(ownsDirection({ tagName }, 'up')).toBe(false);
    });

    // Checkboxes and buttons-as-inputs are not caret-bearing.
    it.each(['checkbox', 'radio', 'button', 'submit'])('moves focus off an input of type %s', type => {
        expect(ownsDirection({ tagName: 'INPUT', type }, 'left')).toBe(false);
    });

    it('is case-insensitive about the tag name', () => {
        expect(ownsDirection({ tagName: 'input', type: 'RANGE' }, 'left')).toBe(true);
    });
});
