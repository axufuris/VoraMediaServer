import { describe, it, expect } from 'vitest';
import { findActiveLineIndex, parseLrc } from './lrcParser';

// Times are kept in seconds, matching the player's currentTime, so a fraction
// has to survive the conversion at millisecond precision.
const at = (lrc: string) => parseLrc(lrc).map(l => ({ time: l.time, text: l.text }));

describe('parsing timestamps', () => {
    it('reads minutes and seconds', () => {
        expect(at('[01:23]hello')).toEqual([{ time: 83, text: 'hello' }]);
    });

    // The fraction is hundredths in most LRC files but thousandths in some, and
    // a single digit means tenths. Reading "5" as 5ms would put the line a half
    // second early.
    it.each([
        ['[00:01.5]a', 1.5],
        ['[00:01.50]a', 1.5],
        ['[00:01.500]a', 1.5],
        ['[00:01.05]a', 1.05],
        ['[00:01.005]a', 1.005],
    ])('reads the fraction in %s as %s seconds', (lrc, expected) => {
        expect(parseLrc(lrc)[0].time).toBeCloseTo(expected, 4);
    });

    it('treats a missing fraction as zero', () => {
        expect(parseLrc('[00:07]a')[0].time).toBe(7);
    });

    it('handles minutes past an hour', () => {
        expect(parseLrc('[100:00]a')[0].time).toBe(6000);
    });
});

describe('repeated tags on one line', () => {
    // A chorus line is written once with every timestamp it recurs at.
    it('emits the line once per timestamp', () => {
        expect(at('[00:10.00][01:20.00][02:30.00]chorus')).toEqual([
            { time: 10, text: 'chorus' },
            { time: 80, text: 'chorus' },
            { time: 150, text: 'chorus' },
        ]);
    });

    it('strips every tag from the text', () => {
        expect(parseLrc('[00:10.00][01:20.00]chorus')[0].text).toBe('chorus');
    });
});

describe('lines without timing', () => {
    // Metadata headers carry no timestamp and must not become lyric lines.
    it.each(['[ar:Artist]', '[ti:Title]', '[by:Someone]', 'bare text', ''])('drops %s', line => {
        expect(parseLrc(`${line}\n[00:05.00]real`)).toHaveLength(1);
    });

    it('keeps a timed line with no words as an empty beat', () => {
        expect(at('[00:05.00]')).toEqual([{ time: 5, text: '' }]);
    });
});

describe('ordering and whitespace', () => {
    it('returns lines in time order regardless of file order', () => {
        expect(at('[00:30.00]third\n[00:10.00]first\n[00:20.00]second').map(l => l.text))
            .toEqual(['first', 'second', 'third']);
    });

    it('orders the repeats of one line correctly among others', () => {
        expect(at('[00:10.00][00:30.00]chorus\n[00:20.00]verse').map(l => l.text))
            .toEqual(['chorus', 'verse', 'chorus']);
    });

    it('trims surrounding whitespace from the text', () => {
        expect(parseLrc('[00:05.00]   padded   ')[0].text).toBe('padded');
    });

    it('handles CRLF line endings', () => {
        expect(parseLrc('[00:05.00]a\r\n[00:06.00]b')).toHaveLength(2);
    });
});

describe('nothing to parse', () => {
    it.each([undefined, null, ''])('returns no lines for %s', value => {
        expect(parseLrc(value)).toEqual([]);
    });

    // Plain lyrics pasted into the synced field must not read as synced.
    it('returns no lines for untimed text', () => {
        expect(parseLrc('just some words\nand more words')).toEqual([]);
    });
});

describe('finding the active line', () => {
    const lines = parseLrc('[00:00.00]zero\n[00:10.00]ten\n[00:20.00]twenty');

    it('has no active line before the first timestamp', () => {
        expect(findActiveLineIndex(parseLrc('[00:05.00]a'), 1)).toBe(-1);
    });

    it('activates a line exactly on its timestamp', () => {
        expect(findActiveLineIndex(lines, 10)).toBe(1);
    });

    it('keeps the line active until the next one', () => {
        expect(findActiveLineIndex(lines, 19.999)).toBe(1);
    });

    it('holds the last line to the end of the song', () => {
        expect(findActiveLineIndex(lines, 500)).toBe(2);
    });

    // Seeking backwards has to move the highlight back, not just forwards.
    it('follows a backward seek', () => {
        expect(findActiveLineIndex(lines, 0)).toBe(0);
    });

    it('has no active line among none', () => {
        expect(findActiveLineIndex([], 10)).toBe(-1);
    });
});
