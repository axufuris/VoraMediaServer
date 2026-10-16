import { describe, expect, it } from 'vitest';
import { audioLayout, knownLanguageName, languageName } from './trackLabels';

describe('track labels', () => {
    it.each([['jpn', 'Japanese'], ['ja', 'Japanese'], ['eng', 'English'], ['fr', 'French']])('names %s as %s', (code, name) => {
        expect(languageName(code)).toBe(name);
    });

    // What the scanner stores: "eng" becomes the name "English", every other
    // code is only capitalised.
    it.each([['English', 'English'], ['Tel', 'Telugu'], ['Tam', 'Tamil'], ['Hin', 'Hindi'], ['Jpn', 'Japanese'], ['Fre', 'French']])('names the stored %s as %s', (stored, name) => {
        expect(languageName(stored)).toBe(name);
    });

    it('keeps a spelled-out name capitalised instead of lower-casing it', () => {
        expect(languageName('english')).toBe('English');
        expect(languageName('Telugu')).toBe('Telugu');
    });

    it('names a missing language Unknown and shows a code it cannot read as it is', () => {
        expect(languageName(undefined)).toBe('Unknown');
        expect(languageName('not a language')).toBe('not a language');
    });

    it('has no language to name when the file does not set one', () => {
        expect(knownLanguageName(undefined)).toBeNull();
        expect(knownLanguageName('  ')).toBeNull();
        expect(knownLanguageName('Und')).toBeNull();
        expect(knownLanguageName('Tel')).toBe('Telugu');
    });

    it.each([[8, '7.1'], [6, '5.1'], [2, '2.0'], [1, '1.0'], [3, '3ch'], [undefined, '']])('writes %s channels as "%s"', (channels, layout) => {
        expect(audioLayout(channels)).toBe(layout);
    });
});
