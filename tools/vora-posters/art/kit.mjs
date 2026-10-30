export function rng(seed) {
    let state = 0;
    for (const character of seed) state = (state * 31 + character.charCodeAt(0)) | 0;
    return () => {
        state = (state + 0x6d2b79f5) | 0;
        let t = Math.imul(state ^ (state >>> 15), 1 | state);
        t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
        return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    };
}

export const round = value => Math.round(value * 10) / 10;

export const between = (rand, low, high) => low + rand() * (high - low);

export function polar(cx, cy, radius, degrees) {
    const angle = (degrees * Math.PI) / 180;
    return [cx + radius * Math.cos(angle), cy + radius * Math.sin(angle)];
}

export const points = list => list.map(([x, y]) => `${round(x)},${round(y)}`).join(' ');

const stopList = list => list
    .map(([offset, color, opacity = 1]) => `<stop offset="${offset}" stop-color="${color}"${opacity === 1 ? '' : ` stop-opacity="${opacity}"`}/>`)
    .join('');

export const linear = (id, list, [x1, y1, x2, y2] = [0, 0, 0, 1]) =>
    `<linearGradient id="${id}" x1="${x1}" y1="${y1}" x2="${x2}" y2="${y2}">${stopList(list)}</linearGradient>`;

export const linearAt = (id, list, [x1, y1, x2, y2]) =>
    `<linearGradient id="${id}" gradientUnits="userSpaceOnUse" x1="${x1}" y1="${y1}" x2="${x2}" y2="${y2}">${stopList(list)}</linearGradient>`;

export const radial = (id, list, { cx = 0.5, cy = 0.5, r = 0.5, fx = cx, fy = cy } = {}) =>
    `<radialGradient id="${id}" cx="${cx}" cy="${cy}" r="${r}" fx="${fx}" fy="${fy}">${stopList(list)}</radialGradient>`;

export const radialAt = (id, list, { cx, cy, r, fx = cx, fy = cy }) =>
    `<radialGradient id="${id}" gradientUnits="userSpaceOnUse" cx="${cx}" cy="${cy}" r="${r}" fx="${fx}" fy="${fy}">${stopList(list)}</radialGradient>`;

export const blur = (id, deviation) =>
    `<filter id="${id}" x="-60%" y="-60%" width="220%" height="220%"><feGaussianBlur stdDeviation="${deviation}"/></filter>`;

export const glow = (id, deviation) =>
    `<filter id="${id}" x="-60%" y="-60%" width="220%" height="220%"><feGaussianBlur in="SourceGraphic" stdDeviation="${deviation}" result="soft"/><feMerge><feMergeNode in="soft"/><feMergeNode in="SourceGraphic"/></feMerge></filter>`;

export function starPoints(cx, cy, outer, inner, count = 5, rotation = -90) {
    const corners = [];
    for (let index = 0; index < count * 2; index++) {
        const radius = index % 2 === 0 ? outer : inner;
        corners.push(polar(cx, cy, radius, rotation + (index * 180) / count));
    }
    return points(corners);
}

export function twinkle(x, y, radius) {
    const pinch = radius * 0.14;
    return `M${round(x)},${round(y - radius)} Q${round(x + pinch)},${round(y - pinch)} ${round(x + radius)},${round(y)} Q${round(x + pinch)},${round(y + pinch)} ${round(x)},${round(y + radius)} Q${round(x - pinch)},${round(y + pinch)} ${round(x - radius)},${round(y)} Q${round(x - pinch)},${round(y - pinch)} ${round(x)},${round(y - radius)} Z`;
}

export function sparkles(rand, count, [x0, y0, x1, y1], color, [small, large], [faint, bright] = [0.45, 1]) {
    let out = '';
    for (let index = 0; index < count; index++) {
        const x = between(rand, x0, x1);
        const y = between(rand, y0, y1);
        const size = between(rand, small, large);
        out += `<path d="${twinkle(x, y, size)}" fill="${color}" opacity="${round(between(rand, faint, bright))}"/>`;
    }
    return out;
}

export function specks(rand, count, [x0, y0, x1, y1], color, [small, large], [faint, bright] = [0.2, 0.8]) {
    let out = '';
    for (let index = 0; index < count; index++) {
        out += `<circle cx="${round(between(rand, x0, x1))}" cy="${round(between(rand, y0, y1))}" r="${round(between(rand, small, large))}" fill="${color}" opacity="${round(between(rand, faint, bright))}"/>`;
    }
    return out;
}

export function rays(cx, cy, inner, outer, count, spread, fill, opacity = 1, offset = 0) {
    let out = '';
    for (let index = 0; index < count; index++) {
        const centre = offset + (index * 360) / count;
        const wedge = [
            polar(cx, cy, inner, centre - spread / 2),
            polar(cx, cy, outer, centre - spread / 2),
            polar(cx, cy, outer, centre + spread / 2),
            polar(cx, cy, inner, centre + spread / 2),
        ];
        out += `<polygon points="${points(wedge)}" fill="${fill}" opacity="${opacity}"/>`;
    }
    return out;
}

export const shine = (id, cx, cy, rx, ry, opacity = 0.35, rotate = 0) =>
    `<ellipse cx="${cx}" cy="${cy}" rx="${rx}" ry="${ry}" fill="url(#${id})" opacity="${opacity}" transform="rotate(${rotate} ${cx} ${cy})"/>`;

export const shineGradient = id => radial(id, [[0, '#ffffff', 0.95], [0.6, '#ffffff', 0.25], [1, '#ffffff', 0]]);

export const floorShadow = (cx, cy, rx, ry, opacity = 0.55) =>
    `<ellipse cx="${cx}" cy="${cy}" rx="${rx}" ry="${ry}" fill="#000" opacity="${opacity}" filter="url(#floor-blur)"/>`;

export const floorBlur = blur('floor-blur', 24);

export const HEART = 'M50,90 C50,90 8,62 8,35 C8,20 20,8 34,8 C42,8 48,13 50,18 C52,13 58,8 66,8 C80,8 92,20 92,35 C92,62 50,90 50,90 Z';

export const heart = (cx, cy, size, fill, rotate = 0, extra = '') =>
    `<path d="${HEART}" transform="translate(${round(cx)} ${round(cy)}) rotate(${round(rotate)}) scale(${round(size / 84 * 100) / 100}) translate(-50 -49)" fill="${fill}" ${extra}/>`;
