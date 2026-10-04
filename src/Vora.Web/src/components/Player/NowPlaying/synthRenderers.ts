import { withAlpha, type SynthPalette } from '../../../utils/artPalette';
import type { SynthStyle } from '../../../utils/nowPlayingView';

export interface SynthFrame {
    ctx: CanvasRenderingContext2D;
    width: number;
    height: number;
    scale: number;
    levels: number[];
    peaks: number[];
    pulse: number;
    seconds: number;
    palette: SynthPalette;
    cover: HTMLImageElement | null;
    glow: boolean;
}

// The spectrum is mirrored about the centre: bass in the middle, treble at
// both edges. `slot` runs over the doubled row.
function bandFor(slot: number, slots: number, bands: number): number {
    const half = slots / 2;
    const distance = slot < half ? half - 1 - slot : slot - half;
    return Math.min(bands - 1, Math.floor((distance / half) * bands));
}

function capsule(ctx: CanvasRenderingContext2D, x: number, y: number, w: number, h: number, r: number) {
    ctx.beginPath();
    if (typeof ctx.roundRect === 'function') {
        ctx.roundRect(x, y, w, h, r);
        return;
    }
    ctx.moveTo(x + r, y);
    ctx.arcTo(x + w, y, x + w, y + h, r);
    ctx.arcTo(x + w, y + h, x, y + h, r);
    ctx.arcTo(x, y + h, x, y, r);
    ctx.arcTo(x, y, x + w, y, r);
    ctx.closePath();
}

function drawBars(f: SynthFrame) {
    const { ctx, width: w, height: h, scale, levels, peaks, palette } = f;
    const slots = levels.length * 2;
    const usable = w * 0.9;
    const slot = usable / slots;
    const left = (w - usable) / 2;
    const cy = h * 0.5;
    const maxHalf = h * 0.42;
    const barWidth = Math.max(2 * scale, slot * 0.44);
    const radius = barWidth / 2;

    const gradient = ctx.createLinearGradient(0, cy - maxHalf, 0, cy + maxHalf);
    gradient.addColorStop(0, palette[2]);
    gradient.addColorStop(0.3, palette[1]);
    gradient.addColorStop(0.5, palette[0]);
    gradient.addColorStop(0.7, palette[1]);
    gradient.addColorStop(1, palette[2]);

    ctx.save();
    if (f.glow) {
        ctx.shadowColor = withAlpha(palette[0], 0.75);
        ctx.shadowBlur = 16 * scale;
    }
    ctx.fillStyle = gradient;
    for (let s = 0; s < slots; s++) {
        const half = Math.max(radius, Math.pow(levels[bandFor(s, slots, levels.length)], 1.2) * maxHalf);
        capsule(ctx, left + s * slot + (slot - barWidth) / 2, cy - half, barWidth, half * 2, radius);
        ctx.fill();
    }
    ctx.restore();

    ctx.save();
    ctx.fillStyle = withAlpha(palette[2], 0.95);
    for (let s = 0; s < slots; s++) {
        const band = bandFor(s, slots, levels.length);
        const level = Math.pow(levels[band], 1.2) * maxHalf;
        const peak = Math.pow(peaks[band] ?? 0, 1.2) * maxHalf;
        if (peak < level + 5 * scale) continue;
        ctx.beginPath();
        ctx.arc(left + s * slot + slot / 2, cy - peak - 5 * scale, radius * 0.85, 0, Math.PI * 2);
        ctx.fill();
    }
    ctx.restore();

    const fade = ctx.createLinearGradient(0, cy, 0, h);
    fade.addColorStop(0, 'rgba(0, 0, 0, 0)');
    fade.addColorStop(0.15, 'rgba(0, 0, 0, 0.45)');
    fade.addColorStop(1, 'rgba(0, 0, 0, 0.95)');
    ctx.save();
    ctx.globalCompositeOperation = 'destination-out';
    ctx.fillStyle = fade;
    ctx.fillRect(0, cy, w, h - cy);
    ctx.restore();

    const halo = ctx.createRadialGradient(w / 2, cy, 0, w / 2, cy, w * 0.42);
    halo.addColorStop(0, withAlpha(palette[0], 0.1 + 0.24 * f.pulse));
    halo.addColorStop(1, withAlpha(palette[0], 0));
    ctx.save();
    ctx.globalCompositeOperation = 'destination-over';
    ctx.fillStyle = halo;
    ctx.fillRect(0, 0, w, h);
    ctx.restore();
}

function curveThrough(ctx: CanvasRenderingContext2D, points: [number, number][]) {
    ctx.lineTo(points[0][0], points[0][1]);
    for (let i = 1; i < points.length - 1; i++) {
        ctx.quadraticCurveTo(points[i][0], points[i][1], (points[i][0] + points[i + 1][0]) / 2, (points[i][1] + points[i + 1][1]) / 2);
    }
    const last = points[points.length - 1];
    ctx.lineTo(last[0], last[1]);
}

function drawWaves(f: SynthFrame) {
    const { ctx, width: w, height: h, scale, levels, palette, seconds } = f;
    const cy = h * 0.5;
    const maxHalf = h * 0.44;
    const slots = levels.length * 2;
    const usable = w * 0.96;
    const left = (w - usable) / 2;
    const layers = [
        { color: palette[0], alpha: 0.6, size: 1, phase: 0 },
        { color: palette[1], alpha: 0.45, size: 0.78, phase: 2.1 },
        { color: palette[2], alpha: 0.36, size: 0.56, phase: 4.2 },
    ];

    const halo = ctx.createRadialGradient(w / 2, cy, 0, w / 2, cy, w * 0.45);
    halo.addColorStop(0, withAlpha(palette[0], 0.08 + 0.2 * f.pulse));
    halo.addColorStop(1, withAlpha(palette[0], 0));
    ctx.fillStyle = halo;
    ctx.fillRect(0, 0, w, h);

    ctx.save();
    ctx.globalCompositeOperation = 'lighter';
    layers.forEach((layer, index) => {
        const heights: [number, number][] = [];
        for (let s = 0; s <= slots; s++) {
            const edge = Math.sin((Math.PI * s) / slots);
            const drift = 0.82 + 0.18 * Math.sin(seconds * 1.5 + layer.phase + s * 0.32);
            const level = Math.pow(levels[bandFor(Math.min(s, slots - 1), slots, levels.length)], 1.1);
            heights.push([left + (usable * s) / slots, level * layer.size * drift * (0.2 + 0.8 * edge) * maxHalf]);
        }
        const fill = ctx.createLinearGradient(0, cy - maxHalf, 0, cy + maxHalf);
        fill.addColorStop(0, withAlpha(layer.color, 0));
        fill.addColorStop(0.5, withAlpha(layer.color, layer.alpha));
        fill.addColorStop(1, withAlpha(layer.color, 0));

        const top = heights.map(([x, v]): [number, number] => [x, cy - v]);
        ctx.beginPath();
        ctx.moveTo(heights[0][0], cy);
        curveThrough(ctx, top);
        curveThrough(ctx, heights.slice().reverse().map(([x, v]): [number, number] => [x, cy + v * 0.7]));
        ctx.closePath();
        ctx.fillStyle = fill;
        ctx.fill();

        if (index === 0) {
            ctx.beginPath();
            ctx.moveTo(top[0][0], top[0][1]);
            curveThrough(ctx, top);
            ctx.strokeStyle = withAlpha(palette[2], 0.85);
            ctx.lineWidth = 1.6 * scale;
            if (f.glow) {
                ctx.shadowColor = withAlpha(palette[1], 0.9);
                ctx.shadowBlur = 10 * scale;
            }
            ctx.stroke();
            ctx.shadowBlur = 0;
        }
    });
    ctx.restore();
}

function drawRing(f: SynthFrame) {
    const { ctx, width: w, height: h, scale, levels, palette } = f;
    const cx = w / 2;
    const cy = h / 2;
    const size = Math.min(w, h);
    const radius = size * 0.22;
    const maxLength = size * 0.2;
    const spokes = 120;
    const inner = radius + 9 * scale;

    const halo = ctx.createRadialGradient(cx, cy, radius * 0.8, cx, cy, radius + maxLength * 1.5);
    halo.addColorStop(0, withAlpha(palette[0], 0.16 + 0.26 * f.pulse));
    halo.addColorStop(1, withAlpha(palette[0], 0));
    ctx.fillStyle = halo;
    ctx.fillRect(0, 0, w, h);

    let stroke: string | CanvasGradient = palette[0];
    if (typeof ctx.createConicGradient === 'function') {
        const conic = ctx.createConicGradient(Math.PI / 2, cx, cy);
        conic.addColorStop(0, palette[0]);
        conic.addColorStop(0.25, palette[1]);
        conic.addColorStop(0.5, palette[2]);
        conic.addColorStop(0.75, palette[1]);
        conic.addColorStop(1, palette[0]);
        stroke = conic;
    }

    ctx.save();
    ctx.lineCap = 'round';
    ctx.lineWidth = Math.max(2 * scale, ((2 * Math.PI * inner) / spokes) * 0.48);
    ctx.strokeStyle = stroke;
    if (f.glow) {
        ctx.shadowColor = withAlpha(palette[1], 0.7);
        ctx.shadowBlur = 12 * scale;
    }
    ctx.beginPath();
    for (let s = 0; s < spokes; s++) {
        const half = spokes / 2;
        const position = s < half ? s : spokes - 1 - s;
        const band = Math.min(levels.length - 1, Math.floor((position / half) * levels.length));
        const length = 3 * scale + Math.pow(levels[band], 1.2) * maxLength;
        const angle = Math.PI / 2 + ((s + 0.5) / spokes) * Math.PI * 2;
        ctx.moveTo(cx + Math.cos(angle) * inner, cy + Math.sin(angle) * inner);
        ctx.lineTo(cx + Math.cos(angle) * (inner + length), cy + Math.sin(angle) * (inner + length));
    }
    ctx.stroke();
    ctx.restore();

    const r = radius * (1 + f.pulse * 0.035);
    ctx.save();
    ctx.beginPath();
    ctx.arc(cx, cy, r, 0, Math.PI * 2);
    ctx.clip();
    if (f.cover) {
        ctx.drawImage(f.cover, cx - r, cy - r, r * 2, r * 2);
    } else {
        const fill = ctx.createRadialGradient(cx, cy - r * 0.3, 0, cx, cy, r);
        fill.addColorStop(0, palette[1]);
        fill.addColorStop(1, palette[0]);
        ctx.fillStyle = fill;
        ctx.fillRect(cx - r, cy - r, r * 2, r * 2);
    }
    ctx.restore();
    ctx.beginPath();
    ctx.arc(cx, cy, r, 0, Math.PI * 2);
    ctx.strokeStyle = withAlpha(palette[2], 0.3);
    ctx.lineWidth = scale;
    ctx.stroke();
}

export function drawSynthFrame(style: SynthStyle, frame: SynthFrame) {
    frame.ctx.clearRect(0, 0, frame.width, frame.height);
    if (style === 'waves') drawWaves(frame);
    else if (style === 'ring') drawRing(frame);
    else drawBars(frame);
}
