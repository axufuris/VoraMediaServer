import { between, blur, floorBlur, floorShadow, glow, heart, linear, points, polar, radial, rays, round, shine, shineGradient, sparkles, specks, starPoints } from './kit.mjs';

export const trending = {
    palette: { bg: '#120604', a: '#f97316', b: '#dc2626', c: '#fdba74', ink: '#fff1e6' },
    draw: (p, rand) => {
        let embers = '';
        for (let index = 0; index < 26; index++) {
            embers += `<circle cx="${round(between(rand, 300, 700))}" cy="${round(between(rand, 80, 420))}" r="${round(between(rand, 2, 6))}" fill="${index % 3 === 0 ? '#fde68a' : '#fb923c'}" opacity="${round(between(rand, 0.4, 0.95))}" filter="url(#ember-glow)"/>`;
        }
        return {
            defs: [
                linear('flame-outer', [[0, '#ffb347'], [0.45, '#ff5a1f'], [1, '#a3170c']]),
                linear('flame-mid', [[0, '#ffe08a'], [1, '#ff7a1a']]),
                linear('flame-inner', [[0, '#fffbe6'], [1, '#ffc94d']]),
                glow('flame-glow', 16),
                glow('ember-glow', 4),
                blur('halo', 60),
                floorBlur,
            ].join(''),
            body: `
                <ellipse cx="500" cy="560" rx="300" ry="360" fill="#f97316" opacity="0.4" filter="url(#halo)"/>
                ${floorShadow(500, 860, 230, 26)}
                <ellipse cx="500" cy="830" rx="210" ry="30" fill="#ff7a1a" opacity="0.5" filter="url(#halo)"/>
                <path d="M500,150 C564,280 708,356 708,574 C708,740 614,840 500,840 C386,840 292,740 292,584 C292,470 356,418 390,352 C402,438 436,470 468,482 C444,380 464,264 500,150 Z" fill="url(#flame-outer)" filter="url(#flame-glow)"/>
                <path d="M506,322 C552,416 636,470 636,614 C636,726 576,798 500,798 C424,798 364,730 364,622 C364,550 406,508 430,468 C440,530 466,556 488,562 C474,484 484,404 506,322 Z" fill="url(#flame-mid)"/>
                <path d="M502,500 C534,562 576,604 576,684 C576,746 542,780 500,780 C458,780 424,748 424,692 C424,642 450,612 468,590 C474,630 490,644 500,648 C494,600 494,548 502,500 Z" fill="url(#flame-inner)"/>
                ${embers}`,
        };
    },
};

function seat(x, y, scale, fill, rim) {
    return `<g transform="translate(${x} ${y}) scale(${scale})">
        <path d="M-92,140 C-92,70 -60,40 0,40 C60,40 92,70 92,140 Z" fill="${fill}"/>
        <ellipse cx="0" cy="-10" rx="52" ry="60" fill="${fill}"/>
        <path d="M-40,-52 C-20,-72 20,-72 40,-52" stroke="${rim}" stroke-width="6" fill="none" opacity="0.6" stroke-linecap="round"/>
    </g>`;
}

export const popular = {
    palette: { bg: '#0b0614', a: '#e879f9', b: '#6366f1', c: '#f0abfc', ink: '#fbeaff' },
    draw: (p, rand) => {
        let hearts = '';
        for (let index = 0; index < 11; index++) {
            hearts += heart(between(rand, 200, 800), between(rand, 520, 720), between(rand, 22, 48), index % 2 ? '#f0abfc' : '#e879f9', between(rand, -20, 20), `opacity="${round(between(rand, 0.55, 0.95))}" filter="url(#heart-glow)"`);
        }
        let back = '';
        for (let index = 0; index < 6; index++) back += seat(170 + index * 132, 790, 0.82, '#1d0f33', '#f5d0fe');
        let front = '';
        for (let index = 0; index < 5; index++) front += seat(230 + index * 135, 900, 1.05, '#0d0618', '#f5d0fe');
        return {
            defs: [
                radial('screen', [[0, '#fdf4ff'], [0.4, '#f0abfc'], [1, '#7e22ce']], { cx: 0.5, cy: 0.5, r: 0.7 }),
                linear('beam', [[0, '#fdf4ff', 0.35], [1, '#fdf4ff', 0]], [0, 0, 0, 1]),
                glow('heart-glow', 6),
                blur('halo', 60),
            ].join(''),
            body: `
                <rect x="150" y="110" width="700" height="400" rx="20" fill="#e879f9" opacity="0.5" filter="url(#halo)"/>
                <rect x="170" y="130" width="660" height="370" rx="18" fill="url(#screen)"/>
                <polygon points="170,500 830,500 560,1000 440,1000" fill="url(#beam)" opacity="0.5"/>
                ${hearts}
                ${back}
                ${front}
                ${specks(rand, 40, [180, 140, 820, 490], '#ffffff', [1, 2.5], [0.3, 0.9])}`,
        };
    },
};

function facetedStar(cx, cy, outer, light, dark) {
    const corners = [];
    for (let index = 0; index < 10; index++) {
        corners.push(polar(cx, cy, index % 2 === 0 ? outer : outer * 0.42, -90 + index * 36));
    }
    let out = '';
    for (let index = 0; index < 10; index++) {
        const next = corners[(index + 1) % 10];
        out += `<polygon points="${points([[cx, cy], corners[index], next])}" fill="${index % 2 === 0 ? light : dark}"/>`;
    }
    return out + `<polygon points="${points(corners)}" fill="none" stroke="#7a4a05" stroke-opacity="0.35" stroke-width="3"/>`;
}

export const topRated = {
    palette: { bg: '#0f0a02', a: '#facc15', b: '#b45309', c: '#fde68a', ink: '#fff8e1' },
    draw: (p, rand) => {
        const stars = [[245, 92], [257.5, 118], [270, 156], [282.5, 118], [295, 92]].map(([angle, size]) => {
            const [x, y] = polar(500, 1400, 900, angle);
            return facetedStar(x, y, size, 'url(#gold-light)', 'url(#gold-dark)');
        }).join('');
        return {
            defs: [
                linear('gold-light', [[0, '#fff3b0'], [1, '#f5c518']], [0, 0, 1, 1]),
                linear('gold-dark', [[0, '#e8a90c'], [1, '#9a5b05']], [0, 0, 1, 1]),
                glow('star-glow', 14),
                blur('halo', 60),
            ].join(''),
            body: `
                ${rays(500, 500, 80, 560, 32, 4, '#facc15', 0.1)}
                <ellipse cx="500" cy="500" rx="380" ry="240" fill="#facc15" opacity="0.28" filter="url(#halo)"/>
                <g filter="url(#star-glow)">${stars}</g>
                ${sparkles(rand, 12, [80, 80, 920, 880], '#fff8e1', [6, 14], [0.5, 1])}
                ${specks(rand, 40, [60, 60, 940, 940], '#fde68a', [1, 3], [0.3, 0.8])}`,
        };
    },
};

export const newReleases = {
    palette: { bg: '#06070f', a: '#60a5fa', b: '#f43f5e', c: '#bfdbfe', ink: '#eef4ff' },
    draw: (p, rand) => {
        let stripes = '';
        for (let index = -2; index < 12; index++) {
            stripes += `<polygon points="${points([[250 + index * 62, 0], [282 + index * 62, 0], [252 + index * 62, 80], [220 + index * 62, 80]])}" fill="#f5f7ff"/>`;
        }
        return {
            defs: [
                linear('slate', [[0, '#2a3146'], [1, '#141826']]),
                linear('bar', [[0, '#1c2132'], [1, '#0c0f18']]),
                `<clipPath id="bar-clip"><rect x="250" y="0" width="500" height="72" rx="10"/></clipPath>`,
                glow('flash', 14),
                blur('halo', 60),
                floorBlur,
            ].join(''),
            body: `
                ${rays(500, 380, 60, 520, 24, 5, '#93c5fd', 0.12)}
                <ellipse cx="500" cy="520" rx="340" ry="280" fill="#60a5fa" opacity="0.3" filter="url(#halo)"/>
                ${floorShadow(500, 860, 290, 26)}
                <rect x="250" y="430" width="500" height="390" rx="24" fill="url(#slate)"/>
                <rect x="250" y="430" width="500" height="390" rx="24" fill="none" stroke="#ffffff" stroke-opacity="0.08" stroke-width="3"/>
                <line x1="280" y1="560" x2="720" y2="560" stroke="#cbd5f5" stroke-opacity="0.35" stroke-width="3"/>
                <line x1="280" y1="660" x2="720" y2="660" stroke="#cbd5f5" stroke-opacity="0.35" stroke-width="3"/>
                <line x1="500" y1="560" x2="500" y2="760" stroke="#cbd5f5" stroke-opacity="0.35" stroke-width="3"/>
                <text x="290" y="530" font-family="Inter Tight" font-weight="800" font-size="34" letter-spacing="6" fill="#cbd5f5" opacity="0.7">PROD.</text>
                <text x="290" y="620" font-family="Inter Tight" font-weight="800" font-size="28" letter-spacing="6" fill="#cbd5f5" opacity="0.6">SCENE</text>
                <text x="520" y="620" font-family="Inter Tight" font-weight="800" font-size="28" letter-spacing="6" fill="#cbd5f5" opacity="0.6">TAKE</text>
                <text x="290" y="725" font-family="Inter Tight" font-weight="900" font-size="60" fill="#f5f7ff" opacity="0.85">01</text>
                <text x="520" y="725" font-family="Inter Tight" font-weight="900" font-size="60" fill="#f5f7ff" opacity="0.85">01</text>
                <g transform="translate(0 360)">
                    <rect x="250" y="0" width="500" height="72" rx="10" fill="url(#bar)"/>
                    <g clip-path="url(#bar-clip)">${stripes}</g>
                </g>
                <g transform="rotate(-20 262 356)">
                    <g transform="translate(0 282)">
                        <rect x="250" y="0" width="500" height="72" rx="10" fill="url(#bar)"/>
                        <g clip-path="url(#bar-clip)">${stripes}</g>
                    </g>
                </g>
                <circle cx="266" cy="360" r="16" fill="#9aa4bf"/>
                <path d="M690,250 m0,-40 q6,34 40,40 q-34,6 -40,40 q-6,-34 -40,-40 q34,-6 40,-40 Z" fill="#ffffff" filter="url(#flash)"/>
                ${sparkles(rand, 10, [100, 100, 900, 880], '#eef4ff', [6, 13], [0.45, 0.95])}`,
        };
    },
};

function card(x, y, rotate, top, bottom, accent) {
    return `<g transform="rotate(${rotate} ${x} ${y})">
        <rect x="${x - 150}" y="${y - 225}" width="300" height="450" rx="22" fill="#000" opacity="0.35" transform="translate(10 16)"/>
        <rect x="${x - 150}" y="${y - 225}" width="300" height="450" rx="22" fill="${top}"/>
        <rect x="${x - 150}" y="${y - 225}" width="300" height="450" rx="22" fill="${bottom}" opacity="0.65"/>
        <circle cx="${x + 50}" cy="${y - 90}" r="46" fill="${accent}" opacity="0.85"/>
        <polygon points="${x - 150},${y + 90} ${x - 60},${y - 10} ${x + 10},${y + 60} ${x + 70},${y} ${x + 150},${y + 90} ${x + 150},${y + 225} ${x - 150},${y + 225}" fill="#000" opacity="0.28"/>
        <rect x="${x - 110}" y="${y + 130}" width="170" height="20" rx="10" fill="#ffffff" opacity="0.75"/>
        <rect x="${x - 110}" y="${y + 165}" width="110" height="14" rx="7" fill="#ffffff" opacity="0.4"/>
        <rect x="${x - 150}" y="${y - 225}" width="300" height="450" rx="22" fill="none" stroke="#ffffff" stroke-opacity="0.16" stroke-width="3"/>
    </g>`;
}

export const recentlyAdded = {
    palette: { bg: '#05100f', a: '#2dd4bf', b: '#0e7490', c: '#99f6e4', ink: '#e6fffb' },
    draw: (p, rand) => ({
        defs: [
            linear('card-a', [[0, '#6366f1'], [1, '#1e1b4b']], [0, 0, 1, 1]),
            linear('card-b', [[0, '#f59e0b'], [1, '#7c2d12']], [0, 0, 1, 1]),
            linear('card-c', [[0, '#2dd4bf'], [1, '#134e4a']], [0, 0, 1, 1]),
            linear('fade', [[0, '#000', 0], [1, '#000', 0.6]]),
            radial('badge', [[0, '#99f6e4'], [0.6, '#14b8a6'], [1, '#0f766e']], { cx: 0.4, cy: 0.35, r: 0.75 }),
            glow('badge-glow', 14),
            blur('halo', 60),
            floorBlur,
        ].join(''),
        body: `
            <ellipse cx="500" cy="500" rx="340" ry="320" fill="#2dd4bf" opacity="0.28" filter="url(#halo)"/>
            ${floorShadow(500, 860, 300, 28)}
            ${card(390, 520, -14, 'url(#card-a)', 'url(#fade)', '#a5b4fc')}
            ${card(610, 520, 12, 'url(#card-b)', 'url(#fade)', '#fde68a')}
            ${card(500, 500, -1, 'url(#card-c)', 'url(#fade)', '#ccfbf1')}
            <circle cx="680" cy="290" r="78" fill="url(#badge)" filter="url(#badge-glow)"/>
            <rect x="670" y="246" width="20" height="88" rx="10" fill="#f0fdfa"/>
            <rect x="636" y="280" width="88" height="20" rx="10" fill="#f0fdfa"/>
            ${sparkles(rand, 10, [100, 100, 900, 880], '#e6fffb', [6, 13], [0.45, 0.95])}`,
    }),
};

export const awardWinners = {
    palette: { bg: '#0d0902', a: '#eab308', b: '#92400e', c: '#fde047', ink: '#fff8dc' },
    draw: (p, rand) => {
        let laurel = '';
        for (const side of [-1, 1]) {
            for (let index = 0; index < 8; index++) {
                const angle = side < 0 ? 160 - index * 15 : 20 + index * 15;
                const [x, y] = polar(500, 420, 300, angle);
                const tilt = side < 0 ? angle - 60 : angle + 60;
                laurel += `<ellipse cx="${round(x)}" cy="${round(y)}" rx="40" ry="15" fill="url(#leaf)" transform="rotate(${round(tilt)} ${round(x)} ${round(y)})"/>`;
            }
        }
        let confetti = '';
        const colors = ['#fde047', '#f59e0b', '#fff8dc', '#facc15'];
        for (let index = 0; index < 34; index++) {
            const x = between(rand, 80, 920);
            const y = between(rand, 80, 880);
            confetti += `<rect x="${round(x)}" y="${round(y)}" width="12" height="6" rx="2" fill="${colors[index % 4]}" opacity="${round(between(rand, 0.5, 0.95))}" transform="rotate(${round(between(rand, 0, 180))} ${round(x)} ${round(y)})"/>`;
        }
        return {
            defs: [
                linear('gold', [[0, '#fff3b0'], [0.45, '#f2c230'], [1, '#9a6a08']], [0, 0, 1, 1]),
                linear('gold-side', [[0, '#c98f0e'], [1, '#6e4706']], [0, 0, 1, 0]),
                linear('plinth', [[0, '#3a2a12'], [1, '#1a1208']]),
                linear('leaf', [[0, '#f8d76a'], [1, '#a77a14']]),
                shineGradient('shine'),
                blur('halo', 60),
                floorBlur,
            ].join(''),
            body: `
                ${rays(500, 420, 80, 540, 28, 4, '#facc15', 0.1)}
                <ellipse cx="500" cy="440" rx="320" ry="300" fill="#eab308" opacity="0.28" filter="url(#halo)"/>
                ${confetti}
                ${laurel}
                ${floorShadow(500, 860, 250, 24)}
                <path d="M352,300 C250,300 246,452 364,470" stroke="url(#gold)" stroke-width="26" fill="none" stroke-linecap="round"/>
                <path d="M648,300 C750,300 754,452 636,470" stroke="url(#gold)" stroke-width="26" fill="none" stroke-linecap="round"/>
                <path d="M336,262 L664,262 C664,430 604,540 500,560 C396,540 336,430 336,262 Z" fill="url(#gold)"/>
                <ellipse cx="500" cy="262" rx="164" ry="22" fill="#fff3b0"/>
                <ellipse cx="500" cy="264" rx="146" ry="14" fill="#c98f0e"/>
                <polygon points="${starPoints(500, 410, 62, 26)}" fill="#c98f0e" opacity="0.7"/>
                ${shine('shine', 420, 360, 30, 80, 0.45, 10)}
                <rect x="478" y="556" width="44" height="90" fill="url(#gold-side)"/>
                <ellipse cx="500" cy="604" rx="44" ry="18" fill="url(#gold)"/>
                <path d="M424,700 L576,700 L556,640 L444,640 Z" fill="url(#gold)"/>
                <rect x="372" y="700" width="256" height="120" rx="12" fill="url(#plinth)"/>
                <rect x="430" y="736" width="140" height="44" rx="6" fill="url(#gold)" opacity="0.85"/>
                ${sparkles(rand, 10, [100, 100, 900, 700], '#fff8dc', [6, 14], [0.5, 1])}`,
        };
    },
};

export const hiddenGems = {
    palette: { bg: '#060716', a: '#22d3ee', b: '#8b5cf6', c: '#a5f3fc', ink: '#ecfeff' },
    draw: (p, rand) => {
        const top1 = [390, 340];
        const top2 = [610, 340];
        const middle = [500, 340];
        const girdle = [[240, 440], [370, 440], [500, 440], [630, 440], [760, 440]];
        const culet = [500, 780];
        const facets = [
            [[girdle[0], top1, girdle[1]], '#a5f3fc'],
            [[top1, middle, girdle[2], girdle[1]], '#67e8f9'],
            [[middle, top2, girdle[3], girdle[2]], '#c4b5fd'],
            [[top2, girdle[4], girdle[3]], '#8b5cf6'],
            [[girdle[0], girdle[1], culet], '#0e7490'],
            [[girdle[1], girdle[2], culet], '#22d3ee'],
            [[girdle[2], girdle[3], culet], '#7c3aed'],
            [[girdle[3], girdle[4], culet], '#4c1d95'],
        ];
        const gem = facets.map(([corners, fill]) => `<polygon points="${points(corners)}" fill="${fill}" stroke="#ecfeff" stroke-opacity="0.55" stroke-width="2.5" stroke-linejoin="round"/>`).join('');
        return {
            defs: [
                linear('facet-sheen', [[0, '#ffffff', 0.45], [0.5, '#ffffff', 0], [1, '#ffffff', 0.12]], [0, 0, 1, 1]),
                linear('rock', [[0, '#2a2d40'], [1, '#0b0c16']]),
                glow('gem-glow', 22),
                blur('halo', 60),
            ].join(''),
            body: `
                ${rays(500, 520, 60, 540, 18, 3, '#a5f3fc', 0.14)}
                <ellipse cx="500" cy="540" rx="320" ry="300" fill="#22d3ee" opacity="0.32" filter="url(#halo)"/>
                <g filter="url(#gem-glow)">${gem}</g>
                <polygon points="${points([girdle[0], top1, top2, girdle[4], culet])}" fill="url(#facet-sheen)"/>
                <polygon points="120,1000 180,760 300,700 380,742 450,700 560,730 640,690 760,720 860,780 900,1000" fill="url(#rock)"/>
                <polygon points="300,700 380,742 330,820 250,800" fill="#3a3e58" opacity="0.7"/>
                <polygon points="640,690 760,720 720,800 620,780" fill="#3a3e58" opacity="0.6"/>
                ${sparkles(rand, 6, [260, 300, 740, 560], '#ffffff', [10, 22], [0.7, 1])}
                ${sparkles(rand, 8, [80, 80, 920, 700], '#ecfeff', [5, 11], [0.4, 0.9])}`,
        };
    },
};

export const blockbusters = {
    palette: { bg: '#120408', a: '#ef4444', b: '#f59e0b', c: '#fecaca', ink: '#fff1f1' },
    draw: (p, rand) => {
        let stripes = '';
        for (let index = 0; index < 7; index++) {
            const topLeft = 290 + index * 60;
            const bottomLeft = 350 + index * (300 / 7);
            stripes += `<polygon points="${points([[topLeft, 470], [topLeft + 60, 470], [bottomLeft + 300 / 7, 880], [bottomLeft, 880]])}" fill="${index % 2 === 0 ? '#e11d48' : '#fff1f2'}"/>`;
        }
        const kernel = (x, y, r, fill) => {
            let puffs = '';
            for (let index = 0; index < 4; index++) {
                const [px, py] = polar(x, y, r * 0.55, index * 90 + 30);
                puffs += `<circle cx="${round(px)}" cy="${round(py)}" r="${round(r * 0.62)}" fill="${fill}"/>`;
            }
            return puffs + `<circle cx="${x}" cy="${y}" r="${round(r * 0.5)}" fill="#ffe9a8"/>`;
        };
        let pile = '';
        for (let index = 0; index < 30; index++) {
            const angle = between(rand, 180, 360);
            const reach = between(rand, 0, 1);
            const x = 500 + Math.cos((angle * Math.PI) / 180) * 210 * reach;
            const y = 470 + Math.sin((angle * Math.PI) / 180) * 150 * reach;
            pile += kernel(round(x), round(y), round(between(rand, 34, 46)), index % 3 === 0 ? '#fff6dc' : '#fde7b0');
        }
        let flying = '';
        [[230, 300, 30], [780, 260, 28], [700, 140, 22], [300, 160, 24], [840, 420, 26]].forEach(([x, y, r]) => {
            flying += kernel(x, y, r, '#fff6dc');
        });
        return {
            defs: [
                linear('beam', [[0, '#fde68a', 0.3], [1, '#fde68a', 0]]),
                linear('shade', [[0, '#000', 0.05], [0.5, '#000', 0], [1, '#000', 0.35]], [0, 0, 1, 0]),
                blur('halo', 60),
                floorBlur,
            ].join(''),
            body: `
                <polygon points="60,1000 180,1000 520,0 380,0" fill="url(#beam)" opacity="0.6"/>
                <polygon points="940,1000 820,1000 480,0 620,0" fill="url(#beam)" opacity="0.6"/>
                <ellipse cx="500" cy="520" rx="320" ry="320" fill="#ef4444" opacity="0.3" filter="url(#halo)"/>
                ${floorShadow(500, 890, 260, 26)}
                ${pile}
                ${stripes}
                <polygon points="290,470 710,470 650,880 350,880" fill="url(#shade)"/>
                <rect x="272" y="452" width="456" height="38" rx="12" fill="#fff1f2"/>
                <rect x="272" y="478" width="456" height="12" rx="6" fill="#e11d48" opacity="0.55"/>
                <circle cx="500" cy="640" r="76" fill="#fff1f2"/>
                <polygon points="${starPoints(500, 646, 54, 23)}" fill="#e11d48"/>
                ${flying}
                ${sparkles(rand, 8, [100, 100, 900, 600], '#fff1f1', [6, 12], [0.45, 0.9])}`,
        };
    },
};
