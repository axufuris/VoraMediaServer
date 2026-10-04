import { describe, it, expect } from 'vitest';
import { parseGuidePrefs } from './useGuideData';

describe('parseGuidePrefs', () => {
    it('reads saved prefs and fills in what is missing', () => {
        expect(parseGuidePrefs('{"favoriteChannels":["cnn.us"],"hideEmpty":true}')).toEqual({
            enabledProviders: [], hiddenChannels: [], favoriteChannels: ['cnn.us'], regions: [], resolutions: [], hideEmpty: true,
        });
    });

    it('reads the old provider-list save', () => {
        expect(parseGuidePrefs('["p1","p2"]')?.enabledProviders).toEqual(['p1', 'p2']);
    });

    it('treats nothing or garbage as no save', () => {
        expect(parseGuidePrefs(null)).toBeNull();
        expect(parseGuidePrefs('[]')).toBeNull();
        expect(parseGuidePrefs('{oops')).toBeNull();
    });
});
