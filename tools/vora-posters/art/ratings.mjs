import { blur, linear, round } from './kit.mjs';

const glyphSize = glyph => glyph.length <= 1 ? 360 : glyph.length === 2 ? 300 : 214;

function rating(glyph, tv, palette) {
    return {
        palette,
        titleSize: 104,
        draw: p => {
            let ripples = '';
            for (let ring = 0; ring < 7; ring++) {
                const w = 680 + ring * 120;
                const h = 480 + ring * 120;
                ripples += `<rect x="${500 - w / 2}" y="${500 - h / 2}" width="${w}" height="${h}" rx="${80 + ring * 18}" fill="none" stroke="${p.a}" stroke-width="3" opacity="${round(0.4 - ring * 0.055)}"/>`;
            }
            const size = tv ? Math.min(glyphSize(glyph), 250) : glyphSize(glyph);
            const glyphY = tv ? 572 : 500;
            return {
                defs: [
                    linear('badge', [[0, p.bg, 0.55], [1, p.bg, 0.88]]),
                    linear('sheen', [[0, '#ffffff', 0.14], [0.5, '#ffffff', 0.02], [1, '#ffffff', 0]]),
                    linear('edge', [[0, p.c], [1, p.a]], [0, 0, 1, 1]),
                    linear('glyph', [[0, '#ffffff'], [0.55, '#ffffff'], [1, p.c]]),
                    blur('halo', 70),
                    blur('glyph-shadow', 10),
                ].join(''),
                body: `<g transform="translate(0 64)">
                    <rect x="200" y="300" width="600" height="400" rx="80" fill="${p.a}" opacity="0.6" filter="url(#halo)"/>
                    ${ripples}
                    <rect x="180" y="280" width="640" height="440" rx="70" fill="url(#badge)"/>
                    <rect x="180" y="280" width="640" height="440" rx="70" fill="url(#sheen)" stroke="url(#edge)" stroke-width="7"/>
                    <rect x="198" y="298" width="604" height="404" rx="54" fill="none" stroke="#ffffff" stroke-opacity="0.1" stroke-width="2"/>
                    ${tv ? `<text x="500" y="400" text-anchor="middle" font-family="Inter Tight" font-weight="800" font-size="74" letter-spacing="20" fill="${p.c}">TV</text>` : ''}
                    <text x="500" y="${glyphY + 8}" text-anchor="middle" dominant-baseline="central" font-family="Inter Tight" font-weight="900" font-size="${size}" letter-spacing="-6" fill="#000" opacity="0.45" filter="url(#glyph-shadow)">${glyph}</text>
                    <text x="500" y="${glyphY}" text-anchor="middle" dominant-baseline="central" font-family="Inter Tight" font-weight="900" font-size="${size}" letter-spacing="-6" fill="url(#glyph)">${glyph}</text></g>`,
            };
        },
    };
}

export const g = rating('G', false, { bg: '#03120a', a: '#22c55e', b: '#14532d', c: '#86efac', ink: '#ecfdf3' });
export const pg = rating('PG', false, { bg: '#03111a', a: '#0ea5e9', b: '#0c4a6e', c: '#7dd3fc', ink: '#e9f7ff' });
export const pg13 = rating('PG-13', false, { bg: '#120a02', a: '#f59e0b', b: '#7c2d12', c: '#fcd34d', ink: '#fff1d6' });
export const r = rating('R', false, { bg: '#140405', a: '#ef4444', b: '#7f1d1d', c: '#fca5a5', ink: '#fff0f0' });
export const nc17 = rating('NC-17', false, { bg: '#0c0207', a: '#be123c', b: '#4c0519', c: '#fda4af', ink: '#ffe4ea' });
export const tvY = rating('Y', true, { bg: '#121004', a: '#facc15', b: '#a16207', c: '#fef08a', ink: '#fffbe6' });
export const tvY7 = rating('Y7', true, { bg: '#130904', a: '#fb923c', b: '#9a3412', c: '#fed7aa', ink: '#fff4ea' });
export const tvG = rating('G', true, { bg: '#03120d', a: '#10b981', b: '#065f46', c: '#6ee7b7', ink: '#ecfdf5' });
export const tvPg = rating('PG', true, { bg: '#050a1a', a: '#6366f1', b: '#312e81', c: '#a5b4fc', ink: '#eef0ff' });
export const tv14 = rating('14', true, { bg: '#130a03', a: '#f59e0b', b: '#78350f', c: '#fcd34d', ink: '#fff6e0' });
export const tvMa = rating('MA', true, { bg: '#100306', a: '#e11d48', b: '#4c0519', c: '#fda4af', ink: '#ffe8ee' });
