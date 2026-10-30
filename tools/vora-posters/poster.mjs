import { CATEGORIES, POSTERS } from './posters.mjs';
import { rng } from './art/kit.mjs';

const MARK = `<svg class="mark" viewBox="0 0 64 64" xmlns="http://www.w3.org/2000/svg">
    <defs>
        <linearGradient id="vora-v-mark" x1="0.1" y1="0" x2="0.55" y2="1">
            <stop offset="0" stop-color="#fdba74"/>
            <stop offset="0.55" stop-color="#fb923c"/>
            <stop offset="1" stop-color="#ea580c"/>
        </linearGradient>
    </defs>
    <path d="M6 8 L18 8 L32 40 L46 8 L58 8 L32 60 Z" fill="url(#vora-v-mark)"/>
</svg>`;

function fits(title, size, maxLines) {
    title.style.fontSize = `${size}px`;
    const lineHeight = parseFloat(getComputedStyle(title).lineHeight);
    const lines = Math.round(title.getBoundingClientRect().height / lineHeight);
    return title.scrollWidth <= title.clientWidth + 1 && lines <= maxLines;
}

function fitTitle(title, largest, maxLines) {
    const smallestSingleLine = Math.round(largest * 0.74);
    for (let size = largest; size >= smallestSingleLine; size -= 2) {
        if (fits(title, size, 1)) return;
    }
    for (let size = largest; size >= 56; size -= 2) {
        if (fits(title, size, maxLines)) return;
    }
}

async function draw() {
    const id = new URLSearchParams(location.search).get('id');
    const poster = POSTERS.find(candidate => candidate.id === id);
    if (!poster) throw new Error(`There is no poster called ${id}.`);
    const category = CATEGORIES.find(candidate => candidate.id === poster.category);
    const look = poster.look;

    const root = document.getElementById('poster');
    for (const [key, value] of Object.entries(look.palette)) {
        root.style.setProperty(`--${key}`, value);
    }

    const fonts = ['400 40px "Inter Tight"', '900 40px "Inter Tight"', ...(look.fonts ?? [])];
    await Promise.all(fonts.map(font => document.fonts.load(font)));

    const art = look.draw(look.palette, rng(poster.id));
    root.innerHTML = `
        <div class="base"></div>
        <div class="glow glow-1"></div>
        <div class="glow glow-2"></div>
        <div class="glow glow-3"></div>
        <svg class="art" viewBox="0 0 1000 1000" xmlns="http://www.w3.org/2000/svg">
            <defs>${art.defs ?? ''}</defs>
            ${art.body}
        </svg>
        <div class="shade"></div>
        <div class="frame"></div>
        <div class="caption">
            <div class="eyebrow">${poster.eyebrow ?? category.eyebrow}</div>
            <h1 class="title">${poster.title}</h1>
            ${poster.subtitle ? `<div class="subtitle">${poster.subtitle}</div>` : ''}
        </div>
        ${MARK}
        <div class="grain"></div>`;

    const title = root.querySelector('.title');
    Object.assign(title.style, look.titleStyle ?? {});
    root.getBoundingClientRect();
    await document.fonts.ready;
    fitTitle(title, look.titleSize ?? 150, look.titleLines ?? 2);
    await new Promise(done => requestAnimationFrame(() => requestAnimationFrame(done)));
    window.__posterState = 'ready';
}

draw().catch(error => {
    window.__posterState = `error: ${error.message}`;
});
