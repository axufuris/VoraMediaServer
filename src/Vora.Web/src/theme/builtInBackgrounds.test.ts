import { describe, expect, it } from 'vitest';
import { sanitizeImageUrl } from './applyTheme';
import type { BackgroundSlot, ThemeManifest } from './types';

const modules = import.meta.glob<Record<string, ThemeManifest>>(['./themes/*.ts', './clientTemplates/*.ts'], { eager: true });
const manifests = Object.values(modules).flatMap(module => Object.values(module));

const backgroundImages = manifests.flatMap(manifest =>
    Object.entries(manifest.backgrounds ?? {})
        .filter((entry): entry is [string, BackgroundSlot] => !!entry[1])
        .map(([slot, background]) => ({ theme: manifest.id, slot, image: background.image })));

describe('built-in theme backgrounds', () => {
    it('finds every built-in theme and template', () => {
        expect(manifests.map(m => m.id).sort()).toEqual(
            ['vora-aurora', 'vora-cinema', 'vora-dark', 'vora-light', 'vora-noir', 'vora-ocean', 'vora-velvet']);
        expect(backgroundImages.length).toBeGreaterThan(0);
    });

    it.each(backgroundImages)('$theme $slot image is accepted by the url sanitizer', ({ image }) => {
        expect(sanitizeImageUrl(image)).toBe(image);
    });
});
