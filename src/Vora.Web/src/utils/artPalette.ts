// Three colours for the Synth visualizer, taken from the cover: the main
// colour (the body of the bars), a second one (their middle) and a light
// tint (tips and peak dots). They are pushed bright enough to glow on the
// dark Now Playing screen, whatever the cover's own brightness.
export type SynthPalette = readonly [string, string, string];

interface Hsl {
    h: number;
    s: number;
    l: number;
}

const HUE_BINS = 24;
const MIN_CHROMA = 0.16;
const MIN_BRIGHTNESS = 46;
const MIN_VIVID_SHARE = 0.04;

function rgbToHsl(r: number, g: number, b: number): Hsl {
    const rn = r / 255;
    const gn = g / 255;
    const bn = b / 255;
    const max = Math.max(rn, gn, bn);
    const min = Math.min(rn, gn, bn);
    const l = (max + min) / 2;
    const d = max - min;
    if (d === 0) return { h: 0, s: 0, l };
    const s = d / (1 - Math.abs(2 * l - 1));
    let h: number;
    if (max === rn) h = ((gn - bn) / d) % 6;
    else if (max === gn) h = (bn - rn) / d + 2;
    else h = (rn - gn) / d + 4;
    return { h: (h * 60 + 360) % 360, s, l };
}

function hslToHex({ h, s, l }: Hsl): string {
    const c = (1 - Math.abs(2 * l - 1)) * s;
    const x = c * (1 - Math.abs(((h / 60) % 2) - 1));
    const m = l - c / 2;
    const [r, g, b] = h < 60 ? [c, x, 0] : h < 120 ? [x, c, 0] : h < 180 ? [0, c, x] : h < 240 ? [0, x, c] : h < 300 ? [x, 0, c] : [c, 0, x];
    const hex = (v: number) => Math.round((v + m) * 255).toString(16).padStart(2, '0');
    return `#${hex(r)}${hex(g)}${hex(b)}`;
}

const hueGap = (a: number, b: number) => {
    const d = Math.abs(a - b) % HUE_BINS;
    return Math.min(d, HUE_BINS - d);
};

// RGBA bytes, as from getImageData. Null when the cover is (nearly) grey, so
// the caller falls back to the template's accent instead of inventing a hue.
export function paletteFromPixels(data: ArrayLike<number>): SynthPalette | null {
    const bins = Array.from({ length: HUE_BINS }, () => ({ weight: 0, r: 0, g: 0, b: 0 }));
    let counted = 0;
    let vivid = 0;

    for (let i = 0; i + 3 < data.length; i += 4) {
        if (data[i + 3] < 128) continue;
        counted++;
        const r = data[i];
        const g = data[i + 1];
        const b = data[i + 2];
        const max = Math.max(r, g, b);
        const chroma = (max - Math.min(r, g, b)) / 255;
        if (chroma < MIN_CHROMA || max < MIN_BRIGHTNESS) continue;
        vivid++;
        const bin = Math.floor((rgbToHsl(r, g, b).h / 360) * HUE_BINS) % HUE_BINS;
        const weight = chroma * chroma;
        bins[bin].weight += weight;
        bins[bin].r += r * weight;
        bins[bin].g += g * weight;
        bins[bin].b += b * weight;
    }

    if (counted === 0 || vivid / counted < MIN_VIVID_SHARE) return null;

    const ranked = bins
        .map((bin, index) => ({ ...bin, index }))
        .filter(bin => bin.weight > 0)
        .sort((a, b) => b.weight - a.weight);
    const primary = ranked[0];
    const secondary = ranked.find(bin => hueGap(bin.index, primary.index) >= 3 && bin.weight >= primary.weight * 0.12);

    const average = (bin: typeof primary): Hsl => rgbToHsl(bin.r / bin.weight, bin.g / bin.weight, bin.b / bin.weight);
    const main = average(primary);
    const second = secondary ? average(secondary) : { ...main, h: (main.h + 28) % 360 };

    return [
        hslToHex({ h: main.h, s: Math.max(0.62, main.s), l: 0.56 }),
        hslToHex({ h: second.h, s: Math.max(0.62, second.s), l: 0.6 }),
        hslToHex({ h: main.h, s: Math.max(0.5, main.s), l: 0.8 }),
    ];
}

const parseHex = (color: string): [number, number, number] | null => {
    const m = /^#([0-9a-f]{6})$/i.exec(color.trim());
    if (!m) return null;
    const n = parseInt(m[1], 16);
    return [(n >> 16) & 255, (n >> 8) & 255, n & 255];
};

const parseRgb = (color: string): [number, number, number] | null => {
    const m = /^rgba?\(\s*([\d.]+)[,\s]+([\d.]+)[,\s]+([\d.]+)/i.exec(color.trim());
    return m ? [Number(m[1]), Number(m[2]), Number(m[3])] : null;
};

export function withAlpha(color: string, alpha: number): string {
    const rgb = parseHex(color) ?? parseRgb(color);
    return rgb ? `rgba(${rgb[0]}, ${rgb[1]}, ${rgb[2]}, ${alpha})` : color;
}

export function lighten(color: string, amount: number): string {
    const rgb = parseHex(color) ?? parseRgb(color);
    if (!rgb) return color;
    const hsl = rgbToHsl(rgb[0], rgb[1], rgb[2]);
    return hslToHex({ ...hsl, l: Math.min(0.92, hsl.l + amount) });
}
