import { between, blur, floorBlur, floorShadow, glow, heart, linear, linearAt, points, polar, radial, radialAt, rays, round, shineGradient, sparkles, specks, starPoints } from './kit.mjs';

const eraTitle = (font, gradient, extra = {}) => ({
    fontFamily: `${font}, "Inter Tight", sans-serif`,
    fontWeight: '400',
    textTransform: 'none',
    letterSpacing: '0.02em',
    lineHeight: '1.05',
    backgroundImage: gradient,
    ...extra,
});

export const twenties = {
    palette: { bg: '#0b0905', a: '#d4af37', b: '#3b2f12', c: '#f0d78c', ink: '#fbf1d6' },
    fonts: ['400 40px "Limelight"'],
    titleStyle: eraTitle('"Limelight"', 'linear-gradient(180deg, #fff6d6 0%, #e6c35c 55%, #9a7420 100%)', { letterSpacing: '0.06em' }),
    titleSize: 160,
    draw: (p, rand) => {
        let fan = '';
        for (let index = 0; index < 18; index++) {
            const start = 180 + index * 10;
            fan += `<polygon points="${points([[500, 860], polar(500, 860, 640, start), polar(500, 860, 640, start + 10)])}" fill="${index % 2 === 0 ? 'url(#gold)' : '#1a1408'}" opacity="${index % 2 === 0 ? 0.85 : 1}"/>`;
        }
        let chevrons = '';
        for (let index = 0; index < 5; index++) {
            chevrons += `<polyline points="${points([[380, 930 - index * 18], [500, 880 - index * 18], [620, 930 - index * 18]])}" fill="none" stroke="#d4af37" stroke-width="4" opacity="${round(0.8 - index * 0.13)}"/>`;
        }
        return {
            defs: [
                linear('gold', [[0, '#fff3c4'], [0.5, '#d4af37'], [1, '#7a5a14']], [0, 0, 1, 1]),
                linear('tower', [[0, '#f0d78c'], [0.5, '#b48a2c'], [1, '#5c430f']], [0, 0, 1, 0]),
                `<clipPath id="arch"><path d="M200,860 L200,440 A300,300 0 0 1 800,440 L800,860 Z"/></clipPath>`,
                blur('halo', 60),
            ].join(''),
            body: `
                <ellipse cx="500" cy="560" rx="360" ry="380" fill="#d4af37" opacity="0.22" filter="url(#halo)"/>
                <g clip-path="url(#arch)">
                    <rect x="200" y="140" width="600" height="720" fill="#120d05"/>
                    ${fan}
                    <path d="M260,860 A240,240 0 0 1 740,860" fill="none" stroke="#fff3c4" stroke-width="5" opacity="0.6"/>
                    <path d="M340,860 A160,160 0 0 1 660,860" fill="none" stroke="#fff3c4" stroke-width="5" opacity="0.6"/>
                </g>
                <path d="M200,860 L200,440 A300,300 0 0 1 800,440 L800,860" fill="none" stroke="url(#gold)" stroke-width="14"/>
                <path d="M172,880 L172,430 A328,328 0 0 1 828,430 L828,880" fill="none" stroke="#d4af37" stroke-width="4" opacity="0.7"/>
                <path d="M150,900 L150,420 A350,350 0 0 1 850,420 L850,900" fill="none" stroke="#d4af37" stroke-width="2" opacity="0.4"/>
                <polygon points="440,860 440,520 460,520 460,470 480,470 480,400 492,400 500,250 508,400 520,400 520,470 540,470 540,520 560,520 560,860" fill="url(#tower)"/>
                <polygon points="476,400 524,400 512,360 488,360" fill="#fff3c4" opacity="0.7"/>
                ${[0, 1, 2, 3, 4, 5, 6, 7].map(row => `<rect x="452" y="${560 + row * 36}" width="96" height="10" fill="#0b0905" opacity="0.55"/>`).join('')}
                <rect x="130" y="860" width="740" height="16" fill="url(#gold)"/>
                ${chevrons}
                <polygon points="${points([[150, 420], [164, 404], [178, 420], [164, 436]])}" fill="#f0d78c"/>
                <polygon points="${points([[822, 420], [836, 404], [850, 420], [836, 436]])}" fill="#f0d78c"/>
                ${sparkles(rand, 6, [100, 100, 900, 400], '#fff3c4', [6, 12], [0.4, 0.85])}`,
        };
    },
};

export const thirties = {
    palette: { bg: '#05070d', a: '#cbd5e1', b: '#1e3a5f', c: '#e2e8f0', ink: '#f1f5f9' },
    fonts: ['italic 900 40px "Playfair Display"'],
    titleStyle: eraTitle('"Playfair Display"', 'linear-gradient(180deg, #ffffff 0%, #cbd5e1 55%, #64748b 100%)', { fontStyle: 'italic', fontWeight: '900', letterSpacing: '0' }),
    titleSize: 170,
    draw: (p, rand) => {
        const beam = (x, angle) => {
            const tip = polar(x, 900, 1100, angle);
            const left = polar(tip[0], tip[1], 90, angle - 90);
            const right = polar(tip[0], tip[1], 90, angle + 90);
            return `<polygon points="${points([[x - 14, 900], left, right, [x + 14, 900]])}" fill="url(#beam)"/>`;
        };
        let bulbs = '';
        for (let x = 240; x <= 760; x += 26) {
            bulbs += `<circle cx="${x}" cy="640" r="6" fill="#fde68a" filter="url(#bulb)"/><circle cx="${x}" cy="740" r="6" fill="#fde68a" filter="url(#bulb)"/>`;
        }
        const letters = 'CINEMA'.split('').map((letter, index) => `<text x="500" y="${290 + index * 52}" text-anchor="middle" dominant-baseline="central" font-family="Inter Tight" font-weight="900" font-size="44" fill="#fff8e1" filter="url(#bulb)">${letter}</text>`).join('');
        return {
            defs: [
                linearAt('beam', [[0, '#e2e8f0', 0.45], [1, '#e2e8f0', 0]], [500, 900, 500, 0]),
                linear('facade', [[0, '#334155'], [1, '#0f172a']]),
                linear('sign', [[0, '#1e3a5f'], [1, '#0b1730']]),
                glow('bulb', 5),
                blur('halo', 60),
            ].join(''),
            body: `
                ${specks(rand, 90, [20, 20, 980, 600], '#ffffff', [0.8, 2.2], [0.3, 0.9])}
                ${beam(230, -62)}
                ${beam(250, -102)}
                ${beam(770, -118)}
                ${beam(750, -78)}
                <ellipse cx="500" cy="760" rx="360" ry="140" fill="#cbd5e1" opacity="0.2" filter="url(#halo)"/>
                <rect x="200" y="620" width="600" height="380" fill="url(#facade)"/>
                <polygon points="200,620 500,560 800,620" fill="#1e293b"/>
                <rect x="220" y="630" width="560" height="120" rx="10" fill="#0b1220" stroke="#fde68a" stroke-opacity="0.4" stroke-width="3"/>
                ${bulbs}
                <text x="500" y="690" text-anchor="middle" dominant-baseline="central" font-family="Playfair Display" font-style="italic" font-weight="900" font-size="52" fill="#f8fafc">Tonight</text>
                <rect x="452" y="250" width="96" height="340" rx="14" fill="url(#sign)" stroke="#fde68a" stroke-opacity="0.6" stroke-width="4"/>
                ${letters}
                ${[0, 1, 2, 3, 4].map(column => `<rect x="${250 + column * 110}" y="790" width="60" height="210" fill="#0b1220" opacity="0.8"/>`).join('')}`,
        };
    },
};

export const forties = {
    palette: { bg: '#0d0806', a: '#c2410c', b: '#3f2a1d', c: '#f5d0a9', ink: '#f8eadb' },
    fonts: ['400 40px "Abril Fatface"'],
    titleStyle: eraTitle('"Abril Fatface"', 'linear-gradient(180deg, #fff4e6 0%, #f5d0a9 55%, #b7794a 100%)', { letterSpacing: '0.01em' }),
    titleSize: 170,
    draw: (p, rand) => {
        let grille = '';
        for (let y = 280; y <= 520; y += 18) grille += `<line x1="410" y1="${y}" x2="590" y2="${y}" stroke="#2a1d14" stroke-width="6" opacity="0.7"/>`;
        const note = (x, y, scale, rotate) => `<g transform="translate(${x} ${y}) rotate(${rotate}) scale(${scale})" fill="#f5d0a9" opacity="0.45"><ellipse cx="0" cy="0" rx="22" ry="16" transform="rotate(-20)"/><rect x="16" y="-110" width="7" height="110"/><path d="M23,-110 C50,-96 60,-70 50,-50 C48,-70 36,-82 23,-84 Z"/></g>`;
        return {
            defs: [
                linear('chrome', [[0, '#fff4e6'], [0.35, '#d6b48c'], [0.6, '#7a5a3e'], [1, '#e8cfa8']], [0, 0, 1, 0]),
                linear('capsule', [[0, '#e8cfa8'], [0.5, '#a07a54'], [1, '#4a3424']], [0, 0, 1, 0]),
                linear('spot', [[0, '#fff4e6', 0.35], [1, '#fff4e6', 0]]),
                blur('halo', 60),
                floorBlur,
            ].join(''),
            body: `
                <polygon points="440,0 560,0 760,900 240,900" fill="url(#spot)"/>
                <ellipse cx="500" cy="440" rx="300" ry="300" fill="#c2410c" opacity="0.25" filter="url(#halo)"/>
                ${note(220, 380, 1.1, -12)}
                ${note(800, 300, 0.9, 10)}
                ${note(770, 600, 0.8, 16)}
                ${floorShadow(500, 900, 200, 22)}
                <rect x="482" y="600" width="36" height="290" fill="url(#chrome)"/>
                <ellipse cx="500" cy="895" rx="150" ry="26" fill="url(#chrome)"/>
                <path d="M370,420 L370,560 C370,630 630,630 630,560 L630,420" stroke="url(#chrome)" stroke-width="22" fill="none" stroke-linecap="round"/>
                <rect x="400" y="240" width="200" height="330" rx="100" fill="url(#capsule)"/>
                ${grille}
                <rect x="400" y="240" width="200" height="330" rx="100" fill="none" stroke="url(#chrome)" stroke-width="16"/>
                <rect x="392" y="388" width="216" height="30" rx="10" fill="url(#chrome)"/>
                <ellipse cx="450" cy="300" rx="18" ry="40" fill="#fff4e6" opacity="0.35"/>
                ${specks(rand, 30, [60, 60, 940, 940], '#f5d0a9', [1, 2.6], [0.25, 0.7])}`,
        };
    },
};

function atomicStar(x, y, radius, color, spikes) {
    let out = '';
    for (let index = 0; index < spikes; index++) {
        const [tx, ty] = polar(x, y, radius * (index % 2 === 0 ? 1 : 0.6), (index * 360) / spikes);
        out += `<line x1="${x}" y1="${y}" x2="${round(tx)}" y2="${round(ty)}" stroke="${color}" stroke-width="4" stroke-linecap="round"/><circle cx="${round(tx)}" cy="${round(ty)}" r="6" fill="${color}"/>`;
    }
    return out + `<circle cx="${x}" cy="${y}" r="8" fill="${color}"/>`;
}

const boomerang = (x, y, scale, rotate, fill) =>
    `<path transform="translate(${x} ${y}) rotate(${rotate}) scale(${scale})" d="M-80,20 C-40,-30 10,-50 80,-40 C40,-30 10,-10 -10,30 C-30,10 -55,10 -80,20 Z" fill="${fill}"/>`;

export const fifties = {
    palette: { bg: '#071413', a: '#2dd4bf', b: '#f472b6', c: '#fde68a', ink: '#effffb' },
    fonts: ['400 40px "Pacifico"'],
    titleStyle: eraTitle('"Pacifico"', 'linear-gradient(180deg, #ffffff 0%, #99f6e4 55%, #f472b6 100%)', { textTransform: 'none', letterSpacing: '0', lineHeight: '1.3' }),
    titleSize: 160,
    draw: (p, rand) => ({
        defs: [
            radial('nucleus', [[0, '#ffe4f1'], [0.6, '#f472b6'], [1, '#9d174d']], { cx: 0.4, cy: 0.35, r: 0.8 }),
            glow('orbit-glow', 6),
            blur('halo', 60),
        ].join(''),
        body: `
            <circle cx="500" cy="470" r="330" fill="#2dd4bf" opacity="0.28" filter="url(#halo)"/>
            ${boomerang(190, 230, 1.3, 20, '#f9a8d4')}
            ${boomerang(820, 780, 1.4, -160, '#fde68a')}
            ${boomerang(800, 250, 1, -30, '#5eead4')}
            ${boomerang(200, 760, 1.1, 150, '#5eead4')}
            ${atomicStar(250, 560, 70, '#fde68a', 12)}
            ${atomicStar(780, 500, 56, '#f9a8d4', 10)}
            ${atomicStar(640, 160, 44, '#5eead4', 10)}
            ${atomicStar(330, 860, 40, '#f9a8d4', 8)}
            <g filter="url(#orbit-glow)" fill="none" stroke-width="12">
                <ellipse cx="500" cy="470" rx="250" ry="90" stroke="#5eead4"/>
                <ellipse cx="500" cy="470" rx="250" ry="90" stroke="#fde68a" transform="rotate(60 500 470)"/>
                <ellipse cx="500" cy="470" rx="250" ry="90" stroke="#f9a8d4" transform="rotate(-60 500 470)"/>
            </g>
            <circle cx="750" cy="470" r="16" fill="#effffb"/>
            <circle cx="409" cy="251" r="16" fill="#effffb"/>
            <circle cx="505" cy="617" r="16" fill="#effffb"/>
            <circle cx="500" cy="470" r="62" fill="url(#nucleus)"/>
            <circle cx="482" cy="452" r="16" fill="#ffffff" opacity="0.6"/>
            ${specks(rand, 40, [60, 60, 940, 940], '#effffb', [2, 4], [0.3, 0.8])}`,
    }),
};

function daisy(x, y, radius, petals = 12) {
    let out = '';
    for (let index = 0; index < petals; index++) {
        const angle = (index * 360) / petals;
        const [px, py] = polar(x, y, radius * 0.62, angle);
        out += `<ellipse cx="${round(px)}" cy="${round(py)}" rx="${round(radius * 0.42)}" ry="${round(radius * 0.17)}" transform="rotate(${angle} ${round(px)} ${round(py)})" fill="#fff8ee"/>`;
    }
    return out + `<circle cx="${x}" cy="${y}" r="${round(radius * 0.3)}" fill="#facc15"/>`;
}

export const sixties = {
    palette: { bg: '#140612', a: '#f97316', b: '#db2777', c: '#fde047', ink: '#fff3e6' },
    fonts: ['400 40px "Righteous"'],
    titleStyle: eraTitle('"Righteous"', 'linear-gradient(180deg, #fff3e6 0%, #fdba74 50%, #f472b6 100%)'),
    titleSize: 170,
    draw: (p, rand) => {
        const rings = ['#f97316', '#fde047', '#db2777', '#fff3e6', '#f97316', '#db2777', '#fde047'];
        const target = rings.map((color, index) => `<circle cx="500" cy="470" r="${320 - index * 44}" fill="${color}"/>`).join('');
        let wave = '';
        for (let band = 0; band < 4; band++) {
            const y = 820 + band * 34;
            wave += `<path d="M0,${y} C150,${y - 50} 250,${y + 50} 400,${y} S650,${y - 50} 800,${y} S950,${y + 40} 1000,${y}" stroke="${rings[band]}" stroke-width="22" fill="none"/>`;
        }
        return {
            defs: [blur('halo', 60)].join(''),
            body: `
                <circle cx="500" cy="470" r="380" fill="#db2777" opacity="0.35" filter="url(#halo)"/>
                ${target}
                <circle cx="500" cy="470" r="44" fill="#140612"/>
                ${wave}
                ${daisy(190, 210, 110)}
                ${daisy(830, 190, 80)}
                ${daisy(810, 700, 120)}
                ${daisy(170, 690, 70)}
                ${sparkles(rand, 6, [80, 80, 920, 780], '#fff3e6', [6, 12], [0.5, 0.9])}`,
        };
    },
};

export const seventies = {
    palette: { bg: '#120a04', a: '#ea580c', b: '#854d0e', c: '#fcd34d', ink: '#fff4dc' },
    fonts: ['400 40px "Shrikhand"'],
    titleStyle: eraTitle('"Shrikhand"', 'linear-gradient(180deg, #fff4dc 0%, #fcd34d 45%, #ea580c 100%)', { letterSpacing: '0' }),
    titleSize: 160,
    draw: (p, rand) => {
        const bands = ['#7c2d12', '#c2410c', '#ea580c', '#f59e0b', '#fcd34d', '#fef3c7'];
        const arcs = bands.map((color, index) => {
            const radius = 400 - index * 52;
            return `<path d="M${500 - radius},880 A${radius},${radius} 0 0 1 ${500 + radius},880" stroke="${color}" stroke-width="50" fill="none"/>`;
        }).join('');
        let tiles = '';
        for (let row = -6; row <= 6; row++) {
            for (let column = -6; column <= 6; column++) {
                tiles += `<rect x="${720 + column * 22}" y="${250 + row * 22}" width="19" height="19" fill="${(row + column) % 3 === 0 ? '#f8fafc' : (row * column) % 2 === 0 ? '#94a3b8' : '#cbd5e1'}"/>`;
            }
        }
        return {
            defs: [
                `<clipPath id="ball"><circle cx="720" cy="250" r="120"/></clipPath>`,
                radialAt('ball-shade', [[0, '#ffffff', 0.4], [0.5, '#ffffff', 0], [1, '#000', 0.55]], { cx: 690, cy: 210, r: 150 }),
                glow('glint', 8),
                blur('halo', 60),
            ].join(''),
            body: `
                <ellipse cx="500" cy="700" rx="460" ry="300" fill="#ea580c" opacity="0.3" filter="url(#halo)"/>
                ${arcs}
                <line x1="720" y1="0" x2="720" y2="130" stroke="#cbd5e1" stroke-width="4"/>
                <circle cx="720" cy="250" r="150" fill="#fcd34d" opacity="0.25" filter="url(#halo)"/>
                <g clip-path="url(#ball)">${tiles}<circle cx="720" cy="250" r="120" fill="url(#ball-shade)"/></g>
                ${rays(720, 250, 140, 300, 16, 2, '#fff4dc', 0.25)}
                <g filter="url(#glint)">${sparkles(rand, 10, [580, 110, 860, 390], '#ffffff', [8, 18], [0.7, 1])}</g>
                ${sparkles(rand, 8, [80, 80, 600, 500], '#fff4dc', [5, 11], [0.4, 0.85])}`,
        };
    },
};

export const eighties = {
    palette: { bg: '#0a0418', a: '#ff2fb2', b: '#5b21b6', c: '#22d3ee', ink: '#ffd1f4' },
    fonts: ['400 40px "Monoton"'],
    titleStyle: eraTitle('"Monoton"', 'linear-gradient(180deg, #ffffff 0%, #ff8ae0 50%, #22d3ee 100%)', { filter: 'drop-shadow(0 0 22px rgba(255, 47, 178, 0.75))' }),
    titleSize: 170,
    draw: (p, rand) => {
        const horizon = 700;
        let grid = '';
        for (let step = 1; step <= 11; step++) {
            const t = step / 11;
            const y = horizon + 300 * t * t;
            grid += `<line x1="0" y1="${round(y)}" x2="1000" y2="${round(y)}" stroke="#ff2fb2" stroke-width="${round(1.5 + t * 2.5)}" opacity="${round(0.35 + t * 0.6)}"/>`;
        }
        for (let lane = -12; lane <= 12; lane++) {
            grid += `<line x1="${500 + lane * 14}" y1="${horizon}" x2="${500 + lane * 150}" y2="1000" stroke="#22d3ee" stroke-width="2.5" opacity="0.75"/>`;
        }
        let cuts = '';
        [[548, 7], [586, 10], [626, 13], [668, 16]].forEach(([y, h]) => {
            cuts += `<rect x="200" y="${y}" width="600" height="${h}" fill="#000"/>`;
        });
        return {
            defs: [
                linear('sun', [[0, '#ffe66e'], [0.45, '#ff9a3c'], [1, '#ff2f92']]),
                linear('floor', [[0, '#250a4a'], [1, '#0a0418']]),
                linear('peaks', [[0, '#3a1470'], [1, '#12052a']]),
                `<mask id="sun-cut"><rect x="0" y="0" width="1000" height="1000" fill="#fff"/>${cuts}</mask>`,
                `<clipPath id="floor-clip"><rect x="0" y="${horizon}" width="1000" height="300"/></clipPath>`,
                blur('sun-blur', 50),
                glow('neon', 5),
            ].join(''),
            body: `
                ${specks(rand, 90, [20, 20, 980, 560], '#ffffff', [0.8, 2.2], [0.25, 0.9])}
                <circle cx="500" cy="470" r="300" fill="#ff3fa4" opacity="0.4" filter="url(#sun-blur)"/>
                <circle cx="500" cy="470" r="250" fill="url(#sun)" mask="url(#sun-cut)"/>
                <polygon points="0,700 90,600 170,650 260,540 360,640 430,600 500,700" fill="url(#peaks)" stroke="#ff4fd8" stroke-opacity="0.7" stroke-width="3" filter="url(#neon)"/>
                <polygon points="500,700 580,620 650,660 740,560 830,640 900,590 1000,700" fill="url(#peaks)" stroke="#ff4fd8" stroke-opacity="0.7" stroke-width="3" filter="url(#neon)"/>
                <rect x="0" y="${horizon}" width="1000" height="300" fill="url(#floor)"/>
                <g clip-path="url(#floor-clip)" filter="url(#neon)">${grid}</g>
                <line x1="0" y1="${horizon}" x2="1000" y2="${horizon}" stroke="#ff7ad9" stroke-width="4" filter="url(#neon)"/>`,
        };
    },
};

export const nineties = {
    palette: { bg: '#0a0716', a: '#14b8a6', b: '#a855f7', c: '#facc15', ink: '#f5f3ff' },
    fonts: ['400 40px "Bungee"'],
    titleStyle: eraTitle('"Bungee"', 'linear-gradient(180deg, #ffffff 0%, #5eead4 50%, #c084fc 100%)', { letterSpacing: '0.01em' }),
    titleSize: 160,
    draw: (p, rand) => {
        const colors = ['#14b8a6', '#a855f7', '#facc15', '#f472b6'];
        let memphis = '';
        for (let index = 0; index < 16; index++) {
            const x = round(between(rand, 60, 940));
            const y = round(between(rand, 60, 940));
            const color = colors[index % 4];
            const kind = index % 4;
            if (kind === 0) memphis += `<polyline points="${points([[x, y], [x + 20, y - 20], [x + 40, y], [x + 60, y - 20], [x + 80, y]])}" fill="none" stroke="${color}" stroke-width="9" stroke-linecap="round" stroke-linejoin="round"/>`;
            else if (kind === 1) memphis += `<polygon points="${points([[x, y - 26], [x + 30, y + 22], [x - 30, y + 22]])}" fill="none" stroke="${color}" stroke-width="7"/>`;
            else if (kind === 2) memphis += `<circle cx="${x}" cy="${y}" r="${round(between(rand, 12, 26))}" fill="${color}"/>`;
            else memphis += `<path d="M${x - 30},${y} A30,30 0 0 1 ${x + 30},${y} Z" fill="${color}"/>`;
        }
        let dots = '';
        for (let row = 0; row < 5; row++) {
            for (let column = 0; column < 6; column++) dots += `<circle cx="${130 + column * 26}" cy="${780 + row * 26}" r="5" fill="#f5f3ff" opacity="0.5"/>`;
        }
        const reel = x => {
            let teeth = '';
            for (let index = 0; index < 6; index++) {
                const [tx, ty] = polar(x, 520, 30, index * 60);
                teeth += `<rect x="${round(tx - 6)}" y="${round(ty - 6)}" width="12" height="12" rx="3" fill="#1e1b2e" transform="rotate(${index * 60} ${round(tx)} ${round(ty)})"/>`;
            }
            return `<circle cx="${x}" cy="520" r="54" fill="#f5f3ff"/><circle cx="${x}" cy="520" r="40" fill="#d8d4ee"/>${teeth}<circle cx="${x}" cy="520" r="14" fill="#1e1b2e"/>`;
        };
        return {
            defs: [
                linear('shell', [[0, '#7c3aed'], [1, '#3b0764']], [0, 0, 1, 1]),
                linear('label', [[0, '#fef9c3'], [1, '#fde68a']]),
                blur('halo', 60),
                floorBlur,
            ].join(''),
            body: `
                ${memphis}
                ${dots}
                <ellipse cx="500" cy="520" rx="340" ry="260" fill="#14b8a6" opacity="0.28" filter="url(#halo)"/>
                ${floorShadow(500, 760, 300, 24)}
                <rect x="230" y="330" width="540" height="350" rx="28" fill="url(#shell)"/>
                <rect x="230" y="330" width="540" height="350" rx="28" fill="none" stroke="#c4b5fd" stroke-opacity="0.4" stroke-width="4"/>
                <rect x="270" y="364" width="460" height="230" rx="16" fill="url(#label)"/>
                <rect x="270" y="364" width="460" height="40" rx="16" fill="#14b8a6"/>
                <rect x="270" y="388" width="460" height="16" fill="#14b8a6"/>
                <rect x="270" y="404" width="460" height="10" fill="#f472b6"/>
                <rect x="350" y="470" width="300" height="100" rx="50" fill="#1e1b2e"/>
                ${reel(410)}
                ${reel(590)}
                <rect x="470" y="496" width="60" height="48" fill="#5b4b8a" opacity="0.7"/>
                <polygon points="330,680 670,680 640,620 360,620" fill="#5b21b6"/>
                ${[[252, 352], [748, 352], [252, 658], [748, 658]].map(([x, y]) => `<circle cx="${x}" cy="${y}" r="8" fill="#c4b5fd" opacity="0.7"/>`).join('')}`,
        };
    },
};

function bubble(x, y, radius) {
    return `<circle cx="${x}" cy="${y}" r="${radius}" fill="url(#bubble)"/>
        <ellipse cx="${round(x - radius * 0.3)}" cy="${round(y - radius * 0.45)}" rx="${round(radius * 0.46)}" ry="${round(radius * 0.26)}" fill="#ffffff" opacity="0.8" transform="rotate(-20 ${round(x - radius * 0.3)} ${round(y - radius * 0.45)})"/>
        <ellipse cx="${round(x + radius * 0.2)}" cy="${round(y + radius * 0.55)}" rx="${round(radius * 0.4)}" ry="${round(radius * 0.16)}" fill="#bae6fd" opacity="0.4"/>`;
}

export const twoThousands = {
    palette: { bg: '#030b16', a: '#38bdf8', b: '#6366f1', c: '#bae6fd', ink: '#eef8ff' },
    fonts: ['900 40px "Orbitron"'],
    titleStyle: eraTitle('"Orbitron"', 'linear-gradient(180deg, #ffffff 0%, #bae6fd 45%, #64748b 60%, #e0f2fe 100%)', { fontWeight: '900', letterSpacing: '0.02em' }),
    titleSize: 150,
    draw: (p, rand) => {
        let pixels = '';
        for (let index = 0; index < 18; index++) {
            const x = round(between(rand, 60, 940));
            const y = round(between(rand, 60, 940));
            pixels += `<rect x="${x}" y="${y}" width="10" height="10" fill="#bae6fd" opacity="${round(between(rand, 0.4, 0.9))}"/><rect x="${x + 10}" y="${y}" width="10" height="10" fill="#bae6fd" opacity="0.3"/><rect x="${x}" y="${y + 10}" width="10" height="10" fill="#bae6fd" opacity="0.3"/>`;
        }
        return {
            defs: [
                radial('bubble', [[0, '#e0f7ff', 0.25], [0.7, '#38bdf8', 0.35], [1, '#0369a1', 0.85]], { cx: 0.45, cy: 0.4, r: 0.6 }),
                linear('swoosh', [[0, '#bae6fd', 0], [0.3, '#7dd3fc', 0.8], [0.7, '#a5b4fc', 0.8], [1, '#a5b4fc', 0]], [0, 0, 1, 0]),
                radial('orb', [[0, '#f0f9ff'], [0.35, '#7dd3fc'], [0.8, '#2563eb'], [1, '#1e1b4b']], { cx: 0.38, cy: 0.32, r: 0.75 }),
                blur('halo', 60),
                glow('soft', 8),
            ].join(''),
            body: `
                ${pixels}
                <circle cx="500" cy="480" r="320" fill="#38bdf8" opacity="0.32" filter="url(#halo)"/>
                <path d="M60,720 C260,520 500,820 940,420 C620,760 300,600 60,760 Z" fill="url(#swoosh)" filter="url(#soft)"/>
                <circle cx="500" cy="470" r="220" fill="url(#orb)"/>
                <ellipse cx="430" cy="360" rx="120" ry="66" fill="#ffffff" opacity="0.65" transform="rotate(-24 430 360)"/>
                <ellipse cx="540" cy="620" rx="120" ry="30" fill="#bae6fd" opacity="0.35"/>
                ${bubble(200, 270, 80)}
                ${bubble(790, 240, 60)}
                ${bubble(800, 690, 96)}
                ${bubble(230, 760, 52)}
                ${bubble(690, 860, 36)}
                ${sparkles(rand, 10, [80, 80, 920, 920], '#ffffff', [6, 14], [0.6, 1])}`,
        };
    },
};

export const twentyTens = {
    palette: { bg: '#06080f', a: '#f43f5e', b: '#6366f1', c: '#fda4af', ink: '#fff1f2' },
    titleStyle: { textTransform: 'none' },
    titleSize: 160,
    draw: (p, rand) => {
        const colors = ['#f43f5e', '#f59e0b', '#22c55e', '#0ea5e9', '#6366f1', '#ec4899', '#14b8a6', '#eab308'];
        let apps = '';
        for (let row = 0; row < 5; row++) {
            for (let column = 0; column < 4; column++) {
                apps += `<rect x="${388 + column * 60}" y="${290 + row * 70}" width="44" height="44" rx="12" fill="${colors[(row * 4 + column) % colors.length]}"/>`;
            }
        }
        return {
            defs: [
                linear('screen', [[0, '#312e81'], [1, '#831843']], [0, 0, 1, 1]),
                linear('long', [[0, '#000', 0.45], [1, '#000', 0]], [0, 0, 1, 1]),
                blur('halo', 60),
            ].join(''),
            body: `
                <circle cx="500" cy="500" r="330" fill="#f43f5e" opacity="0.28" filter="url(#halo)"/>
                <circle cx="500" cy="500" r="300" fill="#1e1b4b"/>
                <polygon points="350,250 650,250 1000,600 1000,1000 700,1000 350,700" fill="url(#long)" opacity="0.8"/>
                <rect x="350" y="200" width="300" height="580" rx="44" fill="#0f172a"/>
                <rect x="366" y="246" width="268" height="480" rx="10" fill="url(#screen)"/>
                ${apps}
                <rect x="460" y="222" width="80" height="8" rx="4" fill="#334155"/>
                <circle cx="500" cy="752" r="16" fill="none" stroke="#334155" stroke-width="4"/>
                ${heart(230, 330, 90, '#f43f5e')}
                <rect x="700" y="300" width="130" height="96" rx="30" fill="#0ea5e9"/>
                <polygon points="730,390 720,430 770,394" fill="#0ea5e9"/>
                <rect x="160" y="600" width="120" height="90" rx="22" fill="#eab308"/>
                <circle cx="220" cy="645" r="26" fill="#0f172a"/>
                <circle cx="220" cy="645" r="14" fill="#eab308"/>
                <rect x="240" y="586" width="26" height="14" rx="4" fill="#eab308"/>`,
        };
    },
};

export const twentyTwenties = {
    palette: { bg: '#05050c', a: '#a855f7', b: '#22d3ee', c: '#f0abfc', ink: '#f8f5ff' },
    titleStyle: { textTransform: 'none' },
    titleSize: 160,
    draw: (p, rand) => {
        const blobs = `<circle cx="360" cy="380" r="200" fill="#a855f7"/><circle cx="650" cy="520" r="220" fill="#22d3ee"/><circle cx="500" cy="760" r="160" fill="#f472b6"/>`;
        const glassCard = (x, y, w, h, rotate) => `<g transform="rotate(${rotate} ${x + w / 2} ${y + h / 2})">
            <clipPath id="card-${x}"><rect x="${x}" y="${y}" width="${w}" height="${h}" rx="34"/></clipPath>
            <g clip-path="url(#card-${x})"><g filter="url(#frost)" transform="rotate(${-rotate} ${x + w / 2} ${y + h / 2})">${blobs}</g><rect x="${x}" y="${y}" width="${w}" height="${h}" fill="#ffffff" opacity="0.1"/></g>
            <rect x="${x}" y="${y}" width="${w}" height="${h}" rx="34" fill="none" stroke="#ffffff" stroke-opacity="0.35" stroke-width="3"/>
            <rect x="${x + 34}" y="${y + h - 80}" width="${w * 0.5}" height="18" rx="9" fill="#ffffff" opacity="0.7"/>
            <rect x="${x + 34}" y="${y + h - 50}" width="${w * 0.32}" height="14" rx="7" fill="#ffffff" opacity="0.4"/>
        </g>`;
        return {
            defs: [
                blur('blob-blur', 50),
                blur('frost', 34),
            ].join(''),
            body: `
                <g filter="url(#blob-blur)">${blobs}</g>
                ${glassCard(220, 250, 380, 300, -8)}
                ${glassCard(420, 470, 360, 280, 6)}
                <circle cx="760" cy="240" r="52" fill="#ffffff" opacity="0.12" stroke="#ffffff" stroke-opacity="0.4" stroke-width="3"/>
                <rect x="160" y="680" width="150" height="56" rx="28" fill="#ffffff" opacity="0.12" stroke="#ffffff" stroke-opacity="0.35" stroke-width="3"/>
                ${sparkles(rand, 10, [80, 80, 920, 920], '#f8f5ff', [5, 12], [0.5, 0.95])}`,
        };
    },
};
