import { between, blur, floorBlur, floorShadow, glow, heart, linear, linearAt, points, polar, radial, radialAt, rays, round, shine, shineGradient, sparkles, specks, starPoints } from './kit.mjs';

function jagged(rand, cx, cy, outer, inner, spikes) {
    const corners = [];
    for (let index = 0; index < spikes * 2; index++) {
        const radius = index % 2 === 0 ? outer * between(rand, 0.78, 1) : inner * between(rand, 0.85, 1.1);
        corners.push(polar(cx, cy, radius, (index * 180) / spikes + between(rand, -4, 4)));
    }
    return points(corners);
}

export const action = {
    palette: { bg: '#120604', a: '#f97316', b: '#dc2626', c: '#fde68a', ink: '#fff3e0' },
    draw: (p, rand) => {
        let smoke = '';
        for (let index = 0; index < 14; index++) {
            const [x, y] = polar(500, 520, between(rand, 300, 400), between(rand, 0, 360));
            smoke += `<circle cx="${round(x)}" cy="${round(y)}" r="${round(between(rand, 60, 110))}" fill="${index % 2 ? '#3b2a24' : '#4d3a32'}" opacity="0.85"/>`;
        }
        let debris = '';
        let streaks = '';
        for (let index = 0; index < 16; index++) {
            const angle = between(rand, 0, 360);
            const [x, y] = polar(500, 520, between(rand, 330, 460), angle);
            const [sx, sy] = polar(500, 520, between(rand, 220, 300), angle);
            streaks += `<line x1="${round(sx)}" y1="${round(sy)}" x2="${round(x)}" y2="${round(y)}" stroke="#ffd08a" stroke-width="3" opacity="0.55" stroke-linecap="round"/>`;
            debris += `<polygon points="${jagged(rand, round(x), round(y), 18, 9, 4)}" fill="#1a0d08"/>`;
        }
        return {
            defs: [
                glow('fire-glow', 18),
                blur('halo', 70),
                blur('smoke-blur', 12),
            ].join(''),
            body: `
                <circle cx="500" cy="520" r="420" fill="#f97316" opacity="0.35" filter="url(#halo)"/>
                <g filter="url(#smoke-blur)">${smoke}</g>
                ${streaks}
                <g filter="url(#fire-glow)">
                    <polygon points="${jagged(rand, 500, 520, 420, 250, 14)}" fill="#a3180c"/>
                    <polygon points="${jagged(rand, 500, 520, 340, 210, 13)}" fill="#f2560a"/>
                    <polygon points="${jagged(rand, 500, 520, 250, 160, 12)}" fill="#ffad1f"/>
                    <polygon points="${jagged(rand, 500, 520, 150, 100, 10)}" fill="#fff3c4"/>
                </g>
                ${debris}
                ${specks(rand, 40, [80, 80, 920, 920], '#ffd08a', [1.5, 4], [0.35, 0.95])}`,
        };
    },
};

export const adventure = {
    palette: { bg: '#06100f', a: '#f59e0b', b: '#0f766e', c: '#fde68a', ink: '#fff8e6' },
    draw: (p, rand) => {
        let ticks = '';
        for (let degree = 0; degree < 360; degree += 10) {
            const long = degree % 45 === 0;
            const [x0, y0] = polar(500, 400, long ? 196 : 206, degree);
            const [x1, y1] = polar(500, 400, 222, degree);
            ticks += `<line x1="${round(x0)}" y1="${round(y0)}" x2="${round(x1)}" y2="${round(y1)}" stroke="#fde68a" stroke-width="${long ? 5 : 3}" opacity="0.8"/>`;
        }
        let rose = '';
        [[0, 210], [90, 210], [180, 210], [270, 210], [45, 130], [135, 130], [225, 130], [315, 130]].forEach(([angle, reach]) => {
            const tip = polar(500, 400, reach, angle - 90);
            const left = polar(500, 400, reach * 0.2, angle - 90 - 45);
            const right = polar(500, 400, reach * 0.2, angle - 90 + 45);
            rose += `<polygon points="${points([[500, 400], left, tip])}" fill="${reach > 150 ? '#fff3c4' : '#d9b56a'}"/>`;
            rose += `<polygon points="${points([[500, 400], tip, right])}" fill="${reach > 150 ? '#d99a1c' : '#8f6a24'}"/>`;
        });
        const letters = [['N', 0], ['E', 90], ['S', 180], ['W', 270]].map(([letter, angle]) => {
            const [x, y] = polar(500, 400, 262, angle - 90);
            return `<text x="${round(x)}" y="${round(y)}" text-anchor="middle" dominant-baseline="central" font-family="Inter Tight" font-weight="800" font-size="40" fill="#fde68a">${letter}</text>`;
        }).join('');
        return {
            defs: [
                linear('far', [[0, '#1f6f68'], [1, '#0b2f2c']]),
                linear('near', [[0, '#134e4a'], [1, '#04120f']]),
                radial('dial', [[0, '#21403c', 0.9], [1, '#0b1d1b', 0.95]]),
                blur('halo', 60),
            ].join(''),
            body: `
                <circle cx="500" cy="400" r="320" fill="#f59e0b" opacity="0.28" filter="url(#halo)"/>
                ${specks(rand, 60, [40, 40, 960, 560], '#fff8e6', [1, 2.4], [0.3, 0.9])}
                <polygon points="0,1000 0,720 120,600 200,660 330,520 450,650 560,560 690,680 800,580 1000,720 1000,1000" fill="url(#far)"/>
                <polygon points="330,520 300,560 330,552 360,566" fill="#e6f4f1" opacity="0.8"/>
                <polygon points="800,580 774,612 802,604 826,616" fill="#e6f4f1" opacity="0.8"/>
                <polygon points="0,1000 0,820 160,720 300,800 440,700 600,820 760,730 1000,860 1000,1000" fill="url(#near)"/>
                <path d="M150,960 C260,900 330,920 420,860 C500,810 560,840 640,780" stroke="#fde68a" stroke-width="6" stroke-dasharray="4 18" stroke-linecap="round" fill="none" opacity="0.8"/>
                <circle cx="500" cy="400" r="240" fill="url(#dial)" stroke="#d9b56a" stroke-width="10"/>
                <circle cx="500" cy="400" r="190" fill="none" stroke="#d9b56a" stroke-opacity="0.4" stroke-width="3"/>
                ${ticks}
                ${letters}
                ${rose}
                <circle cx="500" cy="400" r="20" fill="#fde68a" stroke="#8f6a24" stroke-width="5"/>`,
        };
    },
};

export const animation = {
    palette: { bg: '#0a0718', a: '#f472b6', b: '#38bdf8', c: '#fde047', ink: '#fff5fb' },
    draw: (p, rand) => {
        const floor = 820;
        const radius = 58;
        const rest = floor - radius;
        const path = `M90,${rest - 440} Q235,${rest - 440} 380,${rest} Q500,${rest - 600} 620,${rest} Q710,${rest - 380} 800,${rest} Q830,${rest - 200} 860,${rest - 150}`;
        const poses = [[90, rest - 440, 1, 1, 0.14], [380, floor - radius * 0.72, 1.3, 0.72, 0.24], [500, rest - 300, 1, 1, 0.32], [620, floor - radius * 0.72, 1.28, 0.72, 0.42], [710, rest - 190, 1, 1, 0.52], [800, floor - radius * 0.74, 1.24, 0.74, 0.62]];
        let ghosts = `<path d="${path}" stroke="#c4b5fd" stroke-width="5" stroke-dasharray="2 16" stroke-linecap="round" fill="none" opacity="0.7"/>`;
        poses.forEach(([x, y, stretchX, stretchY, opacity]) => {
            ghosts += `<ellipse cx="${x}" cy="${round(y)}" rx="${round(radius * stretchX)}" ry="${round(radius * stretchY)}" fill="url(#ball)" opacity="${opacity}"/>`;
            if (stretchY < 1) ghosts += `<ellipse cx="${x}" cy="${floor + 6}" rx="${round(radius * 1.2)}" ry="10" fill="#000" opacity="0.35"/>`;
        });
        const ballX = 860;
        const ballY = rest - 150;
        let candy = '';
        const colors = ['#f472b6', '#38bdf8', '#fde047', '#a78bfa'];
        for (let index = 0; index < 18; index++) {
            const x = between(rand, 80, 920);
            const y = between(rand, 80, 560);
            candy += index % 2
                ? `<polygon points="${starPoints(x, y, between(rand, 10, 18), between(rand, 5, 8))}" fill="${colors[index % 4]}" opacity="0.85"/>`
                : `<circle cx="${round(x)}" cy="${round(y)}" r="${round(between(rand, 5, 10))}" fill="${colors[index % 4]}" opacity="0.8"/>`;
        }
        return {
            defs: [
                radial('ball', [[0, '#ffd6ec'], [0.5, '#f472b6'], [1, '#9d174d']], { cx: 0.35, cy: 0.3, r: 0.8 }),
                radial('ball-main', [[0, '#fff0f8'], [0.45, '#f472b6'], [1, '#86103f']], { cx: 0.35, cy: 0.3, r: 0.8 }),
                `<clipPath id="ball-clip"><circle cx="${ballX}" cy="${ballY}" r="${radius}"/></clipPath>`,
                blur('halo', 50),
            ].join(''),
            body: `
                ${candy}
                <path d="M200,300 C260,250 320,350 380,300 C440,250 500,350 560,300" stroke="#38bdf8" stroke-width="10" fill="none" stroke-linecap="round" opacity="0.7"/>
                <line x1="40" y1="${floor}" x2="960" y2="${floor}" stroke="#c4b5fd" stroke-width="5" stroke-linecap="round" opacity="0.6"/>
                ${ghosts}
                <circle cx="${ballX}" cy="${ballY}" r="${radius + 30}" fill="#f472b6" opacity="0.5" filter="url(#halo)"/>
                <circle cx="${ballX}" cy="${ballY}" r="${radius}" fill="url(#ball-main)"/>
                <g clip-path="url(#ball-clip)">
                    <path d="M${ballX - 80},${ballY - 10} C${ballX - 30},${ballY + 30} ${ballX + 30},${ballY + 30} ${ballX + 80},${ballY - 10} L${ballX + 80},${ballY + 20} C${ballX + 30},${ballY + 60} ${ballX - 30},${ballY + 60} ${ballX - 80},${ballY + 20} Z" fill="#fde047" opacity="0.9"/>
                </g>
                <circle cx="${ballX - 20}" cy="${ballY - 22}" r="14" fill="#ffffff" opacity="0.75"/>
                <line x1="${ballX - 110}" y1="${ballY - 30}" x2="${ballX - 170}" y2="${ballY - 30}" stroke="#fff5fb" stroke-width="6" stroke-linecap="round" opacity="0.6"/>
                <line x1="${ballX - 100}" y1="${ballY}" x2="${ballX - 190}" y2="${ballY}" stroke="#fff5fb" stroke-width="6" stroke-linecap="round" opacity="0.5"/>
                <line x1="${ballX - 110}" y1="${ballY + 30}" x2="${ballX - 160}" y2="${ballY + 30}" stroke="#fff5fb" stroke-width="6" stroke-linecap="round" opacity="0.4"/>`,
        };
    },
};

const PETAL = 'M0,-24 L7,-32 C22,-28 28,-10 20,8 C13,24 4,32 0,36 C-4,32 -13,24 -20,8 C-28,-10 -22,-28 -7,-32 Z';

export const anime = {
    palette: { bg: '#0c0614', a: '#fb7185', b: '#7c3aed', c: '#fecdd3', ink: '#fff0f3' },
    draw: (p, rand) => {
        let petals = '';
        for (let index = 0; index < 24; index++) {
            petals += `<path d="${PETAL}" transform="translate(${round(between(rand, 60, 940))} ${round(between(rand, 60, 940))}) rotate(${round(between(rand, 0, 360))}) scale(${round(between(rand, 0.7, 1.6) * 100) / 100})" fill="url(#petal)" opacity="${round(between(rand, 0.6, 1))}"/>`;
        }
        return {
            defs: [
                linear('sun', [[0, '#ffc2cf'], [0.5, '#fb7185'], [1, '#be123c']]),
                linear('peak', [[0, '#3b1d6e'], [1, '#12082a']]),
                linear('petal', [[0, '#ffe4ec'], [1, '#f9a8c2']], [0, 0, 1, 1]),
                blur('halo', 60),
                glow('sparkle', 6),
            ].join(''),
            body: `
                ${rays(500, 500, 250, 640, 18, 10, '#fb7185', 0.16)}
                <circle cx="500" cy="500" r="300" fill="#fb7185" opacity="0.4" filter="url(#halo)"/>
                <circle cx="500" cy="500" r="240" fill="url(#sun)"/>
                <polygon points="80,900 380,560 440,600 500,540 560,600 620,560 920,900" fill="url(#peak)"/>
                <polygon points="380,560 440,600 500,540 560,600 620,560 580,520 500,470 420,520" fill="#f5f0ff"/>
                <polygon points="420,520 500,470 580,520 560,560 520,530 480,560 450,530" fill="#ffffff" opacity="0.6"/>
                ${petals}
                <g filter="url(#sparkle)">${sparkles(rand, 8, [80, 80, 920, 520], '#ffffff', [10, 22], [0.7, 1])}</g>`,
        };
    },
};

export const biography = {
    palette: { bg: '#0d0a07', a: '#d6a35c', b: '#7c4a1e', c: '#f3d9a8', ink: '#fbf1e2' },
    draw: (p, rand) => {
        let beads = '';
        for (let index = 0; index < 48; index++) {
            const angle = (index * 360) / 48;
            const x = 500 + Math.cos((angle * Math.PI) / 180) * 228;
            const y = 450 + Math.sin((angle * Math.PI) / 180) * 298;
            beads += `<circle cx="${round(x)}" cy="${round(y)}" r="6" fill="#fff0c2" opacity="0.8"/>`;
        }
        return {
            defs: [
                radial('portrait', [[0, '#f6e2bd'], [0.6, '#c99a5e'], [1, '#6e4520']], { cx: 0.5, cy: 0.35, r: 0.75 }),
                linear('gold', [[0, '#fff0c2'], [0.5, '#d6a35c'], [1, '#7a4f1e']], [0, 0, 1, 1]),
                `<clipPath id="oval"><ellipse cx="500" cy="450" rx="236" ry="306"/></clipPath>`,
                blur('halo', 60),
                floorBlur,
            ].join(''),
            body: `
                <ellipse cx="500" cy="460" rx="330" ry="400" fill="#d6a35c" opacity="0.25" filter="url(#halo)"/>
                ${floorShadow(500, 880, 240, 24)}
                <g clip-path="url(#oval)">
                    <ellipse cx="500" cy="450" rx="236" ry="306" fill="url(#portrait)"/>
                    <ellipse cx="500" cy="400" rx="92" ry="112" fill="#2a1a10"/>
                    <rect x="462" y="490" width="76" height="80" fill="#2a1a10"/>
                    <path d="M290,800 C300,650 390,590 500,584 C610,590 700,650 710,800 Z" fill="#2a1a10"/>
                </g>
                <ellipse cx="500" cy="450" rx="250" ry="320" fill="none" stroke="url(#gold)" stroke-width="34"/>
                ${beads}
                <rect x="390" y="812" width="220" height="62" rx="10" fill="url(#gold)"/>
                <rect x="402" y="824" width="196" height="38" rx="6" fill="#4a3018" opacity="0.55"/>
                <rect x="430" y="834" width="140" height="8" rx="4" fill="#f3d9a8" opacity="0.75"/>
                <rect x="458" y="848" width="84" height="6" rx="3" fill="#f3d9a8" opacity="0.5"/>
                ${sparkles(rand, 6, [100, 100, 900, 800], '#fbf1e2', [5, 10], [0.4, 0.8])}`,
        };
    },
};

function bubble(x, y, rx, ry, rotate, fill, text, ink, size, tail) {
    const [tx, ty] = tail;
    return `<g transform="rotate(${rotate} ${x} ${y})">
        <polygon points="${points([[x - rx * 0.25, y + ry * 0.7], [x + tx, y + ty], [x + rx * 0.1, y + ry * 0.8]])}" fill="${fill}"/>
        <ellipse cx="${x}" cy="${y}" rx="${rx}" ry="${ry}" fill="${fill}"/>
        <ellipse cx="${x}" cy="${y}" rx="${rx}" ry="${ry}" fill="url(#bubble-shade)"/>
        <text x="${x}" y="${y + size * 0.06}" text-anchor="middle" dominant-baseline="central" font-family="Inter Tight" font-weight="900" font-size="${size}" letter-spacing="-4" fill="${ink}">${text}</text>
    </g>`;
}

export const comedy = {
    palette: { bg: '#0f0a02', a: '#facc15', b: '#f97316', c: '#fef08a', ink: '#fffbea' },
    draw: (p, rand) => ({
        defs: [
            radial('bubble-shade', [[0, '#ffffff', 0.35], [0.6, '#ffffff', 0], [1, '#000', 0.18]], { cx: 0.35, cy: 0.3, r: 0.8 }),
            blur('halo', 60),
        ].join(''),
        body: `
            ${rays(480, 500, 280, 470, 22, 4, '#fde047', 0.35)}
            <ellipse cx="480" cy="500" rx="320" ry="260" fill="#facc15" opacity="0.3" filter="url(#halo)"/>
            ${bubble(740, 260, 150, 108, 12, '#fb923c', 'HA', '#3b1a02', 120, [-150, 140])}
            ${bubble(270, 760, 150, 104, -8, '#fffbea', 'HA', '#1c1404', 110, [120, -160])}
            ${bubble(480, 500, 260, 186, -6, '#facc15', 'HA!', '#1c1404', 220, [-120, 220])}
            ${specks(rand, 30, [80, 80, 920, 920], '#fde047', [3, 7], [0.4, 0.9])}
            ${sparkles(rand, 8, [80, 80, 920, 920], '#fffbea', [8, 15], [0.5, 1])}`,
    }),
};

function tape(y, rotate) {
    const words = 'DO NOT CROSS  ·  DO NOT CROSS  ·  DO NOT CROSS  ·  DO NOT CROSS';
    return `<g transform="rotate(${rotate} 500 ${y})">
        <rect x="-200" y="${y - 34}" width="1400" height="68" fill="#000" opacity="0.4" transform="translate(0 10)"/>
        <rect x="-200" y="${y - 34}" width="1400" height="68" fill="url(#tape)"/>
        <text x="500" y="${y + 2}" text-anchor="middle" dominant-baseline="central" font-family="Inter Tight" font-weight="900" font-size="36" letter-spacing="4" fill="#111">${words}</text>
    </g>`;
}

export const crime = {
    palette: { bg: '#05070a', a: '#38bdf8', b: '#1e40af', c: '#facc15', ink: '#f5f5f0' },
    draw: (p, rand) => {
        let ridges = '';
        for (let ring = 0; ring < 12; ring++) {
            const rx = 30 + ring * 22;
            const ry = 44 + ring * 26;
            const gap = round(between(rand, 18, 40));
            const dash = round(between(rand, 90, 220));
            ridges += `<ellipse cx="500" cy="${430 + ring * 3}" rx="${rx}" ry="${ry}" fill="none" stroke="#7dd3fc" stroke-width="8" stroke-linecap="round" stroke-dasharray="${dash} ${gap} ${round(dash * 0.6)} ${gap}" stroke-dashoffset="${round(between(rand, 0, 200))}" transform="rotate(${round(between(rand, -8, 8))} 500 430)"/>`;
        }
        return {
            defs: [
                radialAt('print-fade', [[0, '#fff'], [0.7, '#fff'], [1, '#fff', 0]], { cx: 500, cy: 450, r: 330 }),
                `<mask id="print-mask" maskContentUnits="userSpaceOnUse"><rect x="0" y="0" width="1000" height="1000" fill="url(#print-fade)"/></mask>`,
                linear('tape', [[0, '#fde047'], [1, '#eab308']]),
                glow('print-glow', 8),
                blur('halo', 60),
            ].join(''),
            body: `
                <ellipse cx="500" cy="450" rx="320" ry="380" fill="#38bdf8" opacity="0.22" filter="url(#halo)"/>
                <g mask="url(#print-mask)" filter="url(#print-glow)" opacity="0.9">${ridges}</g>
                ${tape(690, -12)}
                ${tape(830, 8)}`,
        };
    },
};

export const documentary = {
    palette: { bg: '#050608', a: '#94a3b8', b: '#0ea5e9', c: '#e2e8f0', ink: '#f1f5f9' },
    draw: (p, rand) => {
        let bokeh = '';
        for (let index = 0; index < 12; index++) {
            bokeh += `<circle cx="${round(between(rand, 40, 960))}" cy="${round(between(rand, 40, 960))}" r="${round(between(rand, 30, 80))}" fill="${index % 2 ? '#38bdf8' : '#a78bfa'}" opacity="${round(between(rand, 0.1, 0.25))}" filter="url(#bokeh)"/>`;
        }
        const marks = ['∞', '10', '5', '3', '2', '1.5', '1'].map((label, index) => {
            const [x, y] = polar(500, 470, 252, -150 + index * 20);
            return `<text x="${round(x)}" y="${round(y)}" text-anchor="middle" dominant-baseline="central" font-family="Inter Tight" font-weight="700" font-size="24" fill="#e2e8f0" opacity="0.8">${label}</text>`;
        }).join('');
        let blades = '';
        for (let index = 0; index < 6; index++) {
            const a = polar(500, 470, 150, index * 60);
            const b = polar(500, 470, 150, index * 60 + 60);
            const inner = polar(500, 470, 58, index * 60 + 40);
            blades += `<polygon points="${points([a, b, inner])}" fill="#141821" stroke="#3a4352" stroke-width="2"/>`;
        }
        return {
            defs: [
                radial('barrel', [[0, '#3a404c'], [0.8, '#1a1d24'], [1, '#0a0b0e']]),
                radial('glass', [[0, '#1f2a5a'], [0.6, '#0a0f24'], [1, '#05070f']], { cx: 0.42, cy: 0.38, r: 0.7 }),
                blur('bokeh', 16),
                blur('halo', 60),
                floorBlur,
            ].join(''),
            body: `
                ${bokeh}
                <circle cx="500" cy="470" r="340" fill="#0ea5e9" opacity="0.2" filter="url(#halo)"/>
                ${floorShadow(500, 860, 260, 24)}
                <circle cx="500" cy="470" r="310" fill="url(#barrel)"/>
                <circle cx="500" cy="470" r="300" fill="none" stroke="#5b6476" stroke-width="4"/>
                <circle cx="500" cy="470" r="282" fill="none" stroke="#2a2f3a" stroke-width="26" stroke-dasharray="4 7"/>
                ${marks}
                <circle cx="500" cy="470" r="226" fill="#0b0d12" stroke="#4a5263" stroke-width="5"/>
                <circle cx="500" cy="470" r="200" fill="url(#glass)"/>
                ${blades}
                <circle cx="500" cy="470" r="60" fill="#04050a"/>
                <path d="M360,380 A170,170 0 0 1 520,300" stroke="#a78bfa" stroke-width="16" fill="none" opacity="0.55" stroke-linecap="round"/>
                <path d="M610,560 A150,150 0 0 1 520,620" stroke="#34d399" stroke-width="10" fill="none" opacity="0.45" stroke-linecap="round"/>
                <ellipse cx="430" cy="380" rx="60" ry="30" fill="#ffffff" opacity="0.18" transform="rotate(-35 430 380)"/>
                <circle cx="560" cy="420" r="10" fill="#ffffff" opacity="0.5"/>`,
        };
    },
};

export const drama = {
    palette: { bg: '#0f0406', a: '#e11d48', b: '#7f1d1d', c: '#fecdd3', ink: '#fff0f2' },
    draw: (p, rand) => {
        let scallops = '';
        for (let x = 0; x <= 1000; x += 100) scallops += `<circle cx="${x + 50}" cy="96" r="52" fill="url(#fold)"/>`;
        return {
            defs: [
                `<linearGradient id="fold" gradientUnits="userSpaceOnUse" x1="0" y1="0" x2="70" y2="0" spreadMethod="repeat"><stop offset="0" stop-color="#4a0711"/><stop offset="0.5" stop-color="#c81e3a"/><stop offset="1" stop-color="#4a0711"/></linearGradient>`,
                linear('beam', [[0, '#fff3e6', 0.38], [1, '#fff3e6', 0.06]]),
                linear('floor', [[0, '#2a1410'], [1, '#0c0605']]),
                linear('curtain-shade', [[0, '#000', 0], [1, '#000', 0.5]]),
                blur('pool-blur', 30),
            ].join(''),
            body: `
                <rect x="0" y="820" width="1000" height="180" fill="url(#floor)"/>
                <line x1="0" y1="822" x2="1000" y2="822" stroke="#6b2a1a" stroke-width="4"/>
                <polygon points="430,0 570,0 760,850 240,850" fill="url(#beam)"/>
                <ellipse cx="500" cy="850" rx="270" ry="54" fill="#fff3e6" opacity="0.55" filter="url(#pool-blur)"/>
                ${specks(rand, 50, [300, 60, 700, 820], '#fff3e6', [1, 2.6], [0.25, 0.8])}
                <path d="M0,0 L330,0 C300,280 250,480 230,560 C200,700 214,860 244,1000 L0,1000 Z" fill="url(#fold)"/>
                <path d="M0,0 L330,0 C300,280 250,480 230,560 C200,700 214,860 244,1000 L0,1000 Z" fill="url(#curtain-shade)"/>
                <path d="M1000,0 L670,0 C700,280 750,480 770,560 C800,700 786,860 756,1000 L1000,1000 Z" fill="url(#fold)"/>
                <path d="M1000,0 L670,0 C700,280 750,480 770,560 C800,700 786,860 756,1000 L1000,1000 Z" fill="url(#curtain-shade)"/>
                <rect x="0" y="0" width="1000" height="100" fill="url(#fold)"/>
                ${scallops}
                <rect x="0" y="0" width="1000" height="40" fill="#e8b84a" opacity="0.8"/>
                <path d="M218,560 C240,540 262,548 270,566" stroke="#e8b84a" stroke-width="10" fill="none" stroke-linecap="round"/>
                <path d="M782,560 C760,540 738,548 730,566" stroke="#e8b84a" stroke-width="10" fill="none" stroke-linecap="round"/>`,
        };
    },
};

function person(x, height, fill) {
    const head = height * 0.13;
    const width = height * 0.38;
    const bodyTop = 860 - height * 0.68;
    return `<rect x="${round(x - width / 2)}" y="${round(bodyTop)}" width="${round(width)}" height="${round(height * 0.68)}" rx="${round(width / 2)}" fill="${fill}"/>
        <circle cx="${x}" cy="${round(bodyTop - head - 10)}" r="${round(head)}" fill="${fill}"/>`;
}

export const family = {
    palette: { bg: '#0c0905', a: '#fb923c', b: '#f43f5e', c: '#fed7aa', ink: '#fff4ea' },
    draw: (p, rand) => ({
        defs: [
            linear('one', [[0, '#fdba74'], [1, '#ea580c']]),
            linear('two', [[0, '#fda4af'], [1, '#e11d48']]),
            linear('three', [[0, '#fde68a'], [1, '#eab308']]),
            linear('four', [[0, '#7dd3fc'], [1, '#0284c7']]),
            linear('heart', [[0, '#ff9dbd'], [1, '#e11d48']], [0, 0, 1, 1]),
            glow('heart-glow', 12),
            blur('halo', 60),
            floorBlur,
        ].join(''),
        body: `
            <ellipse cx="500" cy="560" rx="360" ry="320" fill="#fb923c" opacity="0.25" filter="url(#halo)"/>
            <polyline points="190,500 500,240 810,500" fill="none" stroke="#fed7aa" stroke-width="16" stroke-linecap="round" stroke-linejoin="round" opacity="0.75"/>
            <line x1="250" y1="455" x2="250" y2="860" stroke="#fed7aa" stroke-width="12" stroke-linecap="round" opacity="0.5"/>
            <line x1="750" y1="455" x2="750" y2="860" stroke="#fed7aa" stroke-width="12" stroke-linecap="round" opacity="0.5"/>
            <rect x="660" y="300" width="44" height="110" rx="6" fill="none" stroke="#fed7aa" stroke-width="12" opacity="0.5"/>
            ${heart(500, 390, 110, 'url(#heart)', 0, 'filter="url(#heart-glow)"')}
            ${floorShadow(500, 870, 270, 20)}
            ${person(355, 380, 'url(#one)')}
            ${person(470, 345, 'url(#two)')}
            ${person(578, 250, 'url(#three)')}
            ${person(660, 190, 'url(#four)')}
            ${sparkles(rand, 8, [100, 100, 900, 700], '#fff4ea', [6, 12], [0.4, 0.9])}`,
    }),
};

export const fantasy = {
    palette: { bg: '#070617', a: '#a78bfa', b: '#4338ca', c: '#c4b5fd', ink: '#f3efff' },
    draw: (p, rand) => {
        const tower = (x0, x1, top, roofTop) => {
            let merlons = '';
            for (let x = x0; x < x1; x += 24) merlons += `<rect x="${x}" y="${top - 18}" width="14" height="20" fill="url(#stone)"/>`;
            return `<rect x="${x0}" y="${top}" width="${x1 - x0}" height="${780 - top}" fill="url(#stone)"/>
                <polygon points="${x0 - 14},${top} ${x1 + 14},${top} ${(x0 + x1) / 2},${roofTop}" fill="url(#roof)"/>
                <polygon points="${(x0 + x1) / 2},${roofTop} ${(x0 + x1) / 2 + 40},${roofTop + 14} ${(x0 + x1) / 2},${roofTop + 26}" fill="#f472b6"/>
                <line x1="${(x0 + x1) / 2}" y1="${roofTop}" x2="${(x0 + x1) / 2}" y2="${roofTop + 30}" stroke="#c4b5fd" stroke-width="3"/>`;
        };
        const window = (x, y, w, h) => `<rect x="${x}" y="${y}" width="${w}" height="${h}" rx="${w / 2}" fill="#ffd36b" filter="url(#window-glow)"/>`;
        let trail = '';
        for (let index = 0; index < 18; index++) {
            const t = index / 17;
            const angle = -200 + t * 330;
            const radius = 360 - t * 120;
            const [x, y] = polar(500, 560, radius, angle);
            trail += `<path d="M${round(x)},${round(y - 12 + t * 4)} q2,10 12,12 q-10,2 -12,12 q-2,-10 -12,-12 q10,-2 12,-12 Z" fill="#f3efff" opacity="${round(0.3 + t * 0.7)}"/>`;
        }
        let merlonsWall = '';
        for (let x = 300; x < 700; x += 26) merlonsWall += `<rect x="${x}" y="622" width="15" height="20" fill="url(#stone)"/>`;
        return {
            defs: [
                linear('stone', [[0, '#5b4b9a'], [1, '#231a4d']]),
                linear('roof', [[0, '#8b5cf6'], [1, '#3b1d8f']], [0, 0, 1, 1]),
                linear('hill', [[0, '#2a1f5c'], [1, '#0b0820']]),
                radialAt('moon', [[0, '#fffbe8'], [1, '#e7defd']], { cx: 720, cy: 230, r: 120 }),
                `<mask id="crescent"><rect width="1000" height="1000" fill="#fff"/><circle cx="770" cy="200" r="110" fill="#000"/></mask>`,
                glow('window-glow', 6),
                glow('magic', 8),
                blur('halo', 50),
            ].join(''),
            body: `
                ${specks(rand, 110, [20, 20, 980, 640], '#ffffff', [0.8, 2.4], [0.3, 0.95])}
                <circle cx="720" cy="230" r="170" fill="#c4b5fd" opacity="0.35" filter="url(#halo)"/>
                <circle cx="720" cy="230" r="120" fill="url(#moon)" mask="url(#crescent)"/>
                <path d="M60,1000 C140,860 260,800 330,780 L670,780 C740,800 860,860 940,1000 Z" fill="url(#hill)"/>
                <rect x="300" y="640" width="400" height="140" fill="url(#stone)"/>
                ${merlonsWall}
                ${tower(300, 380, 520, 390)}
                ${tower(620, 700, 500, 360)}
                ${tower(430, 570, 420, 250)}
                ${window(486, 470, 28, 54)}
                ${window(330, 580, 20, 40)}
                ${window(650, 560, 20, 40)}
                ${window(470, 600, 18, 34)}
                ${window(512, 600, 18, 34)}
                <path d="M470,780 L470,720 A30,30 0 0 1 530,720 L530,780 Z" fill="#120c2c"/>
                <g filter="url(#magic)">${trail}</g>
                ${sparkles(rand, 10, [80, 80, 920, 700], '#e9e2ff', [6, 13], [0.45, 0.95])}`,
        };
    },
};

export const history = {
    palette: { bg: '#0d0a05', a: '#d4a24c', b: '#78350f', c: '#f5deb3', ink: '#fbf3e4' },
    draw: (p, rand) => {
        const numerals = ['XII', 'I', 'II', 'III', 'IV', 'V', 'VI', 'VII', 'VIII', 'IX', 'X', 'XI'].map((label, index) => {
            const [x, y] = polar(500, 480, 318, -90 + index * 30);
            return `<text x="${round(x)}" y="${round(y)}" text-anchor="middle" dominant-baseline="central" font-family="Playfair Display" font-style="italic" font-weight="800" font-size="40" fill="#f5deb3" opacity="0.6">${label}</text>`;
        }).join('');
        let ticks = '';
        for (let index = 0; index < 60; index++) {
            const [x0, y0] = polar(500, 480, index % 5 === 0 ? 354 : 362, index * 6);
            const [x1, y1] = polar(500, 480, 372, index * 6);
            ticks += `<line x1="${round(x0)}" y1="${round(y0)}" x2="${round(x1)}" y2="${round(y1)}" stroke="#d4a24c" stroke-width="${index % 5 === 0 ? 4 : 2}" opacity="0.55"/>`;
        }
        return {
            defs: [
                linear('wood', [[0, '#a8743c'], [0.5, '#7a4a1e'], [1, '#4a2a0e']]),
                linear('glass', [[0, '#ffffff', 0.2], [0.5, '#ffffff', 0.05], [1, '#ffffff', 0.16]], [0, 0, 1, 0]),
                linear('sand', [[0, '#f7dca0'], [1, '#d4a24c']]),
                blur('halo', 60),
                floorBlur,
            ].join(''),
            body: `
                <circle cx="500" cy="480" r="372" fill="none" stroke="#d4a24c" stroke-width="3" opacity="0.4"/>
                ${ticks}
                ${numerals}
                <ellipse cx="500" cy="480" rx="230" ry="300" fill="#d4a24c" opacity="0.3" filter="url(#halo)"/>
                ${floorShadow(500, 830, 210, 20)}
                <path d="M400,396 C420,440 474,474 492,500 L508,500 C526,474 580,440 600,396 Z" fill="url(#sand)"/>
                <rect x="496" y="498" width="8" height="190" fill="url(#sand)"/>
                <path d="M398,744 C418,680 468,650 500,640 C532,650 582,680 602,744 Z" fill="url(#sand)"/>
                <path d="M388,256 C388,410 472,452 490,500 C472,548 388,590 388,744 L612,744 C612,590 528,548 510,500 C528,452 612,410 612,256 Z" fill="url(#glass)" stroke="#ffffff" stroke-opacity="0.45" stroke-width="3"/>
                <path d="M412,280 C412,380 450,430 470,460" stroke="#ffffff" stroke-opacity="0.5" stroke-width="7" fill="none" stroke-linecap="round"/>
                <rect x="352" y="254" width="22" height="494" rx="8" fill="url(#wood)"/>
                <rect x="626" y="254" width="22" height="494" rx="8" fill="url(#wood)"/>
                <rect x="330" y="214" width="340" height="44" rx="12" fill="url(#wood)"/>
                <rect x="330" y="744" width="340" height="44" rx="12" fill="url(#wood)"/>
                <rect x="346" y="204" width="308" height="14" rx="7" fill="#c8945a"/>
                <rect x="346" y="784" width="308" height="14" rx="7" fill="#4a2a0e"/>`,
        };
    },
};

export const horror = {
    palette: { bg: '#050305', a: '#b91c1c', b: '#450a0a', c: '#fca5a5', ink: '#f5e6e6' },
    draw: (p, rand) => ({
        defs: [
            linear('wall', [[0, '#1a0c0e'], [1, '#080405']]),
            linear('light', [[0, '#ffb3a6'], [0.4, '#ef4444'], [1, '#5a0b0b']]),
            linear('spill', [[0, '#ef4444', 0.55], [1, '#ef4444', 0]]),
            linear('door', [[0, '#2a1416'], [1, '#120809']], [0, 0, 1, 0]),
            blur('halo', 40),
            blur('soft', 3),
        ].join(''),
        body: `
            <rect x="80" y="60" width="840" height="880" fill="url(#wall)" opacity="0.6"/>
            <rect x="330" y="210" width="340" height="640" fill="#ef4444" opacity="0.45" filter="url(#halo)"/>
            <rect x="340" y="220" width="320" height="630" fill="url(#light)"/>
            <path d="M604,850 L628,850 L640,330 C640,300 600,300 600,330 Z" fill="#0c0405" filter="url(#soft)"/>
            <ellipse cx="620" cy="300" rx="22" ry="28" fill="#0c0405" filter="url(#soft)"/>
            <path d="M590,360 L650,360 L656,560 L584,560 Z" fill="#0c0405" filter="url(#soft)"/>
            <polygon points="340,220 560,256 560,816 340,850" fill="url(#door)"/>
            <polygon points="370,280 530,302 530,500 370,490" fill="none" stroke="#3a1c1e" stroke-width="6"/>
            <polygon points="370,560 530,560 530,770 370,800" fill="none" stroke="#3a1c1e" stroke-width="6"/>
            <circle cx="534" cy="540" r="10" fill="#a8786a"/>
            <rect x="320" y="200" width="360" height="660" fill="none" stroke="#2a1416" stroke-width="22"/>
            <polygon points="560,850 680,850 980,1000 420,1000" fill="url(#spill)"/>
            <path d="M604,852 L630,852 C700,900 760,950 820,1000 L770,1000 C720,950 660,900 604,852 Z" fill="#050203" opacity="0.85"/>
            <path d="M100,200 L180,230 M120,240 L190,262 M110,280 L176,300" stroke="#5a0b0b" stroke-width="5" opacity="0.6" stroke-linecap="round"/>
            ${specks(rand, 30, [120, 120, 880, 880], '#fca5a5', [1, 2.5], [0.2, 0.6])}`,
    }),
};

function cube(cx, cy, size, [top, left, right], letter, ink) {
    const w = size * 0.866;
    const h = size / 2;
    return `<polygon points="${points([[cx, cy - h], [cx + w, cy], [cx, cy + h], [cx - w, cy]])}" fill="${top}"/>
        <polygon points="${points([[cx - w, cy], [cx, cy + h], [cx, cy + h + size], [cx - w, cy + size]])}" fill="${left}"/>
        <polygon points="${points([[cx, cy + h], [cx + w, cy], [cx + w, cy + size], [cx, cy + h + size]])}" fill="${right}"/>
        <text transform="matrix(0.866 0.5 0 1 ${round(cx - w / 2)} ${round(cy + h / 2 + size / 2)})" text-anchor="middle" dominant-baseline="central" font-family="Inter Tight" font-weight="900" font-size="${round(size * 0.62)}" fill="${ink}">${letter}</text>
        <text transform="matrix(0.866 -0.5 0 1 ${round(cx + w / 2)} ${round(cy + h / 2 + size / 2)})" text-anchor="middle" dominant-baseline="central" font-family="Inter Tight" font-weight="900" font-size="${round(size * 0.62)}" fill="${ink}" opacity="0.85">${letter}</text>`;
}

export const kids = {
    palette: { bg: '#07101c', a: '#38bdf8', b: '#f472b6', c: '#fde047', ink: '#f0f9ff' },
    draw: (p, rand) => {
        const colors = ['#f472b6', '#38bdf8', '#fde047', '#a78bfa', '#4ade80'];
        let stars = '';
        for (let index = 0; index < 14; index++) {
            stars += `<polygon points="${starPoints(between(rand, 80, 920), between(rand, 80, 480), between(rand, 10, 20), between(rand, 5, 9))}" fill="${colors[index % 5]}" opacity="0.85"/>`;
        }
        return {
            defs: [
                blur('halo', 60),
                floorBlur,
            ].join(''),
            body: `
                ${stars}
                <ellipse cx="500" cy="600" rx="340" ry="280" fill="#38bdf8" opacity="0.25" filter="url(#halo)"/>
                <path d="M120,420 C220,300 320,480 420,360 C470,300 520,280 580,250" stroke="#f0f9ff" stroke-width="5" stroke-dasharray="14 14" stroke-linecap="round" fill="none" opacity="0.7"/>
                <g transform="translate(640 230) rotate(-18)">
                    <polygon points="0,0 160,40 40,60" fill="#ffffff"/>
                    <polygon points="40,60 160,40 70,100" fill="#dbeafe"/>
                    <polygon points="40,60 70,100 54,64" fill="#93c5fd"/>
                </g>
                ${floorShadow(500, 880, 320, 26)}
                ${cube(395, 600, 170, ['#bae6fd', '#38bdf8', '#0284c7'], 'B', '#f0f9ff')}
                ${cube(395 + 170 * 0.866, 600 + 85, 170, ['#fbcfe8', '#f472b6', '#db2777'], 'C', '#fff0f8')}
                ${cube(395, 600 - 170, 170, ['#fef08a', '#facc15', '#ca8a04'], 'A', '#422006')}`,
        };
    },
};

export const martialArts = {
    palette: { bg: '#0d0605', a: '#dc2626', b: '#7f1d1d', c: '#fecaca', ink: '#fff1ef' },
    draw: (p, rand) => {
        let splatter = '';
        for (let index = 0; index < 14; index++) {
            const [x, y] = polar(500, 470, between(rand, 300, 360), between(rand, -80, -20));
            splatter += `<circle cx="${round(x)}" cy="${round(y)}" r="${round(between(rand, 3, 10))}" fill="#f5ead6" opacity="${round(between(rand, 0.5, 0.9))}"/>`;
        }
        const bamboo = (x, top, bottom, width) => {
            let nodes = '';
            for (let y = top + 80; y < bottom; y += 120) nodes += `<rect x="${x - width / 2 - 3}" y="${y}" width="${width + 6}" height="8" rx="4" fill="#0a1f12"/>`;
            return `<rect x="${x - width / 2}" y="${top}" width="${width}" height="${bottom - top}" rx="${width / 2}" fill="#14532d" opacity="0.85"/>${nodes}`;
        };
        return {
            defs: [
                radial('sun', [[0, '#ff6b5c'], [0.7, '#dc2626'], [1, '#8f1111']], { cx: 0.4, cy: 0.35, r: 0.8 }),
                `<filter id="brush" x="-20%" y="-20%" width="140%" height="140%"><feTurbulence type="fractalNoise" baseFrequency="0.035" numOctaves="3" seed="7"/><feDisplacementMap in="SourceGraphic" scale="22"/></filter>`,
                `<filter id="dry" x="-20%" y="-20%" width="140%" height="140%"><feTurbulence type="fractalNoise" baseFrequency="0.6" numOctaves="2" seed="3"/><feDisplacementMap in="SourceGraphic" scale="9"/></filter>`,
                blur('halo', 60),
            ].join(''),
            body: `
                ${bamboo(110, 80, 980, 30)}
                ${bamboo(170, 200, 980, 22)}
                <path d="M140,300 C100,260 60,250 30,262 C70,280 100,296 140,300 Z" fill="#166534" opacity="0.8"/>
                <path d="M190,420 C240,380 290,376 320,390 C280,404 240,416 190,420 Z" fill="#166534" opacity="0.8"/>
                <circle cx="520" cy="490" r="170" fill="#dc2626" opacity="0.4" filter="url(#halo)"/>
                <circle cx="520" cy="490" r="120" fill="url(#sun)"/>
                <path d="M600,210 A280,280 0 1 1 420,226" stroke="#f5ead6" stroke-width="64" fill="none" stroke-linecap="round" filter="url(#brush)"/>
                <path d="M612,226 A262,262 0 1 1 430,244" stroke="#f5ead6" stroke-width="10" fill="none" stroke-linecap="round" opacity="0.55" filter="url(#dry)"/>
                ${splatter}`,
        };
    },
};

const NOTE = 'M0,0 C-16,0 -30,10 -30,22 C-30,34 -18,40 -4,36 C10,32 18,22 18,12 L18,-120 C40,-110 60,-96 62,-70 C76,-100 56,-130 14,-150 L8,-150 L8,4 C6,2 3,0 0,0 Z';

export const music = {
    palette: { bg: '#06040f', a: '#8b5cf6', b: '#ec4899', c: '#c4b5fd', ink: '#f5f0ff' },
    draw: (p, rand) => {
        let grooves = '';
        for (let radius = 120; radius <= 292; radius += 6) {
            grooves += `<circle cx="450" cy="520" r="${radius}" fill="none" stroke="#ffffff" stroke-width="1.5" opacity="${round(between(rand, 0.03, 0.09))}"/>`;
        }
        const note = (x, y, scale, rotate, fill) => `<path d="${NOTE}" transform="translate(${x} ${y}) rotate(${rotate}) scale(${scale})" fill="${fill}" filter="url(#note-glow)"/>`;
        return {
            defs: [
                radial('disc', [[0, '#1a1726'], [1, '#07060c']]),
                linear('label', [[0, '#f472b6'], [1, '#7c3aed']], [0, 0, 1, 1]),
                linear('sheen', [[0, '#ffffff', 0], [0.5, '#ffffff', 0.16], [1, '#ffffff', 0]], [0, 0, 1, 1]),
                `<clipPath id="disc-clip"><circle cx="450" cy="520" r="300"/></clipPath>`,
                glow('note-glow', 8),
                blur('halo', 60),
                floorBlur,
            ].join(''),
            body: `
                <circle cx="500" cy="500" r="360" fill="#8b5cf6" opacity="0.3" filter="url(#halo)"/>
                ${floorShadow(450, 880, 260, 24)}
                <circle cx="450" cy="520" r="300" fill="url(#disc)"/>
                ${grooves}
                <g clip-path="url(#disc-clip)">
                    <polygon points="450,520 180,180 330,140" fill="url(#sheen)"/>
                    <polygon points="450,520 720,860 570,900" fill="url(#sheen)"/>
                </g>
                <circle cx="450" cy="520" r="300" fill="none" stroke="#2a2638" stroke-width="4"/>
                <circle cx="450" cy="520" r="104" fill="url(#label)"/>
                <circle cx="450" cy="520" r="80" fill="none" stroke="#ffffff" stroke-opacity="0.25" stroke-width="2"/>
                <circle cx="450" cy="520" r="12" fill="#06040f"/>
                ${note(760, 330, 1.3, 8, '#f0abfc')}
                ${note(850, 520, 0.9, -10, '#c4b5fd')}
                ${note(700, 180, 0.8, 14, '#f9a8d4')}
                <path d="M800,620 C840,660 840,720 800,760" stroke="#c4b5fd" stroke-width="8" fill="none" stroke-linecap="round" opacity="0.6"/>
                <path d="M840,590 C900,650 900,730 840,790" stroke="#c4b5fd" stroke-width="8" fill="none" stroke-linecap="round" opacity="0.4"/>
                ${sparkles(rand, 8, [80, 80, 920, 900], '#f5f0ff', [5, 11], [0.4, 0.9])}`,
        };
    },
};

export const mystery = {
    palette: { bg: '#05080d', a: '#38bdf8', b: '#1e293b', c: '#bae6fd', ink: '#eef6ff' },
    draw: (p, rand) => {
        let marks = '';
        for (let index = 0; index < 7; index++) {
            marks += `<text x="${round(between(rand, 80, 920))}" y="${round(between(rand, 120, 920))}" text-anchor="middle" font-family="Inter Tight" font-weight="900" font-size="${round(between(rand, 90, 200))}" fill="#bae6fd" opacity="${round(between(rand, 0.05, 0.1))}" transform="rotate(${round(between(rand, -20, 20))})">?</text>`;
        }
        return {
            defs: [
                linear('metal', [[0, '#e2e8f0'], [0.5, '#7b8aa3'], [1, '#2b3546']], [0, 0, 1, 1]),
                linear('handle', [[0, '#5a3a22'], [1, '#2a1a0e']], [0, 0, 1, 0]),
                radial('lens', [[0, '#bae6fd', 0.25], [1, '#0c2a44', 0.6]], { cx: 0.4, cy: 0.35, r: 0.75 }),
                `<clipPath id="lens-clip"><circle cx="440" cy="420" r="200"/></clipPath>`,
                glow('key-glow', 14),
                blur('halo', 60),
                floorBlur,
            ].join(''),
            body: `
                ${marks}
                <circle cx="440" cy="420" r="300" fill="#38bdf8" opacity="0.22" filter="url(#halo)"/>
                ${floorShadow(560, 880, 260, 22)}
                <g transform="rotate(45 440 420)">
                    <rect x="410" y="640" width="60" height="70" rx="10" fill="url(#metal)"/>
                    <rect x="404" y="700" width="72" height="250" rx="34" fill="url(#handle)"/>
                </g>
                <circle cx="440" cy="420" r="200" fill="#07111d"/>
                <g clip-path="url(#lens-clip)">
                    <g filter="url(#key-glow)" fill="#bae6fd">
                        <circle cx="440" cy="380" r="54"/>
                        <polygon points="410,410 470,410 500,540 380,540"/>
                    </g>
                    <circle cx="440" cy="420" r="200" fill="url(#lens)"/>
                    <path d="M300,330 A160,160 0 0 1 420,250" stroke="#ffffff" stroke-width="14" fill="none" opacity="0.4" stroke-linecap="round"/>
                </g>
                <circle cx="440" cy="420" r="214" fill="none" stroke="url(#metal)" stroke-width="34"/>
                ${sparkles(rand, 6, [100, 100, 900, 900], '#eef6ff', [5, 11], [0.4, 0.85])}`,
        };
    },
};

export const reality = {
    palette: { bg: '#08050c', a: '#ef4444', b: '#a855f7', c: '#fecaca', ink: '#fff1f2' },
    draw: (p, rand) => {
        let lights = '';
        const colors = ['#f472b6', '#a855f7', '#f59e0b', '#38bdf8'];
        for (let index = 0; index < 12; index++) {
            lights += `<circle cx="${round(between(rand, 60, 940))}" cy="${round(between(rand, 60, 940))}" r="${round(between(rand, 40, 110))}" fill="${colors[index % 4]}" opacity="${round(between(rand, 0.15, 0.35))}" filter="url(#bokeh)"/>`;
        }
        const corner = (x, y, dx, dy) => `<path d="M${x},${y + dy * 90} L${x},${y} L${x + dx * 90},${y}" stroke="#ffffff" stroke-width="8" fill="none" stroke-linecap="round" stroke-linejoin="round"/>`;
        return {
            defs: [
                linear('beam', [[0, '#ffffff', 0.25], [1, '#ffffff', 0]]),
                linear('host', [[0, '#1a1024'], [1, '#08050c']]),
                blur('bokeh', 24),
                glow('rec-glow', 8),
            ].join(''),
            body: `
                ${lights}
                <polygon points="180,0 260,0 420,900 120,900" fill="url(#beam)" opacity="0.6"/>
                <polygon points="740,0 820,0 880,900 580,900" fill="url(#beam)" opacity="0.6"/>
                <ellipse cx="500" cy="400" rx="80" ry="96" fill="url(#host)"/>
                <path d="M290,790 C300,640 384,568 500,562 C616,568 700,640 710,790 Z" fill="url(#host)"/>
                <path d="M436,330 C456,302 544,302 564,330" stroke="#f472b6" stroke-width="5" fill="none" opacity="0.6"/>
                ${corner(120, 130, 1, 1)}
                ${corner(880, 130, -1, 1)}
                ${corner(120, 870, 1, -1)}
                ${corner(880, 870, -1, -1)}
                <circle cx="190" cy="200" r="18" fill="#ef4444" filter="url(#rec-glow)"/>
                <text x="222" y="214" font-family="Inter Tight" font-weight="800" font-size="40" letter-spacing="4" fill="#ffffff">REC</text>
                <rect x="730" y="182" width="92" height="40" rx="8" fill="none" stroke="#ffffff" stroke-width="5"/>
                <rect x="824" y="192" width="8" height="20" rx="3" fill="#ffffff"/>
                <rect x="740" y="192" width="20" height="20" rx="3" fill="#ffffff"/>
                <rect x="766" y="192" width="20" height="20" rx="3" fill="#ffffff"/>
                <rect x="792" y="192" width="20" height="20" rx="3" fill="#ffffff"/>
                <line x1="470" y1="500" x2="530" y2="500" stroke="#ffffff" stroke-width="4" opacity="0.7"/>
                <line x1="500" y1="470" x2="500" y2="530" stroke="#ffffff" stroke-width="4" opacity="0.7"/>
                <text x="500" y="852" text-anchor="middle" font-family="Inter Tight" font-weight="700" font-size="38" letter-spacing="6" fill="#ffffff" style="font-variant-numeric: tabular-nums">00:42:17:08</text>`,
        };
    },
};

export const romance = {
    palette: { bg: '#120308', a: '#f43f5e', b: '#9f1239', c: '#fda4af', ink: '#fff0f3' },
    draw: (p, rand) => {
        let petals = '';
        for (let index = 0; index < 8; index++) {
            petals += `<path d="${PETAL}" transform="translate(${round(between(rand, 100, 900))} ${round(between(rand, 560, 900))}) rotate(${round(between(rand, 0, 360))}) scale(${round(between(rand, 1, 1.8) * 100) / 100})" fill="url(#petal-loose)" opacity="${round(between(rand, 0.6, 0.95))}"/>`;
        }
        return {
            defs: [
                linear('outer', [[0, '#e11d48'], [1, '#6d0a22']], [0, 0, 1, 1]),
                linear('middle', [[0, '#fb7185'], [1, '#be123c']], [0, 0, 1, 1]),
                linear('inner', [[0, '#fecdd3'], [1, '#e11d48']]),
                linear('petal-loose', [[0, '#fb7185'], [1, '#9f1239']], [0, 0, 1, 1]),
                linear('stem', [[0, '#4d7c0f'], [1, '#1a3a07']], [0, 0, 1, 0]),
                linear('leaf', [[0, '#65a30d'], [1, '#1e4d0a']], [0, 0, 1, 1]),
                blur('halo', 60),
            ].join(''),
            body: `
                <circle cx="500" cy="400" r="300" fill="#f43f5e" opacity="0.35" filter="url(#halo)"/>
                ${heart(250, 220, 60, '#fda4af', -14, 'opacity="0.5"')}
                ${heart(780, 260, 46, '#fda4af', 14, 'opacity="0.45"')}
                <path d="M500,560 C490,680 520,780 508,920" stroke="url(#stem)" stroke-width="16" fill="none" stroke-linecap="round"/>
                <polygon points="497,650 470,636 494,672" fill="#2a4d0a"/>
                <polygon points="512,760 540,742 514,784" fill="#2a4d0a"/>
                <path d="M500,700 C430,650 350,660 320,700 C380,720 440,730 500,700 Z" fill="url(#leaf)"/>
                <path d="M510,800 C580,760 650,770 690,810 C630,830 560,830 510,800 Z" fill="url(#leaf)"/>
                <path d="M352,400 C330,290 420,220 500,220 C580,220 670,290 648,400 C626,500 374,500 352,400 Z" fill="url(#outer)"/>
                <path d="M340,380 C300,500 400,580 500,580 C462,530 430,470 424,390 Z" fill="url(#outer)"/>
                <path d="M660,380 C700,500 600,580 500,580 C538,530 570,470 576,390 Z" fill="url(#outer)"/>
                <path d="M392,370 C380,300 440,262 500,262 C560,262 620,300 608,370 C596,440 404,440 392,370 Z" fill="url(#middle)"/>
                <path d="M392,440 C420,540 580,540 608,440 C570,500 430,500 392,440 Z" fill="url(#middle)"/>
                <path d="M442,330 C462,282 540,282 556,330 C566,368 516,384 496,366 C476,350 492,326 516,332" stroke="#8a0f2c" stroke-width="7" fill="url(#inner)" stroke-linecap="round"/>
                <path d="M410,300 C420,270 450,250 480,244" stroke="#ffffff" stroke-opacity="0.35" stroke-width="7" fill="none" stroke-linecap="round"/>
                ${petals}
                ${sparkles(rand, 6, [100, 100, 900, 700], '#fff0f3', [5, 11], [0.4, 0.85])}`,
        };
    },
};

export const sports = {
    palette: { bg: '#05080f', a: '#f97316', b: '#2563eb', c: '#fed7aa', ink: '#f1f5ff' },
    draw: (p, rand) => ({
        defs: [
            radial('ball', [[0, '#ffb86b'], [0.55, '#f2700f'], [1, '#8a3305']], { cx: 0.35, cy: 0.3, r: 0.8 }),
            linear('beam', [[0, '#e0ecff', 0.32], [1, '#e0ecff', 0]]),
            linearAt('trail', [[0, '#f97316', 0], [1, '#f97316', 0.85]], [80, 0, 420, 0]),
            `<clipPath id="ball-clip"><circle cx="560" cy="450" r="200"/></clipPath>`,
            blur('halo', 60),
        ].join(''),
        body: `
            <polygon points="0,0 120,0 520,900 260,900" fill="url(#beam)" opacity="0.6"/>
            <polygon points="1000,0 880,0 640,900 900,900" fill="url(#beam)" opacity="0.6"/>
            ${specks(rand, 60, [30, 30, 970, 970], '#e0ecff', [1, 2.6], [0.3, 0.9])}
            <path d="M80,820 C200,760 320,640 400,560" stroke="url(#trail)" stroke-width="34" fill="none" stroke-linecap="round"/>
            <path d="M120,880 C250,800 350,700 420,640" stroke="url(#trail)" stroke-width="14" fill="none" stroke-linecap="round" opacity="0.6"/>
            <path d="M60,740 C170,690 290,590 370,520" stroke="url(#trail)" stroke-width="10" fill="none" stroke-linecap="round" opacity="0.5"/>
            <circle cx="560" cy="450" r="260" fill="#f97316" opacity="0.35" filter="url(#halo)"/>
            <circle cx="560" cy="450" r="200" fill="url(#ball)"/>
            <g clip-path="url(#ball-clip)" stroke="#2a1104" stroke-width="10" fill="none" transform="rotate(24 560 450)">
                <line x1="560" y1="240" x2="560" y2="660"/>
                <line x1="350" y1="450" x2="770" y2="450"/>
                <path d="M420,280 C500,360 500,540 420,620"/>
                <path d="M700,280 C620,360 620,540 700,620"/>
            </g>
            <ellipse cx="490" cy="370" rx="60" ry="34" fill="#ffffff" opacity="0.22" transform="rotate(-30 490 370)"/>`,
    }),
};

export const superhero = {
    palette: { bg: '#060818', a: '#ef4444', b: '#2563eb', c: '#fde047', ink: '#f1f4ff' },
    draw: (p, rand) => {
        let skyline = '';
        let x = 40;
        while (x < 960) {
            const width = round(between(rand, 50, 100));
            const height = round(between(rand, 120, 320));
            skyline += `<rect x="${x}" y="${1000 - height}" width="${width}" height="${height}" fill="#0b1230"/>`;
            for (let wy = 1000 - height + 20; wy < 990; wy += 34) {
                for (let wx = x + 10; wx < x + width - 14; wx += 22) {
                    if (rand() > 0.55) skyline += `<rect x="${round(wx)}" y="${round(wy)}" width="10" height="14" fill="#fde68a" opacity="0.55"/>`;
                }
            }
            x += width + 8;
        }
        return {
            defs: [
                linear('shield', [[0, '#ff6b6b'], [0.5, '#dc2626'], [1, '#7f1d1d']], [0, 0, 1, 1]),
                linear('bolt', [[0, '#fffbe6'], [1, '#facc15']]),
                linear('rim', [[0, '#fde68a'], [1, '#b45309']]),
                glow('bolt-glow', 14),
                blur('halo', 60),
                shineGradient('shine'),
            ].join(''),
            body: `
                ${rays(500, 470, 200, 560, 24, 6, '#3b82f6', 0.16)}
                ${rays(500, 470, 200, 560, 24, 3, '#ef4444', 0.16, 7.5)}
                <circle cx="500" cy="470" r="320" fill="#2563eb" opacity="0.3" filter="url(#halo)"/>
                ${skyline}
                <path d="M500,160 L770,246 C770,520 690,716 500,820 C310,716 230,520 230,246 Z" fill="url(#rim)"/>
                <path d="M500,196 L734,272 C732,512 662,690 500,780 C338,690 268,512 266,272 Z" fill="url(#shield)"/>
                <polygon points="566,286 432,516 508,516 452,716 604,456 524,456 592,286" fill="url(#bolt)" filter="url(#bolt-glow)"/>
                ${shine('shine', 400, 330, 50, 120, 0.25, 20)}
                ${sparkles(rand, 8, [80, 80, 920, 640], '#f1f4ff', [6, 12], [0.45, 0.9])}`,
        };
    },
};

export const thriller = {
    palette: { bg: '#040606', a: '#ef4444', b: '#0f172a', c: '#fecaca', ink: '#f4f4f5' },
    draw: (p, rand) => {
        let blinds = '';
        for (let index = 0; index < 14; index++) {
            blinds += `<rect x="-300" y="${-100 + index * 86}" width="1600" height="42" fill="#e5e7eb" opacity="0.075"/>`;
        }
        let cracks = '';
        for (let index = 0; index < 9; index++) {
            const angle = between(rand, 0, 360);
            let path = 'M720,280';
            let reach = 0;
            for (let step = 0; step < 4; step++) {
                reach += between(rand, 40, 90);
                const [x, y] = polar(720, 280, reach, angle + between(rand, -12, 12));
                path += ` L${round(x)},${round(y)}`;
            }
            cracks += `<path d="${path}" stroke="#ffffff" stroke-width="2.5" fill="none" opacity="0.45"/>`;
        }
        const trace = [[0, 540], [260, 540], [290, 518], [316, 540], [380, 540], [410, 400], [450, 700], [490, 300], [528, 620], [552, 540], [610, 540], [640, 494], [668, 540], [1000, 540]];
        return {
            defs: [
                linearAt('ecg', [[0, '#ef4444', 0], [0.35, '#ef4444', 0.8], [0.7, '#ff8080', 1], [1, '#ef4444', 0.2]], [0, 0, 1000, 0]),
                radialAt('blind-fade', [[0, '#fff'], [1, '#fff', 0]], { cx: 500, cy: 480, r: 520 }),
                `<mask id="blind-mask" maskContentUnits="userSpaceOnUse"><rect width="1000" height="1000" fill="url(#blind-fade)"/></mask>`,
                glow('ecg-glow', 10),
                blur('halo', 50),
            ].join(''),
            body: `
                <g mask="url(#blind-mask)" transform="rotate(-18 500 500)">${blinds}</g>
                ${cracks}
                <ellipse cx="500" cy="540" rx="380" ry="160" fill="#ef4444" opacity="0.22" filter="url(#halo)"/>
                <polyline points="${points(trace)}" fill="none" stroke="url(#ecg)" stroke-width="9" stroke-linejoin="round" stroke-linecap="round" filter="url(#ecg-glow)"/>
                <circle cx="668" cy="540" r="11" fill="#fff1f1" filter="url(#ecg-glow)"/>`,
        };
    },
};

function tag(x, y, rotate) {
    return `<g transform="rotate(${rotate} ${x} ${y})">
        <rect x="${x - 120}" y="${y - 175}" width="240" height="350" rx="58" fill="#000" opacity="0.4" transform="translate(10 14)"/>
        <rect x="${x - 120}" y="${y - 175}" width="240" height="350" rx="58" fill="url(#steel)"/>
        <rect x="${x - 120}" y="${y - 175}" width="240" height="350" rx="58" fill="none" stroke="#f2f0e4" stroke-opacity="0.35" stroke-width="4"/>
        <circle cx="${x}" cy="${y - 130}" r="16" fill="#0a0a06"/>
        ${[0, 1, 2, 3, 4].map(row => `<rect x="${x - 80}" y="${y - 70 + row * 42}" width="${[150, 120, 160, 100, 130][row]}" height="16" rx="5" fill="#5f6152" opacity="0.65"/><rect x="${x - 80}" y="${y - 72 + row * 42}" width="${[150, 120, 160, 100, 130][row]}" height="4" rx="2" fill="#f2f0e4" opacity="0.35"/>`).join('')}
    </g>`;
}

export const war = {
    palette: { bg: '#0a0a06', a: '#a3a36a', b: '#3f4a2a', c: '#e5e2c2', ink: '#f2f0e4' },
    draw: (p, rand) => {
        let chain = '';
        for (let index = 0; index <= 26; index++) {
            const t = index / 26;
            const x = (1 - t) * (1 - t) * 430 + 2 * (1 - t) * t * 520 + t * t * 640;
            const y = (1 - t) * (1 - t) * 330 + 2 * (1 - t) * t * 60 + t * t * 380;
            chain += `<circle cx="${round(x)}" cy="${round(y)}" r="7" fill="url(#steel)" stroke="#2a2a20" stroke-width="1.5"/>`;
        }
        let smoke = '';
        for (let index = 0; index < 10; index++) {
            smoke += `<circle cx="${round(between(rand, 60, 940))}" cy="${round(between(rand, 600, 960))}" r="${round(between(rand, 80, 160))}" fill="#4a4a3a" opacity="0.35" filter="url(#smoke)"/>`;
        }
        let wire = 'M0,900';
        for (let x = 40; x <= 1000; x += 40) wire += ` L${x},${x % 80 === 0 ? 884 : 914}`;
        let barbs = '';
        for (let x = 60; x < 1000; x += 80) barbs += `<path d="M${x - 12},${888} L${x + 12},${912} M${x + 12},${888} L${x - 12},${912}" stroke="#1a1a12" stroke-width="5"/>`;
        return {
            defs: [
                linear('steel', [[0, '#e8e6d6'], [0.5, '#a6a58e'], [1, '#5a5a48']], [0, 0, 1, 1]),
                blur('smoke', 40),
                blur('halo', 60),
            ].join(''),
            body: `
                ${smoke}
                <circle cx="500" cy="520" r="320" fill="none" stroke="#a3a36a" stroke-width="8" opacity="0.18"/>
                <polygon points="${starPoints(500, 520, 300, 120)}" fill="#a3a36a" opacity="0.1"/>
                <ellipse cx="500" cy="520" rx="320" ry="300" fill="#a3a36a" opacity="0.18" filter="url(#halo)"/>
                ${chain}
                ${tag(440, 520, -12)}
                ${tag(610, 560, 12)}
                <path d="${wire}" stroke="#1a1a12" stroke-width="6" fill="none"/>
                ${barbs}`,
        };
    },
};

export const western = {
    palette: { bg: '#140805', a: '#f97316', b: '#9a3412', c: '#fed7aa', ink: '#fff3e6' },
    draw: (p, rand) => ({
        defs: [
            linear('sun', [[0, '#ffe08a'], [0.5, '#fb923c'], [1, '#dc2626']]),
            linear('far', [[0, '#b4532a'], [1, '#6e2a12']]),
            linear('near', [[0, '#5c1f0c'], [1, '#2a0c04']]),
            blur('halo', 60),
        ].join(''),
        body: `
            ${specks(rand, 40, [30, 30, 970, 300], '#fff3e6', [1, 2.2], [0.25, 0.7])}
            <circle cx="500" cy="540" r="300" fill="#f97316" opacity="0.45" filter="url(#halo)"/>
            <circle cx="500" cy="540" r="230" fill="url(#sun)"/>
            <polygon points="0,740 0,650 80,640 110,570 300,570 330,650 420,660 450,610 560,610 590,670 760,670 790,590 940,590 970,660 1000,660 1000,740" fill="url(#far)"/>
            <polygon points="0,1000 0,780 150,770 180,700 340,700 370,790 620,800 650,730 820,730 850,800 1000,810 1000,1000" fill="url(#near)"/>
            <g fill="#1a0703">
                <rect x="170" y="560" width="46" height="400" rx="23"/>
                <path d="M170,700 L130,700 C110,700 100,690 100,670 L100,600 C100,586 112,576 124,576 C136,576 148,586 148,600 L148,656 L170,656 Z"/>
                <path d="M216,760 L250,760 C270,760 280,750 280,730 L280,640 C280,626 268,616 256,616 C244,616 232,626 232,640 L232,716 L216,716 Z"/>
            </g>
            <path d="M660,260 q20,-16 40,0 q20,-16 40,0" stroke="#2a0c04" stroke-width="6" fill="none" stroke-linecap="round"/>
            <path d="M760,320 q14,-12 28,0 q14,-12 28,0" stroke="#2a0c04" stroke-width="5" fill="none" stroke-linecap="round"/>`,
    }),
};

export const scienceFiction = {
    palette: { bg: '#04061a', a: '#6d5dfc', b: '#0ea5e9', c: '#7dd3fc', ink: '#dbeafe' },
    draw: (p, rand) => ({
        defs: [
            linear('planet', [[0, '#a497ff'], [0.45, '#5b3fd6'], [1, '#1f1366']], [0.2, 0, 0.8, 1]),
            radialAt('terminator', [[0, '#05041a', 0], [0.55, '#05041a', 0], [1, '#05041a', 0.9]], { cx: 380, cy: 400, r: 360 }),
            linearAt('ring', [[0, '#7dd3fc', 0], [0.2, '#7dd3fc', 0.85], [0.5, '#c4b5fd', 0.95], [0.8, '#7dd3fc', 0.85], [1, '#7dd3fc', 0]], [40, 0, 900, 0]),
            radialAt('moon', [[0, '#e6ecff'], [0.7, '#9aa8d6'], [1, '#4a5486']], { cx: 785, cy: 195, r: 60 }),
            linearAt('comet', [[0, '#ffffff', 0], [1, '#ffffff', 0.9]], [110, 170, 310, 250]),
            `<clipPath id="planet-clip"><circle cx="470" cy="530" r="250"/></clipPath>`,
            `<clipPath id="ring-front"><rect x="-200" y="530" width="1400" height="700"/></clipPath>`,
            blur('atmosphere', 22),
            blur('halo', 60),
            glow('ring-glow', 6),
        ].join(''),
        body: `
            ${specks(rand, 140, [20, 20, 980, 980], '#ffffff', [0.8, 2.4], [0.25, 0.95])}
            ${sparkles(rand, 9, [40, 40, 960, 960], '#dbeafe', [6, 15], [0.5, 1])}
            <path d="M110,170 L310,250" stroke="url(#comet)" stroke-width="5" stroke-linecap="round"/>
            <circle cx="310" cy="250" r="7" fill="#ffffff" filter="url(#ring-glow)"/>
            <circle cx="470" cy="530" r="300" fill="#6d5dfc" opacity="0.45" filter="url(#halo)"/>
            <g transform="rotate(-16 470 530)" filter="url(#ring-glow)">
                <ellipse cx="470" cy="530" rx="440" ry="96" fill="none" stroke="url(#ring)" stroke-width="26" opacity="0.6"/>
                <ellipse cx="470" cy="530" rx="384" ry="82" fill="none" stroke="url(#ring)" stroke-width="10" opacity="0.9"/>
                <ellipse cx="470" cy="530" rx="482" ry="106" fill="none" stroke="url(#ring)" stroke-width="4" opacity="0.5"/>
            </g>
            <circle cx="470" cy="530" r="256" fill="#8fd3ff" opacity="0.35" filter="url(#atmosphere)"/>
            <g clip-path="url(#planet-clip)">
                <circle cx="470" cy="530" r="250" fill="url(#planet)"/>
                <g transform="rotate(-16 470 530)">
                    <ellipse cx="470" cy="400" rx="420" ry="26" fill="#b8adff" opacity="0.35"/>
                    <ellipse cx="470" cy="470" rx="420" ry="44" fill="#3cc6ff" opacity="0.22"/>
                    <ellipse cx="470" cy="560" rx="420" ry="30" fill="#2b1c8f" opacity="0.45"/>
                    <ellipse cx="470" cy="630" rx="420" ry="52" fill="#8b5cf6" opacity="0.28"/>
                    <ellipse cx="470" cy="712" rx="420" ry="24" fill="#1e1060" opacity="0.5"/>
                </g>
                <circle cx="470" cy="530" r="250" fill="url(#terminator)"/>
            </g>
            <g transform="rotate(-16 470 530)" filter="url(#ring-glow)">
                <g clip-path="url(#ring-front)">
                    <ellipse cx="470" cy="530" rx="440" ry="96" fill="none" stroke="url(#ring)" stroke-width="26" opacity="0.6"/>
                    <ellipse cx="470" cy="530" rx="384" ry="82" fill="none" stroke="url(#ring)" stroke-width="10" opacity="0.9"/>
                    <ellipse cx="470" cy="530" rx="482" ry="106" fill="none" stroke="url(#ring)" stroke-width="4" opacity="0.5"/>
                </g>
            </g>
            <circle cx="785" cy="195" r="56" fill="url(#moon)"/>
            <circle cx="770" cy="182" r="10" fill="#7d88b8" opacity="0.5"/>
            <circle cx="800" cy="214" r="7" fill="#7d88b8" opacity="0.45"/>`,
    }),
};
