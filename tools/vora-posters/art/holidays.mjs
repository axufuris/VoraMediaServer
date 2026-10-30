import { between, blur, floorBlur, floorShadow, glow, HEART, heart, linear, linearAt, points, polar, radial, radialAt, rays, round, shine, shineGradient, sparkles, specks, starPoints } from './kit.mjs';

const bat = (x, y, scale, rotate, fill) =>
    `<path transform="translate(${x} ${y}) rotate(${rotate}) scale(${scale})" fill="${fill}" d="M0,-6 L5,-17 L8,-7 C16,-16 30,-22 46,-20 C40,-14 39,-7 42,0 C34,-4 27,-1 24,6 C18,1 11,3 8,10 C5,6 2,6 0,9 C-2,6 -5,6 -8,10 C-11,3 -18,1 -24,6 C-27,-1 -34,-4 -42,0 C-39,-7 -40,-14 -46,-20 C-30,-22 -16,-16 -8,-7 L-5,-17 Z"/>`;

function burst(rand, cx, cy, radius, color, count) {
    let out = '';
    for (let index = 0; index < count; index++) {
        const angle = (index * 360) / count + between(rand, -4, 4);
        const reach = radius * between(rand, 0.72, 1);
        const [x0, y0] = polar(cx, cy, radius * 0.16, angle);
        const [x1, y1] = polar(cx, cy, reach, angle);
        out += `<line x1="${round(x0)}" y1="${round(y0)}" x2="${round(x1)}" y2="${round(y1)}" stroke="${color}" stroke-width="4" stroke-linecap="round" opacity="0.9"/>`;
        out += `<circle cx="${round(x1)}" cy="${round(y1)}" r="${round(between(rand, 4, 7))}" fill="${color}"/>`;
    }
    return `<g filter="url(#spark-glow)">${out}<circle cx="${cx}" cy="${cy}" r="${round(radius * 0.07)}" fill="${color}"/></g>`;
}

function confetti(rand, count, box, colors) {
    let out = '';
    for (let index = 0; index < count; index++) {
        const x = between(rand, box[0], box[2]);
        const y = between(rand, box[1], box[3]);
        out += `<rect x="${round(x)}" y="${round(y)}" width="${round(between(rand, 8, 16))}" height="${round(between(rand, 4, 7))}" rx="2" fill="${colors[index % colors.length]}" opacity="${round(between(rand, 0.55, 0.95))}" transform="rotate(${round(between(rand, 0, 180))} ${round(x)} ${round(y)})"/>`;
    }
    return out;
}

const BOWL = 'M-58,-520 C-60,-400 -48,-300 -14,-250 L14,-250 C48,-300 60,-400 58,-520 Z';

function flute(rand, x, y, rotate) {
    let bubbles = '';
    for (let index = 0; index < 16; index++) {
        bubbles += `<circle cx="${round(between(rand, -36, 36))}" cy="${round(between(rand, -440, -270))}" r="${round(between(rand, 2, 5))}" fill="#fff8d6" opacity="${round(between(rand, 0.5, 0.95))}"/>`;
    }
    return `<g transform="translate(${x} ${y}) rotate(${rotate})">
        <g clip-path="url(#bowl)">
            <rect x="-70" y="-452" width="140" height="210" fill="url(#bubbly)"/>
            ${bubbles}
        </g>
        <path d="${BOWL}" fill="url(#glass)" stroke="#ffffff" stroke-opacity="0.55" stroke-width="3"/>
        <rect x="-6" y="-252" width="12" height="214" fill="url(#glass)" stroke="#ffffff" stroke-opacity="0.4" stroke-width="2"/>
        <ellipse cx="0" cy="-30" rx="74" ry="14" fill="url(#glass)" stroke="#ffffff" stroke-opacity="0.5" stroke-width="3"/>
        <path d="M-40,-505 C-44,-420 -36,-340 -18,-292" stroke="#ffffff" stroke-opacity="0.5" stroke-width="6" fill="none" stroke-linecap="round"/>
    </g>`;
}

export const newYears = {
    palette: { bg: '#060818', a: '#f5c451', b: '#7c3aed', c: '#fcd986', ink: '#fff4d6' },
    draw: (p, rand) => ({
        defs: [
            linear('bubbly', [[0, '#fff1b8'], [0.4, '#f7cf5c'], [1, '#d99a1c']]),
            linear('glass', [[0, '#ffffff', 0.22], [1, '#ffffff', 0.05]], [0, 0, 1, 1]),
            `<clipPath id="bowl"><path d="${BOWL}"/></clipPath>`,
            glow('spark-glow', 5),
            glow('clink-glow', 10),
            floorBlur,
        ].join(''),
        body: `
            ${burst(rand, 300, 270, 220, '#f5c451', 26)}
            ${burst(rand, 720, 230, 170, '#e879f9', 22)}
            ${burst(rand, 560, 470, 120, '#7dd3fc', 18)}
            ${specks(rand, 70, [30, 30, 970, 700], '#ffffff', [1, 2.6], [0.3, 0.9])}
            ${confetti(rand, 46, [60, 80, 940, 880], ['#f5c451', '#e879f9', '#7dd3fc', '#ffffff'])}
            ${floorShadow(500, 900, 260, 26)}
            ${flute(rand, 410, 900, 9)}
            ${flute(rand, 590, 900, -9)}
            <path d="M500,330 m0,-46 q6,40 46,46 q-40,6 -46,46 q-6,-40 -46,-46 q40,-6 46,-46 Z" fill="#fffbe8" filter="url(#clink-glow)"/>
            ${sparkles(rand, 8, [380, 250, 620, 420], '#fff4d6', [6, 12], [0.6, 1])}`,
    }),
};

function lantern(cx, cy, scale, stringTop) {
    return `<g transform="translate(${cx} ${cy}) scale(${scale})">
        <line x1="0" y1="${round(-(cy - stringTop) / scale)}" x2="0" y2="-150" stroke="#f4c46a" stroke-width="${round(4 / scale)}"/>
        <ellipse cx="0" cy="0" rx="190" ry="140" fill="#ff8a5c" opacity="0.4" filter="url(#lantern-blur)"/>
        <ellipse cx="0" cy="0" rx="150" ry="120" fill="url(#lantern)"/>
        <ellipse cx="0" cy="0" rx="100" ry="120" fill="none" stroke="#7f0a1a" stroke-opacity="0.45" stroke-width="4"/>
        <ellipse cx="0" cy="0" rx="48" ry="120" fill="none" stroke="#7f0a1a" stroke-opacity="0.45" stroke-width="4"/>
        <ellipse cx="-40" cy="-34" rx="34" ry="62" fill="url(#shine)" opacity="0.28"/>
        <rect x="-84" y="-140" width="168" height="30" rx="9" fill="url(#gold)"/>
        <rect x="-84" y="110" width="168" height="30" rx="9" fill="url(#gold)"/>
        <rect x="-16" y="-160" width="32" height="22" rx="5" fill="url(#gold)"/>
        <rect x="-9" y="140" width="18" height="30" fill="url(#gold)"/>
        <path d="M-22,168 L22,168 L34,270 L-34,270 Z" fill="url(#gold)"/>
        <path d="M-14,175 L-20,268 M0,175 L0,268 M14,175 L20,268" stroke="#9a5b12" stroke-opacity="0.6" stroke-width="3"/>
    </g>`;
}

export const lunarNewYear = {
    palette: { bg: '#14030a', a: '#e11d48', b: '#9f1239', c: '#fbbf24', ink: '#ffe4c4' },
    draw: (p, rand) => ({
        defs: [
            radial('lantern', [[0, '#ff7a6b'], [0.6, '#e11d48'], [1, '#7f0a1a']], { cx: 0.45, cy: 0.4, r: 0.7 }),
            linear('gold', [[0, '#fde68a'], [0.5, '#f5b83d'], [1, '#b7791f']]),
            shineGradient('shine'),
            blur('lantern-blur', 40),
        ].join(''),
        body: `
            ${specks(rand, 50, [40, 40, 960, 960], '#fbbf24', [1, 3], [0.25, 0.8])}
            ${lantern(240, 400, 0.62, -40)}
            ${lantern(770, 340, 0.56, -40)}
            ${lantern(505, 560, 1, -40)}
            ${sparkles(rand, 12, [60, 120, 940, 900], '#fde68a', [6, 14], [0.5, 1])}`,
    }),
};

export const valentinesDay = {
    palette: { bg: '#16040c', a: '#ff3d77', b: '#9d174d', c: '#ff9ab8', ink: '#ffe0ea' },
    draw: (p, rand) => {
        let tiny = '';
        for (let index = 0; index < 14; index++) {
            tiny += heart(between(rand, 80, 920), between(rand, 80, 900), between(rand, 18, 44), '#ff7aa2', between(rand, -30, 30), `opacity="${round(between(rand, 0.35, 0.85))}"`);
        }
        return {
            defs: [
                linear('heart', [[0, '#ff9dbd'], [0.5, '#ff3d77'], [1, '#9e0f3d']], [0, 0, 1, 1]),
                linear('heart-back', [[0, '#d61f5a'], [1, '#6d0a2a']], [0, 0, 1, 1]),
                shineGradient('shine'),
                blur('heart-blur', 50),
                floorBlur,
            ].join(''),
            body: `
                ${tiny}
                ${heart(700, 330, 250, 'url(#heart-back)', 16)}
                ${heart(480, 560, 620, '#ff3d77', 0, 'opacity="0.45" filter="url(#heart-blur)"')}
                ${heart(480, 540, 560, 'url(#heart)')}
                ${shine('shine', 360, 400, 70, 110, 0.4, -35)}
                <path d="M120,780 L860,260" stroke="#ffe0ea" stroke-width="10" stroke-linecap="round" opacity="0.9"/>
                <polygon points="${points([[860, 260], [812, 266], [848, 296]])}" fill="#ffe0ea"/>
                <path d="M120,780 l-20,-46 l34,10 Z M140,766 l-20,-46 l34,10 Z" fill="#ff9ab8"/>
                ${sparkles(rand, 9, [80, 100, 920, 880], '#ffe0ea', [7, 15], [0.5, 1])}`,
        };
    },
};

export const presidentsDay = {
    palette: { bg: '#050b1f', a: '#3b5bdb', b: '#b91c1c', c: '#f1d9a8', ink: '#f5ead6' },
    draw: (p, rand) => {
        let columns = '';
        for (let index = 0; index < 8; index++) {
            const x = 282 + index * 62;
            columns += `<rect x="${x}" y="530" width="36" height="236" fill="url(#stone)"/>`;
            columns += `<line x1="${x + 12}" y1="534" x2="${x + 12}" y2="762" stroke="#8c7a58" stroke-opacity="0.45" stroke-width="2"/>`;
            columns += `<line x1="${x + 24}" y1="534" x2="${x + 24}" y2="762" stroke="#8c7a58" stroke-opacity="0.45" stroke-width="2"/>`;
            columns += `<rect x="${x - 7}" y="518" width="50" height="14" rx="2" fill="url(#stone)"/>`;
            columns += `<rect x="${x - 6}" y="756" width="48" height="12" rx="2" fill="url(#stone)"/>`;
        }
        let stars = '';
        for (let index = 0; index < 13; index++) {
            const [x, y] = polar(500, 640, 400, 200 + (index * 140) / 12);
            stars += `<polygon points="${starPoints(x, y, 16, 7)}" fill="#f5ead6" opacity="0.85"/>`;
        }
        return {
            defs: [
                linear('stone', [[0, '#f8eedb'], [1, '#b8a684']]),
                linear('pediment', [[0, '#fbf3e3'], [1, '#c9b791']]),
                linear('shadow', [[0, '#0b1435', 0.9], [1, '#050b1f', 0.95]]),
                linear('star', [[0, '#fff1c4'], [1, '#e0a93a']], [0, 0, 1, 1]),
                glow('star-glow', 14),
                floorBlur,
            ].join(''),
            body: `
                ${rays(500, 380, 40, 560, 28, 4.5, '#5b7cff', 0.1)}
                ${stars}
                ${floorShadow(500, 880, 380, 28)}
                <rect x="268" y="518" width="464" height="250" fill="url(#shadow)"/>
                <polygon points="250,478 750,478 500,360" fill="url(#pediment)"/>
                <polygon points="300,466 700,466 500,386" fill="#b8a684" opacity="0.5"/>
                <rect x="244" y="470" width="512" height="50" fill="url(#stone)"/>
                <rect x="244" y="470" width="512" height="10" fill="#fff8ea" opacity="0.6"/>
                ${columns}
                <rect x="236" y="768" width="528" height="26" fill="url(#stone)"/>
                <rect x="212" y="794" width="576" height="26" fill="url(#stone)" opacity="0.92"/>
                <rect x="188" y="820" width="624" height="28" fill="url(#stone)" opacity="0.85"/>
                <polygon points="${starPoints(500, 292, 44, 19)}" fill="url(#star)" filter="url(#star-glow)"/>
                <path d="M190,890 C330,860 420,910 500,880 C580,850 680,900 810,870" stroke="#b91c1c" stroke-width="16" fill="none" stroke-linecap="round" opacity="0.85"/>
                <path d="M190,920 C330,890 420,940 500,910 C580,880 680,930 810,900" stroke="#f5ead6" stroke-width="16" fill="none" stroke-linecap="round" opacity="0.75"/>
                <path d="M190,950 C330,920 420,970 500,940 C580,910 680,960 810,930" stroke="#b91c1c" stroke-width="16" fill="none" stroke-linecap="round" opacity="0.85"/>`,
        };
    },
};

export const stPatricksDay = {
    palette: { bg: '#03140b', a: '#22c55e', b: '#065f46', c: '#facc15', ink: '#e7ffe9' },
    draw: (p, rand) => {
        const leaf = rotate => `<g transform="translate(500 470) rotate(${rotate})">
            <path d="${HEART}" transform="scale(3.4) translate(-50 -90)" fill="url(#leaf)" stroke="#065f46" stroke-opacity="0.4" stroke-width="1"/>
            <path d="M0,-20 L0,-190" stroke="#bbf7d0" stroke-opacity="0.4" stroke-width="5" stroke-linecap="round"/>
        </g>`;
        const coin = (x, y, r, squash) => `<g transform="translate(${x} ${y}) scale(1 ${squash})">
            <circle r="${r + 4}" fill="#9a6a0c"/>
            <circle r="${r}" fill="url(#coin)"/>
            <circle r="${r * 0.72}" fill="none" stroke="#b7851a" stroke-width="3" opacity="0.7"/>
            <circle cx="${-r * 0.35}" cy="${-r * 0.35}" r="${r * 0.22}" fill="#fff8d1" opacity="0.6"/>
        </g>`;
        return {
            defs: [
                radial('leaf', [[0, '#86efac'], [0.45, '#22c55e'], [1, '#065f3a']], { cx: 0.5, cy: 0.35, r: 0.75 }),
                linear('coin', [[0, '#fff1a8'], [0.5, '#f5c33b'], [1, '#c08a12']], [0, 0, 1, 1]),
                blur('leaf-blur', 46),
                floorBlur,
            ].join(''),
            body: `
                ${specks(rand, 50, [40, 40, 960, 960], '#86efac', [1, 3], [0.25, 0.75])}
                <circle cx="500" cy="440" r="300" fill="#22c55e" opacity="0.35" filter="url(#leaf-blur)"/>
                <path d="M500,480 C520,600 560,700 620,820" stroke="#15803d" stroke-width="28" fill="none" stroke-linecap="round"/>
                ${leaf(0)}
                ${leaf(120)}
                ${leaf(240)}
                ${floorShadow(500, 900, 330, 24)}
                ${coin(320, 850, 44, 0.45)}
                ${coin(400, 880, 46, 0.45)}
                ${coin(660, 868, 42, 0.45)}
                ${coin(560, 896, 40, 0.45)}
                ${coin(250, 800, 40, 1)}
                ${coin(730, 812, 38, 1)}
                ${sparkles(rand, 12, [100, 120, 900, 900], '#fde68a', [6, 14], [0.5, 1])}`,
        };
    },
};

const eggPath = (cx, cy, rx, ry) =>
    `M${cx},${cy - ry} C${cx + rx * 0.73},${cy - ry} ${cx + rx},${cy - ry * 0.2} ${cx + rx},${cy + ry * 0.25} C${cx + rx},${cy + ry * 0.75} ${cx + rx * 0.55},${cy + ry} ${cx},${cy + ry} C${cx - rx * 0.55},${cy + ry} ${cx - rx},${cy + ry * 0.75} ${cx - rx},${cy + ry * 0.25} C${cx - rx},${cy - ry * 0.2} ${cx - rx * 0.73},${cy - ry} ${cx},${cy - ry} Z`;

function zigzag(x0, x1, y, amplitude, step) {
    const corners = [];
    for (let x = x0, up = true; x <= x1; x += step, up = !up) corners.push([x, y + (up ? -amplitude : amplitude)]);
    return `<polyline points="${points(corners)}" fill="none"`;
}

function egg(id, cx, cy, rx, ry, rotate, base, accent, light, kind) {
    const left = cx - rx - 20;
    const right = cx + rx + 20;
    let pattern = '';
    if (kind === 'zigzag') {
        pattern = `<rect x="${left}" y="${cy - 40}" width="${rx * 2 + 40}" height="70" fill="${accent}"/>
            ${zigzag(left, right, cy - 5, 18, 34)} stroke="${light}" stroke-width="9"/>
            ${zigzag(left, right, cy - 120, 12, 30)} stroke="${accent}" stroke-width="8"/>
            ${zigzag(left, right, cy + 110, 12, 30)} stroke="${accent}" stroke-width="8"/>`;
    } else if (kind === 'wave') {
        pattern = `<path d="M${left},${cy} Q${cx - rx / 2},${cy - 50} ${cx},${cy} T${right},${cy}" stroke="${accent}" stroke-width="30" fill="none"/>
            <path d="M${left},${cy - 90} Q${cx - rx / 2},${cy - 130} ${cx},${cy - 90} T${right},${cy - 90}" stroke="${light}" stroke-width="12" fill="none"/>
            <path d="M${left},${cy + 90} Q${cx - rx / 2},${cy + 50} ${cx},${cy + 90} T${right},${cy + 90}" stroke="${light}" stroke-width="12" fill="none"/>`;
        for (let x = cx - rx; x <= cx + rx; x += 44) pattern += `<circle cx="${x}" cy="${cy + 150}" r="9" fill="${accent}"/>`;
    } else {
        for (let band = -2; band <= 2; band++) {
            pattern += `<rect x="${left}" y="${cy + band * 62 - 12}" width="${rx * 2 + 40}" height="24" fill="${band % 2 === 0 ? accent : light}"/>`;
        }
        for (let x = cx - rx; x <= cx + rx; x += 40) pattern += `<circle cx="${x}" cy="${cy - 186}" r="7" fill="${accent}"/>`;
    }
    return `<g transform="rotate(${rotate} ${cx} ${cy})">
        <clipPath id="${id}"><path d="${eggPath(cx, cy, rx, ry)}"/></clipPath>
        <g clip-path="url(#${id})">
            <rect x="${left}" y="${cy - ry - 20}" width="${rx * 2 + 40}" height="${ry * 2 + 40}" fill="${base}"/>
            ${pattern}
            <path d="${eggPath(cx, cy, rx, ry)}" fill="url(#egg-shade)"/>
        </g>
        <ellipse cx="${cx - rx * 0.38}" cy="${cy - ry * 0.45}" rx="${rx * 0.2}" ry="${ry * 0.28}" fill="url(#shine)" opacity="0.5" transform="rotate(-20 ${cx - rx * 0.38} ${cy - ry * 0.45})"/>
    </g>`;
}

function grass(rand, count, y0, y1, colors) {
    let out = '';
    for (let index = 0; index < count; index++) {
        const x = between(rand, 60, 940);
        const height = between(rand, 50, 130);
        const lean = between(rand, -30, 30);
        out += `<path d="M${round(x - 9)},${y1} Q${round(x + lean / 2)},${round(y1 - height / 2)} ${round(x + lean)},${round(y1 - height)} Q${round(x + lean / 2 + 4)},${round(y1 - height / 2)} ${round(x + 9)},${y1} Z" fill="${colors[index % colors.length]}"/>`;
    }
    return out;
}

const blossom = (x, y, r, petal, centre) => {
    let out = '';
    for (let index = 0; index < 5; index++) {
        const [px, py] = polar(x, y, r * 0.9, -90 + index * 72);
        out += `<circle cx="${round(px)}" cy="${round(py)}" r="${r}" fill="${petal}"/>`;
    }
    return out + `<circle cx="${x}" cy="${y}" r="${r * 0.7}" fill="${centre}"/>`;
};

export const easter = {
    palette: { bg: '#120a1f', a: '#c084fc', b: '#f9a8d4', c: '#fde68a', ink: '#fdf2ff' },
    draw: (p, rand) => ({
        defs: [
            radial('egg-shade', [[0, '#ffffff', 0.22], [0.55, '#ffffff', 0], [1, '#2a1240', 0.55]], { cx: 0.38, cy: 0.32, r: 0.78 }),
            shineGradient('shine'),
            floorBlur,
        ].join(''),
        body: `
            ${specks(rand, 50, [40, 40, 960, 900], '#fde68a', [1, 3], [0.25, 0.8])}
            ${sparkles(rand, 10, [80, 80, 920, 520], '#fdf2ff', [6, 14], [0.5, 1])}
            ${floorShadow(500, 880, 380, 30)}
            ${egg('egg-left', 300, 640, 150, 205, -16, '#a7f3d0', '#f472b6', '#fdf2ff', 'wave')}
            ${egg('egg-right', 700, 650, 145, 198, 15, '#fed7aa', '#a78bfa', '#fff7ed', 'stripes')}
            ${egg('egg-mid', 500, 560, 180, 250, 0, '#c4b5fd', '#facc15', '#fdf2ff', 'zigzag')}
            ${grass(rand, 70, 780, 905, ['#4ade80', '#22c55e', '#86efac', '#16a34a'])}
            ${blossom(170, 850, 14, '#fdf2ff', '#facc15')}
            ${blossom(840, 860, 13, '#f9a8d4', '#fde68a')}
            ${blossom(560, 890, 11, '#fdf2ff', '#f472b6')}`,
    }),
};

export const earthDay = {
    palette: { bg: '#02101a', a: '#22c55e', b: '#0ea5e9', c: '#86efac', ink: '#e6fff3' },
    draw: (p, rand) => {
        const leaf = (x, y, rotate, scale) => `<g transform="translate(${x} ${y}) rotate(${rotate}) scale(${scale})">
            <path d="M0,0 C50,-70 150,-92 250,-66 C190,4 100,36 0,0 Z" fill="url(#leaf)"/>
            <path d="M0,0 C80,-30 160,-52 240,-64" stroke="#dcfce7" stroke-opacity="0.45" stroke-width="4" fill="none"/>
        </g>`;
        return {
            defs: [
                radial('ocean', [[0, '#5fd0ff'], [0.5, '#0a77b8'], [1, '#062a48']], { cx: 0.36, cy: 0.32, r: 0.8 }),
                linear('land', [[0, '#7ee8a2'], [1, '#168c4a']], [0, 0, 1, 1]),
                linear('leaf', [[0, '#4ade80'], [1, '#15803d']], [0, 0, 1, 1]),
                radialAt('night', [[0, '#02101a', 0], [0.6, '#02101a', 0], [1, '#02101a', 0.85]], { cx: 400, cy: 400, r: 380 }),
                `<clipPath id="globe"><circle cx="500" cy="480" r="270"/></clipPath>`,
                blur('atmosphere', 18),
                blur('halo', 60),
                floorBlur,
            ].join(''),
            body: `
                ${specks(rand, 110, [20, 20, 980, 980], '#ffffff', [0.8, 2.2], [0.25, 0.9])}
                <circle cx="500" cy="480" r="320" fill="#0ea5e9" opacity="0.35" filter="url(#halo)"/>
                <circle cx="500" cy="480" r="282" fill="none" stroke="#7dd3fc" stroke-width="10" opacity="0.55" filter="url(#atmosphere)"/>
                <g clip-path="url(#globe)">
                    <circle cx="500" cy="480" r="270" fill="url(#ocean)"/>
                    <path d="M300,290 C350,230 450,222 490,268 C522,304 480,336 450,356 C424,374 438,420 404,432 C372,444 340,414 330,382 C310,352 272,336 300,290 Z" fill="url(#land)"/>
                    <path d="M420,470 C462,460 494,494 482,548 C470,610 440,684 404,712 C394,662 382,610 372,566 C362,522 386,482 420,470 Z" fill="url(#land)"/>
                    <path d="M580,318 C622,288 684,298 704,340 C724,384 712,424 690,446 C724,480 722,546 690,606 C668,650 636,670 614,650 C602,598 584,566 574,524 C558,482 552,440 574,418 C552,386 552,344 580,318 Z" fill="url(#land)"/>
                    <path d="M740,520 C770,510 790,540 780,570 C770,600 744,604 732,580 C722,560 724,530 740,520 Z" fill="url(#land)"/>
                    <ellipse cx="420" cy="370" rx="150" ry="16" fill="#ffffff" opacity="0.3" transform="rotate(-12 420 370)"/>
                    <ellipse cx="610" cy="560" rx="170" ry="14" fill="#ffffff" opacity="0.25" transform="rotate(-8 610 560)"/>
                    <circle cx="500" cy="480" r="270" fill="url(#night)"/>
                </g>
                ${floorShadow(500, 860, 260, 24, 0.45)}
                ${leaf(500, 790, 196, 1.15)}
                ${leaf(500, 790, -16, 1.15)}
                ${leaf(500, 800, 230, 0.8)}
                ${leaf(500, 800, -50, 0.8)}`,
        };
    },
};

function smallLeaf(x, y, rotate, scale, fill) {
    return `<path d="M0,0 C20,-26 60,-34 96,-24 C72,2 36,14 0,0 Z" transform="translate(${round(x)} ${round(y)}) rotate(${round(rotate)}) scale(${round(scale * 100) / 100})" fill="${fill}"/>`;
}

export const arborDay = {
    palette: { bg: '#06120a', a: '#84cc16', b: '#166534', c: '#d9f99d', ink: '#f0ffe0' },
    draw: (p, rand) => {
        const crown = [
            [300, 560, 92, 'dark'], [700, 556, 98, 'dark'], [500, 560, 160, 'dark'],
            [360, 470, 142, 'mid'], [640, 466, 146, 'mid'], [500, 420, 186, 'mid'],
            [420, 320, 132, 'light'], [590, 326, 134, 'light'], [500, 248, 118, 'light'],
        ];
        const fills = { dark: 'url(#leaf-dark)', mid: 'url(#leaf-mid)', light: 'url(#leaf-light)' };
        let leaves = '';
        for (let index = 0; index < 10; index++) {
            leaves += smallLeaf(between(rand, 120, 880), between(rand, 640, 860), between(rand, -60, 60), between(rand, 0.6, 1), '#84cc16');
        }
        return {
            defs: [
                radial('leaf-dark', [[0, '#22a050'], [1, '#0d4f26']], { cx: 0.4, cy: 0.3, r: 0.8 }),
                radial('leaf-mid', [[0, '#4cc96a'], [1, '#137a3a']], { cx: 0.4, cy: 0.3, r: 0.8 }),
                radial('leaf-light', [[0, '#a7ec7d'], [1, '#2a9a48']], { cx: 0.4, cy: 0.3, r: 0.8 }),
                linear('bark', [[0, '#8a5a35'], [0.5, '#6b4226'], [1, '#3f2614']], [0, 0, 1, 0]),
                linear('ground', [[0, '#1f6b34'], [1, '#08200f']]),
                blur('sun-blur', 60),
            ].join(''),
            body: `
                <circle cx="720" cy="240" r="200" fill="#fde68a" opacity="0.35" filter="url(#sun-blur)"/>
                <ellipse cx="500" cy="960" rx="560" ry="90" fill="url(#ground)"/>
                <path d="M424,912 C452,860 468,760 466,640 L432,566 L466,594 L478,520 L500,604 L522,520 L534,594 L568,566 L534,640 C532,760 548,860 576,912 Z" fill="url(#bark)"/>
                ${crown.map(([x, y, r, shade]) => `<circle cx="${x}" cy="${y}" r="${r}" fill="${fills[shade]}"/>`).join('')}
                <ellipse cx="430" cy="300" rx="60" ry="34" fill="#ecfccb" opacity="0.22" transform="rotate(-24 430 300)"/>
                ${leaves}
                <path d="M200,905 C200,880 204,860 210,840" stroke="#4d7c0f" stroke-width="6" fill="none"/>
                ${smallLeaf(210, 846, -60, 0.5, '#84cc16')}
                ${smallLeaf(208, 860, -140, 0.45, '#65a30d')}
                <path d="M800,905 C800,884 796,866 790,850" stroke="#4d7c0f" stroke-width="6" fill="none"/>
                ${smallLeaf(790, 856, -40, 0.5, '#84cc16')}
                ${smallLeaf(792, 868, -150, 0.42, '#65a30d')}
                ${sparkles(rand, 8, [100, 100, 900, 600], '#ecfccb', [6, 12], [0.4, 0.85])}`,
        };
    },
};

const TULIP = 'M-58,-42 C-64,22 -34,72 0,74 C34,72 64,22 58,-42 L34,-14 L16,-66 L0,-26 L-16,-66 L-34,-14 Z';
const TULIP_INNER = 'M-16,-66 L0,-26 L16,-66 C30,-20 24,40 0,60 C-24,40 -30,-20 -16,-66 Z';

export const mothersDay = {
    palette: { bg: '#170812', a: '#f472b6', b: '#be185d', c: '#fbcfe8', ink: '#ffe8f3' },
    draw: (p, rand) => {
        const tulips = [
            [372, 372, -20, 1.15, 'url(#tulip-pink)'],
            [628, 378, 20, 1.15, 'url(#tulip-peach)'],
            [500, 300, 0, 1.3, 'url(#tulip-coral)'],
            [438, 466, -8, 1.05, 'url(#tulip-rose)'],
            [566, 470, 9, 1.05, 'url(#tulip-pink)'],
        ];
        const stems = tulips.map(([x, y]) => `<path d="M${x},${y + 70} C${x},${y + 200} ${(x + 500) / 2},640 500,760" stroke="#3f8f4a" stroke-width="10" fill="none"/>`).join('');
        const heads = tulips.map(([x, y, rotate, scale, fill]) => `<g transform="translate(${x} ${y}) rotate(${rotate}) scale(${scale})">
            <path d="${TULIP}" fill="${fill}"/>
            <path d="${TULIP_INNER}" fill="#ffffff" opacity="0.18"/>
            <path d="M-40,-30 C-44,10 -26,44 -8,58" stroke="#ffffff" stroke-opacity="0.35" stroke-width="5" fill="none"/>
        </g>`).join('');
        let floating = '';
        for (let index = 0; index < 10; index++) {
            floating += heart(between(rand, 90, 910), between(rand, 90, 700), between(rand, 16, 34), '#f9a8d4', between(rand, -25, 25), `opacity="${round(between(rand, 0.35, 0.8))}"`);
        }
        return {
            defs: [
                linear('tulip-pink', [[0, '#ffb3d9'], [1, '#db2777']]),
                linear('tulip-peach', [[0, '#ffd7b5'], [1, '#f97362']]),
                linear('tulip-coral', [[0, '#ffc2b8'], [1, '#e11d48']]),
                linear('tulip-rose', [[0, '#fbcfe8'], [1, '#be185d']]),
                linear('paper-back', [[0, '#e9b79f'], [1, '#b9806a']]),
                linear('paper-front', [[0, '#fde7d9'], [1, '#e7b49b']], [0, 0, 1, 1]),
                linear('blade', [[0, '#6cc070'], [1, '#2f7a3a']]),
                floorBlur,
            ].join(''),
            body: `
                ${floating}
                ${floorShadow(500, 920, 220, 22)}
                <path d="M318,600 C400,548 600,548 682,600 L566,880 L434,880 Z" fill="url(#paper-back)"/>
                ${stems}
                <path d="M470,700 C420,620 380,560 330,520 C400,560 450,610 480,690 Z" fill="url(#blade)"/>
                <path d="M530,700 C590,610 640,560 690,530 C620,580 560,630 520,690 Z" fill="url(#blade)"/>
                ${heads}
                <path d="M296,640 C420,692 580,692 704,640 L548,912 L452,912 Z" fill="url(#paper-front)"/>
                <path d="M380,680 L470,900 M620,680 L530,900" stroke="#c9937a" stroke-opacity="0.5" stroke-width="3"/>
                <path d="M500,770 C450,720 400,740 420,780 C436,808 480,790 500,770 Z" fill="#ec4899"/>
                <path d="M500,770 C550,720 600,740 580,780 C564,808 520,790 500,770 Z" fill="#ec4899"/>
                <path d="M496,774 L470,860 M504,774 L534,856" stroke="#db2777" stroke-width="12" stroke-linecap="round"/>
                <circle cx="500" cy="772" r="16" fill="#f472b6"/>
                ${sparkles(rand, 8, [100, 100, 900, 700], '#ffe8f3', [6, 12], [0.4, 0.9])}`,
        };
    },
};

function poppy(cx, cy, scale) {
    let petals = '';
    for (let index = 0; index < 5; index++) {
        const angle = -90 + index * 72;
        const [x, y] = polar(cx, cy, 54 * scale, angle);
        petals += `<ellipse cx="${round(x)}" cy="${round(y)}" rx="${round(92 * scale)}" ry="${round(76 * scale)}" transform="rotate(${angle + 90} ${round(x)} ${round(y)})" fill="url(#petal)" stroke="#7a0d0d" stroke-opacity="0.35" stroke-width="3"/>`;
    }
    let seeds = '';
    for (let index = 0; index < 12; index++) {
        const [x, y] = polar(cx, cy, 40 * scale, index * 30);
        seeds += `<circle cx="${round(x)}" cy="${round(y)}" r="${round(5 * scale)}" fill="#2a0d0d"/>`;
    }
    return `${petals}<circle cx="${cx}" cy="${cy}" r="${round(32 * scale)}" fill="#160707"/>${seeds}<circle cx="${cx - 8 * scale}" cy="${cy - 8 * scale}" r="${round(9 * scale)}" fill="#3b1a1a"/>`;
}

export const memorialDay = {
    palette: { bg: '#070a16', a: '#dc2626', b: '#1e3a8a', c: '#fca5a5', ink: '#f5f0f0' },
    draw: (p, rand) => {
        let stars = '';
        for (let index = 0; index < 18; index++) {
            stars += `<polygon points="${starPoints(between(rand, 60, 940), between(rand, 60, 380), between(rand, 7, 14), between(rand, 3, 6))}" fill="#f5f0f0" opacity="${round(between(rand, 0.3, 0.8))}"/>`;
        }
        return {
            defs: [
                radial('petal', [[0, '#ff6a5c'], [0.55, '#dc2626'], [1, '#8f1010']], { cx: 0.5, cy: 0.7, r: 0.8 }),
                blur('halo', 60),
                floorBlur,
            ].join(''),
            body: `
                ${stars}
                <circle cx="450" cy="500" r="300" fill="#dc2626" opacity="0.3" filter="url(#halo)"/>
                <path d="M430,560 C420,700 440,800 470,920" stroke="#3f6212" stroke-width="10" fill="none"/>
                <path d="M660,460 C670,600 620,760 560,920" stroke="#3f6212" stroke-width="8" fill="none"/>
                <path d="M610,700 C600,780 590,860 600,920" stroke="#3f6212" stroke-width="7" fill="none"/>
                <path d="M446,760 C380,720 330,730 300,760 C350,770 400,780 446,760 Z" fill="#4d7c0f"/>
                <path d="M600,780 C650,740 700,740 740,760 C690,775 650,790 600,780 Z" fill="#4d7c0f"/>
                ${poppy(430, 480, 1.3)}
                ${poppy(660, 400, 0.85)}
                ${poppy(610, 660, 0.72)}
                ${sparkles(rand, 6, [100, 100, 900, 600], '#fecaca', [6, 11], [0.4, 0.8])}`,
        };
    },
};

export const fathersDay = {
    palette: { bg: '#060c18', a: '#38bdf8', b: '#1e3a8a', c: '#fbbf24', ink: '#e8f3ff' },
    draw: (p, rand) => {
        let stripes = '';
        for (let offset = -400; offset < 800; offset += 70) {
            stripes += `<line x1="${400 + offset}" y1="900" x2="${700 + offset}" y2="240" stroke="#fbbf24" stroke-width="16" opacity="0.85"/>`;
            stripes += `<line x1="${426 + offset}" y1="900" x2="${726 + offset}" y2="240" stroke="#7dd3fc" stroke-width="4" opacity="0.6"/>`;
        }
        return {
            defs: [
                linear('shirt', [[0, '#e0efff'], [1, '#93b8e0']]),
                linear('collar', [[0, '#ffffff'], [1, '#c9dcf0']], [0, 0, 1, 1]),
                linear('tie', [[0, '#1d3d8f'], [1, '#0b1b4a']], [0, 0, 1, 1]),
                linear('knot', [[0, '#2a4fa8'], [1, '#132a66']]),
                `<clipPath id="blade"><polygon points="470,338 530,338 590,772 500,874 410,772"/></clipPath>`,
                linear('fade', [[0, '#fff'], [0.55, '#fff'], [0.92, '#fff', 0]]),
                `<mask id="shirt-fade" maskContentUnits="userSpaceOnUse"><rect x="0" y="160" width="1000" height="800" fill="url(#fade)"/></mask>`,
                floorBlur,
            ].join(''),
            body: `
                ${sparkles(rand, 10, [60, 60, 940, 940], '#e8f3ff', [6, 13], [0.4, 0.9])}
                <path d="M250,250 L410,180 L590,180 L750,250 C780,480 790,720 800,960 L200,960 C210,720 220,480 250,250 Z" fill="url(#shirt)" mask="url(#shirt-fade)"/>
                <path d="M300,520 L380,520 L380,600 L300,600 Z" fill="none" stroke="#7a9cc6" stroke-width="4" opacity="0.6"/>
                <polygon points="470,338 530,338 590,772 500,874 410,772" fill="url(#tie)"/>
                <g clip-path="url(#blade)">${stripes}</g>
                <polygon points="470,338 530,338 590,772 500,874 410,772" fill="none" stroke="#08132f" stroke-width="3" opacity="0.6"/>
                <polygon points="452,252 548,252 532,340 468,340" fill="url(#knot)"/>
                <polygon points="500,250 364,166 318,306 462,336" fill="url(#collar)" stroke="#9cb7d6" stroke-width="3"/>
                <polygon points="500,250 636,166 682,306 538,336" fill="url(#collar)" stroke="#9cb7d6" stroke-width="3"/>
                <path d="M436,420 C452,540 466,660 470,780" stroke="#ffffff" stroke-opacity="0.14" stroke-width="10" fill="none"/>`,
        };
    },
};

export const independenceDay = {
    palette: { bg: '#040818', a: '#ef4444', b: '#2563eb', c: '#bfdbfe', ink: '#f1f5ff' },
    draw: (p, rand) => {
        let stripes = '';
        for (let index = 0; index < 7; index++) {
            stripes += `<rect x="0" y="${index * 50}" width="700" height="50" fill="${index % 2 === 0 ? '#dc2626' : '#f5f7ff'}"/>`;
        }
        let canton = '';
        for (let row = 0; row < 4; row++) {
            for (let col = 0; col < 5; col++) {
                canton += `<polygon points="${starPoints(32 + col * 54 + (row % 2) * 27, 26 + row * 44, 13, 5.5)}" fill="#f5f7ff"/>`;
            }
        }
        return {
            defs: [
                linearAt('wave', [[0, '#000', 0.35], [0.18, '#fff', 0.12], [0.36, '#000', 0.3], [0.56, '#fff', 0.14], [0.76, '#000', 0.32], [1, '#fff', 0.08]], [0, 0, 700, 0]),
                glow('spark-glow', 5),
                floorBlur,
            ].join(''),
            body: `
                ${specks(rand, 60, [30, 30, 970, 600], '#ffffff', [1, 2.4], [0.3, 0.9])}
                ${burst(rand, 300, 250, 210, '#ef4444', 26)}
                ${burst(rand, 700, 220, 180, '#93c5fd', 24)}
                ${burst(rand, 520, 430, 140, '#f8fafc', 20)}
                ${burst(rand, 820, 500, 100, '#ef4444', 16)}
                ${burst(rand, 170, 520, 90, '#93c5fd', 14)}
                <g transform="translate(150 600) rotate(-5) skewY(3)">
                    ${stripes}
                    <rect x="0" y="0" width="276" height="200" fill="#1e3a8a"/>
                    ${canton}
                    <rect x="0" y="0" width="700" height="350" fill="url(#wave)"/>
                </g>`,
        };
    },
};

function gearPoints(cx, cy, outer, inner, teeth) {
    const corners = [];
    const step = 360 / teeth;
    for (let index = 0; index < teeth; index++) {
        const start = index * step;
        corners.push(polar(cx, cy, inner, start));
        corners.push(polar(cx, cy, outer, start + step * 0.18));
        corners.push(polar(cx, cy, outer, start + step * 0.5));
        corners.push(polar(cx, cy, inner, start + step * 0.68));
    }
    return points(corners);
}

export const laborDay = {
    palette: { bg: '#0c0a06', a: '#f59e0b', b: '#1d4ed8', c: '#fde68a', ink: '#fff6e0' },
    draw: (p, rand) => ({
        defs: [
            linear('steel', [[0, '#9fb3cf'], [0.5, '#5f7697'], [1, '#2c3a52']], [0, 0, 1, 1]),
            linear('hat', [[0, '#ffe27a'], [0.55, '#f5b31a'], [1, '#c2780a']], [0, 0, 1, 1]),
            linear('brim', [[0, '#f8c23a'], [1, '#b56d06']]),
            linear('handle', [[0, '#a5703f'], [1, '#6b4122']]),
            blur('halo', 60),
            floorBlur,
        ].join(''),
        body: `
            <circle cx="500" cy="470" r="330" fill="#f59e0b" opacity="0.25" filter="url(#halo)"/>
            <g fill-rule="evenodd">
                <path d="M${gearPoints(500, 440, 310, 262, 14).split(' ').join(' L')} Z M500,330 A110,110 0 1 0 500.1,330 Z" fill="url(#steel)"/>
            </g>
            <circle cx="500" cy="440" r="190" fill="none" stroke="#c7d4e8" stroke-opacity="0.25" stroke-width="6"/>
            <g transform="rotate(38 500 600)">
                <rect x="480" y="300" width="40" height="560" rx="14" fill="url(#handle)"/>
                <rect x="410" y="270" width="180" height="70" rx="12" fill="url(#steel)"/>
            </g>
            <g transform="rotate(-38 500 600)">
                <rect x="478" y="330" width="44" height="520" rx="20" fill="url(#steel)"/>
                <path d="M440,330 A62,62 0 1 1 560,330 L536,330 L536,300 L464,300 L464,330 Z" fill="url(#steel)"/>
            </g>
            ${floorShadow(500, 760, 300, 26, 0.6)}
            <path d="M282,690 C282,510 392,424 500,424 C608,424 718,510 718,690 Z" fill="url(#hat)"/>
            <rect x="466" y="426" width="68" height="266" rx="26" fill="#ffe991" opacity="0.55"/>
            <path d="M360,690 C360,580 400,500 450,460" stroke="#fff3b0" stroke-opacity="0.4" stroke-width="10" fill="none" stroke-linecap="round"/>
            <path d="M222,690 C222,662 252,650 296,650 L704,650 C748,650 778,662 778,690 C778,718 748,728 704,728 L296,728 C252,728 222,718 222,690 Z" fill="url(#brim)"/>
            ${sparkles(rand, 8, [100, 100, 900, 900], '#fde68a', [6, 12], [0.4, 0.85])}`,
    }),
};

export const veteransDay = {
    palette: { bg: '#070b18', a: '#c8a24a', b: '#1e3a8a', c: '#f3d98b', ink: '#f6efe0' },
    draw: (p, rand) => {
        let laurel = '';
        for (const side of [-1, 1]) {
            for (let index = 0; index < 9; index++) {
                const angle = side < 0 ? 100 + index * 17 : 80 - index * 17;
                const [x, y] = polar(500, 610, 222, angle);
                laurel += `<ellipse cx="${round(x)}" cy="${round(y)}" rx="34" ry="13" fill="url(#leaf)" transform="rotate(${round(angle + 90 * side + (side < 0 ? 180 : 0) - 40 * side)} ${round(x)} ${round(y)})"/>`;
            }
        }
        return {
            defs: [
                linear('gold', [[0, '#fff1bf'], [0.5, '#e0b54c'], [1, '#9a6d18']], [0, 0, 1, 1]),
                linear('gold-dark', [[0, '#c79a35'], [1, '#7a5310']], [0, 0, 1, 1]),
                linear('leaf', [[0, '#d8c07a'], [1, '#8a6c22']]),
                `<clipPath id="ribbon"><polygon points="378,150 622,150 622,410 500,484 378,410"/></clipPath>`,
                glow('soft', 10),
                floorBlur,
            ].join(''),
            body: `
                ${rays(500, 610, 60, 520, 30, 4, '#c8a24a', 0.09)}
                ${laurel}
                <g clip-path="url(#ribbon)">
                    <rect x="378" y="150" width="244" height="340" fill="#1e3a8a"/>
                    <rect x="420" y="150" width="160" height="340" fill="#f6efe0"/>
                    <rect x="456" y="150" width="88" height="340" fill="#b91c1c"/>
                    <rect x="486" y="150" width="28" height="340" fill="#f6efe0"/>
                    <rect x="378" y="150" width="244" height="340" fill="url(#gold-dark)" opacity="0.12"/>
                </g>
                <rect x="482" y="470" width="36" height="44" rx="8" fill="url(#gold)"/>
                ${floorShadow(500, 860, 200, 22)}
                <circle cx="500" cy="640" r="168" fill="url(#gold-dark)"/>
                <circle cx="500" cy="640" r="152" fill="url(#gold)"/>
                <circle cx="500" cy="640" r="132" fill="none" stroke="#9a6d18" stroke-width="4" opacity="0.6"/>
                <polygon points="${starPoints(500, 646, 112, 46)}" fill="url(#gold-dark)" filter="url(#soft)"/>
                <polygon points="${starPoints(500, 640, 104, 42)}" fill="url(#gold)"/>
                <ellipse cx="452" cy="572" rx="36" ry="20" fill="#fffbe6" opacity="0.4" transform="rotate(-30 452 572)"/>`,
        };
    },
};

const MAPLE = (() => {
    const half = [[0, -1], [0.1, -0.74], [0.28, -0.86], [0.24, -0.5], [0.5, -0.62], [0.44, -0.4], [0.64, -0.42], [0.53, -0.25], [0.88, -0.2], [0.72, -0.04], [0.84, 0.08], [0.5, 0.13], [0.56, 0.3], [0.28, 0.25], [0.3, 0.44], [0.06, 0.36], [0.05, 0.8]];
    const mirrored = half.slice(1, -1).reverse().map(([x, y]) => [-x, y]);
    return points([...half, [-0.05, 0.8], ...mirrored].map(([x, y]) => [x * 100, y * 100]));
})();

function maple(x, y, size, rotate, fill) {
    return `<g transform="translate(${x} ${y}) rotate(${rotate}) scale(${round(size / 200 * 100) / 100})">
        <polygon points="${MAPLE}" fill="${fill}"/>
        <path d="M0,70 L0,-90 M0,10 L70,-30 M0,10 L-70,-30 M0,30 L48,22 M0,30 L-48,22" stroke="#3a1606" stroke-opacity="0.35" stroke-width="4" fill="none"/>
    </g>`;
}

export const thanksgiving = {
    palette: { bg: '#140904', a: '#ea580c', b: '#7c2d12', c: '#fbbf24', ink: '#fff0dc' },
    draw: (p, rand) => ({
        defs: [
            linear('leaf-orange', [[0, '#ffb35c'], [1, '#d9480f']], [0, 0, 1, 1]),
            linear('leaf-red', [[0, '#ff7a59'], [1, '#9f1d14']], [0, 0, 1, 1]),
            linear('leaf-gold', [[0, '#ffe08a'], [1, '#d18b0a']], [0, 0, 1, 1]),
            linear('leaf-brown', [[0, '#c98a4b'], [1, '#6e3a14']], [0, 0, 1, 1]),
            radial('pumpkin', [[0, '#ffbd6b'], [0.5, '#f0801c'], [1, '#9c3d07']], { cx: 0.4, cy: 0.3, r: 0.8 }),
            blur('halo', 60),
            floorBlur,
        ].join(''),
        body: `
            <circle cx="500" cy="460" r="300" fill="#ea580c" opacity="0.28" filter="url(#halo)"/>
            ${maple(320, 640, 240, -40, 'url(#leaf-brown)')}
            ${maple(650, 380, 300, 22, 'url(#leaf-red)')}
            ${maple(410, 400, 380, -16, 'url(#leaf-orange)')}
            ${maple(600, 620, 300, 12, 'url(#leaf-gold)')}
            ${maple(760, 640, 170, 42, 'url(#leaf-orange)')}
            ${maple(480, 210, 150, 6, 'url(#leaf-gold)')}
            ${floorShadow(560, 905, 250, 24)}
            <ellipse cx="470" cy="840" rx="84" ry="66" fill="url(#pumpkin)"/>
            <ellipse cx="650" cy="840" rx="84" ry="66" fill="url(#pumpkin)"/>
            <ellipse cx="560" cy="830" rx="100" ry="80" fill="url(#pumpkin)"/>
            <path d="M552,756 C548,736 556,722 572,712 L582,722 C570,732 566,744 568,758 Z" fill="#5d7a2c"/>
            <ellipse cx="320" cy="880" rx="34" ry="26" fill="#9a5a24"/>
            <path d="M286,872 C290,846 350,846 354,872 Z" fill="#5e3412"/>
            ${specks(rand, 30, [80, 120, 920, 900], '#fbbf24', [1.5, 4], [0.25, 0.8])}`,
    }),
};

export const hanukkah = {
    palette: { bg: '#040a1f', a: '#3b82f6', b: '#1e40af', c: '#fcd34d', ink: '#e7efff' },
    draw: (p, rand) => {
        const arms = [70, 140, 210, 280];
        const tips = [500, ...arms.flatMap(radius => [500 - radius, 500 + radius])];
        const flame = (x, y) => `<path d="M${x},${y - 40} C${x + 17},${y - 14} ${x + 17},${y + 10} ${x},${y + 14} C${x - 17},${y + 10} ${x - 17},${y - 14} ${x},${y - 40} Z" fill="url(#flame)" filter="url(#flame-glow)"/>`;
        const candles = tips.map(x => {
            const cup = x === 500 ? 350 : 430;
            return `<rect x="${x - 11}" y="${cup - 92}" width="22" height="92" rx="4" fill="url(#candle)"/>${flame(x, cup - 112)}<rect x="${x - 24}" y="${cup - 8}" width="48" height="22" rx="7" fill="url(#gold)"/>`;
        }).join('');
        return {
            defs: [
                linear('gold', [[0, '#fff0b3'], [0.5, '#e8b93e'], [1, '#9c6f12']], [0, 0, 1, 1]),
                linear('candle', [[0, '#f8fbff'], [1, '#9cc3ff']], [0, 0, 1, 0]),
                radial('flame', [[0, '#fffdf0'], [0.45, '#ffe17a'], [1, '#ff9d1c']], { cx: 0.5, cy: 0.65, r: 0.7 }),
                glow('flame-glow', 8),
                blur('halo', 50),
                floorBlur,
            ].join(''),
            body: `
                <polygon points="${points([polar(500, 470, 360, -90), polar(500, 470, 360, 30), polar(500, 470, 360, 150)])}" fill="none" stroke="#3b82f6" stroke-width="6" opacity="0.22"/>
                <polygon points="${points([polar(500, 470, 360, 90), polar(500, 470, 360, 210), polar(500, 470, 360, 330)])}" fill="none" stroke="#3b82f6" stroke-width="6" opacity="0.22"/>
                <ellipse cx="500" cy="300" rx="380" ry="120" fill="#ffd36b" opacity="0.2" filter="url(#halo)"/>
                ${floorShadow(500, 880, 220, 22)}
                ${arms.map(radius => `<path d="M${500 - radius},440 A${radius},${radius * 0.95} 0 0 0 ${500 + radius},440" stroke="url(#gold)" stroke-width="18" fill="none"/>`).join('')}
                <rect x="488" y="350" width="24" height="480" fill="url(#gold)"/>
                <path d="M400,880 L600,880 L560,820 L440,820 Z" fill="url(#gold)"/>
                <ellipse cx="500" cy="880" rx="110" ry="16" fill="url(#gold)"/>
                ${candles}
                ${specks(rand, 50, [40, 40, 960, 960], '#cfe0ff', [1, 2.6], [0.25, 0.8])}`,
        };
    },
};

export const prideMonth = {
    palette: { bg: '#0a0614', a: '#a855f7', b: '#ec4899', c: '#fde047', ink: '#fdf4ff' },
    draw: (p, rand) => {
        const colors = ['#ef4444', '#f97316', '#facc15', '#22c55e', '#3b82f6', '#8b5cf6'];
        const arcs = colors.map((color, index) => {
            const radius = 390 - index * 50;
            return `<path d="M${500 - radius},760 A${radius},${radius} 0 0 1 ${500 + radius},760" stroke="${color}" stroke-width="44" fill="none" stroke-linecap="round"/>`;
        }).join('');
        let hearts = '';
        for (let index = 0; index < 10; index++) {
            hearts += heart(between(rand, 90, 910), between(rand, 90, 330), between(rand, 18, 36), colors[index % colors.length], between(rand, -25, 25), `opacity="${round(between(rand, 0.5, 0.9))}"`);
        }
        return {
            defs: [
                blur('halo', 40),
                linear('cloud', [[0, '#ffffff'], [1, '#d8d4f0']]),
            ].join(''),
            body: `
                <g opacity="0.5" filter="url(#halo)">${arcs}</g>
                ${arcs}
                ${hearts}
                <g fill="url(#cloud)">
                    <circle cx="120" cy="772" r="56"/><circle cx="180" cy="752" r="70"/><circle cx="250" cy="784" r="50"/><rect x="80" y="784" width="210" height="56" rx="28"/>
                    <circle cx="880" cy="772" r="56"/><circle cx="820" cy="752" r="70"/><circle cx="750" cy="784" r="50"/><rect x="710" y="784" width="210" height="56" rx="28"/>
                </g>
                ${sparkles(rand, 12, [80, 80, 920, 680], '#fdf4ff', [6, 14], [0.5, 1])}`,
        };
    },
};

export const blackHistoryMonth = {
    palette: { bg: '#0c0805', a: '#f59e0b', b: '#b91c1c', c: '#fcd34d', ink: '#fff3dc' },
    draw: (p, rand) => {
        const colors = ['#f2b705', '#15803d', '#b91c1c', '#111111'];
        let weave = '';
        for (let column = 0; column < 9; column++) {
            for (let row = 0; row < 9; row++) {
                const x = 220 + column * 64;
                const y = 190 + row * 64;
                const base = colors[(column + row * 2) % colors.length];
                const accent = colors[(column + row + 1) % colors.length];
                weave += `<rect x="${x}" y="${y}" width="64" height="64" fill="${base}"/>`;
                const kind = Math.floor(rand() * 4);
                if (kind === 0) {
                    for (let band = 0; band < 4; band++) weave += `<rect x="${x}" y="${y + 6 + band * 15}" width="64" height="6" fill="${accent}"/>`;
                } else if (kind === 1) {
                    for (let band = 0; band < 4; band++) weave += `<rect x="${x + 6 + band * 15}" y="${y}" width="6" height="64" fill="${accent}"/>`;
                } else if (kind === 2) {
                    weave += `<polygon points="${x + 32},${y + 6} ${x + 58},${y + 32} ${x + 32},${y + 58} ${x + 6},${y + 32}" fill="${accent}"/>`;
                } else {
                    weave += `<rect x="${x}" y="${y}" width="32" height="32" fill="${accent}"/><rect x="${x + 32}" y="${y + 32}" width="32" height="32" fill="${accent}"/>`;
                }
            }
        }
        return {
            defs: [
                `<clipPath id="medallion"><circle cx="500" cy="480" r="270"/></clipPath>`,
                radial('depth', [[0, '#000', 0], [0.7, '#000', 0.1], [1, '#000', 0.6]]),
                linear('gold', [[0, '#fff1b8'], [0.5, '#e8b23a'], [1, '#8f5f0e']], [0, 0, 1, 1]),
                shineGradient('shine'),
                floorBlur,
            ].join(''),
            body: `
                ${rays(500, 480, 290, 540, 36, 4, '#f59e0b', 0.14)}
                ${floorShadow(500, 860, 260, 24)}
                <g clip-path="url(#medallion)">
                    ${weave}
                    <circle cx="500" cy="480" r="270" fill="url(#depth)"/>
                </g>
                <circle cx="500" cy="480" r="282" fill="none" stroke="url(#gold)" stroke-width="22"/>
                <circle cx="500" cy="480" r="262" fill="none" stroke="#0c0805" stroke-width="4"/>
                ${shine('shine', 400, 360, 120, 60, 0.18, -30)}
                ${sparkles(rand, 8, [80, 80, 920, 900], '#fde68a', [6, 12], [0.4, 0.9])}`,
        };
    },
};

export const halloween = {
    palette: { bg: '#0d0614', a: '#ff7a1a', b: '#6d28d9', c: '#ffb454', ink: '#ffe2c2' },
    draw: (p, rand) => ({
        defs: [
            radialAt('moon', [[0, '#fff6dc'], [0.65, '#ffd796'], [1, '#ffa94d']], { cx: 650, cy: 250, r: 190 }),
            radial('lobe', [[0, '#ffc46b'], [0.45, '#ff8a1f'], [1, '#9c3108']], { cx: 0.38, cy: 0.3, r: 0.8 }),
            radial('lobe-back', [[0, '#e07a24'], [1, '#5e1c04']], { cx: 0.5, cy: 0.3, r: 0.8 }),
            radial('carve', [[0, '#fffbe0'], [0.4, '#ffe066'], [1, '#ff9a00']], { cx: 0.5, cy: 0.5, r: 0.7 }),
            linear('stem', [[0, '#7c9a45'], [1, '#33441a']], [0, 0, 1, 1]),
            glow('carve-glow', 14),
            blur('moon-blur', 46),
            shineGradient('shine'),
            floorBlur,
        ].join(''),
        body: `
            <circle cx="650" cy="250" r="250" fill="#ffb35c" opacity="0.32" filter="url(#moon-blur)"/>
            <circle cx="650" cy="250" r="190" fill="url(#moon)"/>
            <circle cx="590" cy="215" r="34" fill="#f2b56e" opacity="0.35"/>
            <circle cx="700" cy="300" r="22" fill="#f2b56e" opacity="0.3"/>
            <circle cx="712" cy="190" r="14" fill="#f2b56e" opacity="0.3"/>
            ${bat(470, 190, 2.1, -12, '#160a22')}
            ${bat(800, 140, 1.4, 14, '#160a22')}
            ${bat(560, 360, 1.1, -4, '#1d0d2b')}
            ${bat(250, 300, 1.6, -20, '#1a0b27')}
            ${floorShadow(500, 915, 340, 38)}
            <ellipse cx="270" cy="700" rx="120" ry="178" fill="url(#lobe-back)"/>
            <ellipse cx="730" cy="700" rx="120" ry="178" fill="url(#lobe-back)"/>
            <ellipse cx="360" cy="700" rx="152" ry="206" fill="url(#lobe)" stroke="#5e1c04" stroke-opacity="0.55" stroke-width="5"/>
            <ellipse cx="640" cy="700" rx="152" ry="206" fill="url(#lobe)" stroke="#5e1c04" stroke-opacity="0.55" stroke-width="5"/>
            <ellipse cx="500" cy="700" rx="138" ry="216" fill="url(#lobe)" stroke="#5e1c04" stroke-opacity="0.6" stroke-width="5"/>
            <path d="M478,500 C472,462 486,434 514,414 L540,430 C520,448 514,472 520,502 Z" fill="url(#stem)"/>
            <path d="M522,478 C560,432 616,452 606,494 C598,524 562,516 570,490" fill="none" stroke="#4f6c24" stroke-width="9" stroke-linecap="round"/>
            <g fill="#4a1503" transform="translate(0 6)">
                <polygon points="356,632 462,610 412,540"/>
                <polygon points="538,610 644,632 588,540"/>
                <polygon points="476,680 524,680 500,638"/>
                <path d="M336,712 C420,760 580,760 664,712 C648,788 586,838 500,840 C414,838 352,788 336,712 Z"/>
            </g>
            <g filter="url(#carve-glow)" fill="url(#carve)">
                <polygon points="366,626 452,608 412,552"/>
                <polygon points="548,608 634,626 588,552"/>
                <polygon points="482,674 518,674 500,644"/>
                <path d="M348,716 C424,758 576,758 652,716 C636,782 582,826 500,828 C418,826 364,782 348,716 Z"/>
            </g>
            <rect x="436" y="744" width="36" height="30" rx="4" fill="#d9661a"/>
            <rect x="530" y="744" width="36" height="30" rx="4" fill="#d9661a"/>
            ${shine('shine', 420, 590, 52, 120, 0.2, -18)}
            ${specks(rand, 30, [110, 320, 890, 900], '#ffb347', [1.5, 4.5], [0.25, 0.9])}`,
    }),
};

function tier(apex, bottom, halfWidth) {
    const drop = (bottom - apex) * 0.45;
    return `M500,${apex} C${500 + halfWidth * 0.22},${apex + drop} ${500 + halfWidth * 0.72},${bottom - 34} ${500 + halfWidth},${bottom} Q${500 + halfWidth * 0.5},${bottom + 24} 500,${bottom + 8} Q${500 - halfWidth * 0.5},${bottom + 24} ${500 - halfWidth},${bottom} C${500 - halfWidth * 0.72},${bottom - 34} ${500 - halfWidth * 0.22},${apex + drop} 500,${apex} Z`;
}

function lights(rand, x0, y0, x1, y1, sag, count) {
    let out = `<path d="M${x0},${y0} Q${(x0 + x1) / 2},${(y0 + y1) / 2 + sag} ${x1},${y1}" fill="none" stroke="#2b1a0e" stroke-width="3" opacity="0.6"/>`;
    const colors = ['#ffe29a', '#ff6b6b', '#ffe29a', '#8be9ff'];
    for (let index = 1; index < count; index++) {
        const t = index / count;
        const cx = (1 - t) * (1 - t) * x0 + 2 * (1 - t) * t * ((x0 + x1) / 2) + t * t * x1;
        const cy = (1 - t) * (1 - t) * y0 + 2 * (1 - t) * t * ((y0 + y1) / 2 + sag) + t * t * y1;
        out += `<circle cx="${round(cx)}" cy="${round(cy)}" r="${round(6 + rand() * 2)}" fill="${colors[index % colors.length]}" filter="url(#bulb-glow)"/>`;
    }
    return out;
}

const ornament = (x, y, r, color) =>
    `<circle cx="${x}" cy="${y}" r="${r}" fill="${color}"/><circle cx="${x - r * 0.35}" cy="${y - r * 0.35}" r="${r * 0.28}" fill="#fff" opacity="0.7"/><rect x="${x - 5}" y="${y - r - 7}" width="10" height="9" rx="2" fill="#d9b25c"/>`;

const gift = (x, y, w, h, box, ribbon) =>
    `<rect x="${x}" y="${y}" width="${w}" height="${h}" rx="6" fill="${box}"/><rect x="${x - 6}" y="${y - 4}" width="${w + 12}" height="22" rx="5" fill="${box}" stroke="#000" stroke-opacity="0.15"/><rect x="${x + w / 2 - 9}" y="${y - 4}" width="18" height="${h + 4}" fill="${ribbon}"/><path d="M${x + w / 2},${y - 4} C${x + w / 2 - 40},${y - 44} ${x + w / 2 - 50},${y - 4} ${x + w / 2},${y - 4} C${x + w / 2 + 50},${y - 4} ${x + w / 2 + 40},${y - 44} ${x + w / 2},${y - 4} Z" fill="${ribbon}"/>`;

export const christmas = {
    palette: { bg: '#04140d', a: '#e8a93a', b: '#b91c1c', c: '#f5c96b', ink: '#fff1d6' },
    draw: (p, rand) => {
        const tiers = [[560, 852, 330], [440, 712, 262], [334, 572, 196], [246, 434, 128]];
        const treeTiers = tiers.map(([apex, bottom, half]) =>
            `<path d="${tier(apex, bottom, half)}" fill="url(#tree)"/><path d="${tier(apex, bottom, half)}" fill="url(#tier-shade)"/>`).join('');
        return {
            defs: [
                linear('tree', [[0, '#3bbf75'], [0.5, '#178a4e'], [1, '#0a4a2d']], [0, 0, 1, 0]),
                linear('tier-shade', [[0, '#000', 0], [0.7, '#000', 0], [1, '#000', 0.35]]),
                linear('star', [[0, '#fff3c4'], [0.5, '#ffd36b'], [1, '#e89a1c']], [0, 0, 1, 1]),
                glow('bulb-glow', 6),
                glow('star-glow', 18),
                blur('halo', 40),
                blur('snow-blur', 14),
                floorBlur,
            ].join(''),
            body: `
                <circle cx="500" cy="230" r="150" fill="#ffcf6b" opacity="0.28" filter="url(#halo)"/>
                ${floorShadow(500, 915, 380, 34, 0.6)}
                <ellipse cx="500" cy="918" rx="420" ry="30" fill="#e8f4ff" opacity="0.28" filter="url(#snow-blur)"/>
                <rect x="462" y="830" width="76" height="80" rx="6" fill="#5a3a22"/>
                ${treeTiers}
                ${lights(rand, 230, 790, 760, 770, 48, 12)}
                ${lights(rand, 290, 655, 700, 640, 40, 9)}
                ${lights(rand, 345, 520, 645, 512, 32, 7)}
                ${lights(rand, 405, 398, 590, 392, 22, 5)}
                ${ornament(330, 820, 24, '#d62839')}
                ${ornament(640, 812, 22, '#f2b33d')}
                ${ornament(500, 760, 20, '#e8e8f2')}
                ${ornament(400, 690, 19, '#f2b33d')}
                ${ornament(600, 690, 21, '#d62839')}
                ${ornament(455, 560, 18, '#d62839')}
                ${ornament(565, 548, 16, '#e8e8f2')}
                ${ornament(500, 440, 15, '#f2b33d')}
                <polygon points="${starPoints(500, 224, 70, 30)}" fill="url(#star)" filter="url(#star-glow)"/>
                ${gift(268, 846, 118, 76, '#c81e3a', '#f2c14e')}
                ${gift(616, 860, 96, 62, '#e5b23e', '#c81e3a')}
                ${specks(rand, 70, [40, 40, 960, 900], '#ffffff', [1.5, 4.5], [0.2, 0.85])}
                ${sparkles(rand, 6, [120, 120, 880, 500], '#fff3c4', [8, 16], [0.5, 0.9])}`,
        };
    },
};
