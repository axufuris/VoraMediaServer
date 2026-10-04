import { useEffect, useState } from 'react';
import { paletteFromPixels, type SynthPalette } from '../../../utils/artPalette';
import { thumbUrl } from '../../../utils/thumbnails';

export interface CoverArt {
    image: HTMLImageElement | null;
    palette: SynthPalette | null;
}

const NO_ART: CoverArt = { image: null, palette: null };
const CACHE_LIMIT = 24;
const cache = new Map<string, Promise<CoverArt>>();

function loadImage(src: string, anonymous: boolean): Promise<HTMLImageElement | null> {
    return new Promise(resolve => {
        const img = new Image();
        if (anonymous) img.crossOrigin = 'anonymous';
        img.decoding = 'async';
        img.onload = () => resolve(img);
        img.onerror = () => resolve(null);
        img.src = src;
    });
}

function readPalette(img: HTMLImageElement): SynthPalette | null {
    try {
        const canvas = document.createElement('canvas');
        canvas.width = 32;
        canvas.height = 32;
        const ctx = canvas.getContext('2d', { willReadFrequently: true });
        if (!ctx) return null;
        ctx.drawImage(img, 0, 0, 32, 32);
        return paletteFromPixels(ctx.getImageData(0, 0, 32, 32).data);
    } catch {
        return null;
    }
}

// The cover comes through the server's resize cache, which is same-origin, so
// its pixels can be read for colours. A cover the browser won't let us read
// still loads for the Ring style; it just leaves the colours to the theme.
function loadCoverArt(src: string): Promise<CoverArt> {
    const cached = cache.get(src);
    if (cached) return cached;
    const pending = (async () => {
        const image = (await loadImage(src, true)) ?? (await loadImage(src, false));
        return { image, palette: image ? readPalette(image) : null };
    })();
    cache.set(src, pending);
    if (cache.size > CACHE_LIMIT) {
        const oldest = cache.keys().next().value;
        if (oldest !== undefined) cache.delete(oldest);
    }
    return pending;
}

export function useCoverArt(posterUrl: string | undefined, enabled: boolean): CoverArt {
    const src = enabled && posterUrl ? thumbUrl(posterUrl, 500) : undefined;
    const [loaded, setLoaded] = useState<{ src: string; art: CoverArt } | null>(null);

    useEffect(() => {
        if (!src) return;
        let live = true;
        loadCoverArt(src).then(art => {
            if (live) setLoaded({ src, art });
        });
        return () => { live = false; };
    }, [src]);

    return src && loaded?.src === src ? loaded.art : NO_ART;
}
