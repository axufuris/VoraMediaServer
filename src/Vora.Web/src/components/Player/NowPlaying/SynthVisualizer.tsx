import { useEffect, useRef } from 'react';
import { SYNTH_BANDS, bandEdges, bandLevels, fallPeaks, idleLevels, settle } from '../../../utils/synthBands';

interface SynthVisualizerProps {
    getAnalyser: () => AnalyserNode | null;
}

interface SynthColors {
    bar: string;
    highlight: string;
    line: string;
    grid: string;
}

function readColors(el: HTMLElement): SynthColors {
    const style = getComputedStyle(el);
    const token = (name: string) => style.getPropertyValue(name).trim() || style.color;
    return {
        bar: token('--vora-accent-500'),
        highlight: token('--vora-accent-text'),
        line: token('--vora-text-primary'),
        grid: token('--vora-border-strong'),
    };
}

function drawGrid(ctx: CanvasRenderingContext2D, w: number, h: number, baseline: number, colors: SynthColors, offset: number) {
    ctx.save();
    ctx.strokeStyle = colors.grid;
    ctx.globalAlpha = 0.35;
    ctx.lineWidth = 1;
    const depth = h - baseline;
    for (let i = 0; i < 7; i++) {
        const t = ((i + offset) % 7) / 7;
        const y = baseline + depth * t * t;
        ctx.beginPath();
        ctx.moveTo(0, y);
        ctx.lineTo(w, y);
        ctx.stroke();
    }
    for (let i = -8; i <= 8; i++) {
        ctx.beginPath();
        ctx.moveTo(w / 2 + i * (w / 40), baseline);
        ctx.lineTo(w / 2 + i * (w / 7), h);
        ctx.stroke();
    }
    ctx.restore();
}

function drawBars(ctx: CanvasRenderingContext2D, w: number, baseline: number, levels: number[], peaks: number[], colors: SynthColors, glow: boolean) {
    const slot = w / (levels.length * 2);
    const barWidth = Math.max(1, slot * 0.62);
    const maxHeight = baseline * 0.9;
    const gradient = ctx.createLinearGradient(0, baseline, 0, baseline - maxHeight);
    gradient.addColorStop(0, colors.bar);
    gradient.addColorStop(1, colors.highlight);

    ctx.save();
    if (glow) {
        ctx.shadowBlur = slot;
        ctx.shadowColor = colors.bar;
    }
    levels.forEach((level, i) => {
        const height = Math.max(2, Math.pow(level, 1.3) * maxHeight);
        const peakY = baseline - Math.max(height + 3, Math.pow(peaks[i] ?? 0, 1.3) * maxHeight);
        for (const x of [w / 2 + i * slot, w / 2 - (i + 1) * slot]) {
            const left = x + (slot - barWidth) / 2;
            ctx.fillStyle = gradient;
            ctx.globalAlpha = 1;
            ctx.fillRect(left, baseline - height, barWidth, height);
            ctx.globalAlpha = 0.22;
            ctx.fillRect(left, baseline, barWidth, height * 0.35);
            ctx.globalAlpha = 0.9;
            ctx.fillStyle = colors.highlight;
            ctx.fillRect(left, peakY, barWidth, 2);
        }
    });
    ctx.restore();
}

function drawWave(ctx: CanvasRenderingContext2D, w: number, baseline: number, wave: Uint8Array | null, seconds: number, colors: SynthColors, glow: boolean, scale: number) {
    const amplitude = baseline * 0.22;
    ctx.save();
    ctx.strokeStyle = colors.line;
    ctx.globalAlpha = 0.7;
    ctx.lineWidth = 2 * scale;
    if (glow) {
        ctx.shadowBlur = 10 * scale;
        ctx.shadowColor = colors.highlight;
    }
    ctx.beginPath();
    const points = 160;
    for (let p = 0; p <= points; p++) {
        const x = (p / points) * w;
        const sample = wave
            ? (wave[Math.floor((p / points) * (wave.length - 1))] - 128) / 128
            : Math.sin(seconds * 2 + p * 0.12) * 0.08;
        const y = baseline * 0.55 + sample * amplitude;
        if (p === 0) ctx.moveTo(x, y);
        else ctx.lineTo(x, y);
    }
    ctx.stroke();
    ctx.restore();
}

export default function SynthVisualizer({ getAnalyser }: SynthVisualizerProps) {
    const canvasRef = useRef<HTMLCanvasElement>(null);

    useEffect(() => {
        const canvas = canvasRef.current;
        const ctx = canvas?.getContext('2d');
        if (!canvas || !ctx) return;

        const reduceMotion = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false;
        const frameInterval = reduceMotion ? 100 : 0;
        let colors = readColors(canvas);
        let scale = 1;

        const resize = () => {
            const rect = canvas.getBoundingClientRect();
            scale = window.devicePixelRatio || 1;
            canvas.width = Math.max(1, Math.round(rect.width * scale));
            canvas.height = Math.max(1, Math.round(rect.height * scale));
            colors = readColors(canvas);
        };
        resize();
        const observer = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(resize);
        observer?.observe(canvas);

        let levels: number[] = new Array(SYNTH_BANDS).fill(0);
        let peaks: number[] = new Array(SYNTH_BANDS).fill(0);
        let edges: number[] = [];
        let frequencies: Uint8Array<ArrayBuffer> | null = null;
        let wave: Uint8Array<ArrayBuffer> | null = null;
        let frame = 0;
        let last = 0;

        const draw = (now: number) => {
            frame = requestAnimationFrame(draw);
            if (frameInterval && now - last < frameInterval) return;
            last = now;
            const seconds = now / 1000;

            const analyser = getAnalyser();
            let target: number[];
            if (analyser) {
                if (!frequencies || frequencies.length !== analyser.frequencyBinCount) {
                    frequencies = new Uint8Array(analyser.frequencyBinCount);
                    wave = new Uint8Array(analyser.fftSize);
                    edges = bandEdges(analyser.frequencyBinCount, analyser.context.sampleRate);
                }
                analyser.getByteFrequencyData(frequencies);
                if (wave) analyser.getByteTimeDomainData(wave);
                target = bandLevels(frequencies, edges);
            } else {
                wave = null;
                frequencies = null;
                target = idleLevels(SYNTH_BANDS, seconds);
            }
            levels = settle(levels, target);
            peaks = fallPeaks(peaks, levels);

            const w = canvas.width;
            const h = canvas.height;
            const baseline = h * 0.68;
            ctx.clearRect(0, 0, w, h);
            drawGrid(ctx, w, h, baseline, colors, reduceMotion ? 0 : (seconds * 0.6) % 7);
            drawBars(ctx, w, baseline, levels, peaks, colors, !reduceMotion);
            drawWave(ctx, w, baseline, wave, seconds, colors, !reduceMotion, scale);
        };
        frame = requestAnimationFrame(draw);

        return () => {
            cancelAnimationFrame(frame);
            observer?.disconnect();
        };
    }, [getAnalyser]);

    return (
        <canvas
            ref={canvasRef}
            role="img"
            aria-label="Music visualizer"
            data-testid="synth-visualizer"
            className="block h-full w-full"
            style={{ color: 'var(--vora-text-secondary)' }}
        />
    );
}
