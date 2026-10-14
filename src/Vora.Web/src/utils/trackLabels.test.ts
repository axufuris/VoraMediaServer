import { describe, expect, it } from 'vitest';
import { audioLayout, languageName } from './trackLabels';

describe('track labels', () => {
    it.each([['jpn', 'Japanese'], ['ja', 'Japanese'], ['eng', 'English'], ['fr', 'French']])('names %s as %s', (code, name) => {
        expect(languageName(code)).toBe(name);
    });

    it('names a missing language Unknown and shows a code it cannot read as it is', () => {
        expect(languageName(undefined)).toBe('Unknown');
        expect(languageName('not a language')).toBe('not a language');
    });

    it.each([[8, '7.1'], [6, '5.1'], [2, '2.0'], [1, '1.0'], [3, '3ch'], [undefined, '']])('writes %s channels as "%s"', (channels, layout) => {
        expect(audioLayout(channels)).toBe(layout);
    });
});
