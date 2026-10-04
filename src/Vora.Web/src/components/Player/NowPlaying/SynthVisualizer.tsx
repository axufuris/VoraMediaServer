import { useEffect, useRef } from 'react';
import { SYNTH_BANDS, bandEdges, bandLevels, fallPeaks, idleLevels, settle } from '../../../utils/synthBands';
import { lighten, type SynthPalette } from '../../../utils/artPalette';
import type { SynthStyle } from '../../../utils/nowPlayingView';
import { drawSynthFrame } from './synthRenderers';

interface SynthVisualizerProps {
    getAnalyser: () => AnalyserNode | null;
    style: SynthStyle;
    palette: SynthPalette | null;
    cover: HTMLImageElement | null;
}

const BASS_BANDS = 4;

// Canvas only understands concrete colours, so each token is resolved through
// a scratch context, which also normalises whatever form the template wrote.
function themePalette(el: HTMLElement): SynthPalette {
    const style = getComputedStyle(el);
    const probe = document.createElement('canvas').getContext('2d');
    const resolve = (name: string) => {
        const raw = style.getPropertyValue(name).trim() || style.color;
        if (!probe) return raw;
        probe.fillStyle = raw;
        return String(probe.fillStyle);
    };
    const accent = resolve('--vora-accent-500');
    const accentText = resolve('--vora-accent-text');
    return [accent, accentText, lighten(accentText, 0.18)];
}

export default function SynthVisualizer({ getAnalyser, style, palette, cover }: SynthVisualizerProps) {
    const canvasRef = useRef<HTMLCanvasElement>(null);
    const styleRef = useRef(style);
    const paletteRef = useRef(palette);
    const coverRef = useRef(cover);

    useEffect(() => { styleRef.current = style; }, [style]);
    useEffect(() => { paletteRef.current = palette; }, [palette]);
    useEffect(() => { coverRef.current = cover; }, [cover]);

    useEffect(() => {
        const canvas = canvasRef.current;
        const ctx = canvas?.getContext('2d');
        if (!canvas || !ctx) return;

        const reduceMotion = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false;
        const frameInterval = reduceMotion ? 100 : 0;
        let theme = themePalette(canvas);
        let scale = 1;

        const resize = () => {
            const rect = canvas.getBoundingClientRect();
            scale = Math.min(2, window.devicePixelRatio || 1);
            canvas.width = Math.max(1, Math.round(rect.width * scale));
            canvas.height = Math.max(1, Math.round(rect.height * scale));
            theme = themePalette(canvas);
        };
        resize();
        const observer = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(resize);
        observer?.observe(canvas);

        let levels: number[] = new Array(SYNTH_BANDS).fill(0);
        let peaks: number[] = new Array(SYNTH_BANDS).fill(0);
        let edges: number[] = [];
        let frequencies: Uint8Array<ArrayBuffer> | null = null;
        let slowBass = 0;
        let pulse = 0;
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
                    edges = bandEdges(analyser.frequencyBinCount, analyser.context.sampleRate);
                }
                analyser.getByteFrequencyData(frequencies);
                target = bandLevels(frequencies, edges);
            } else {
                frequencies = null;
                target = idleLevels(SYNTH_BANDS, seconds);
            }
            levels = settle(levels, target);
            peaks = fallPeaks(peaks, levels);

            const bass = levels.slice(0, BASS_BANDS).reduce((sum, v) => sum + v, 0) / BASS_BANDS;
            slowBass += (bass - slowBass) * 0.04;
            pulse = Math.max(Math.min(1, Math.max(0, (bass - slowBass) * 3.5)), pulse - 0.06);

            drawSynthFrame(styleRef.current, {
                ctx,
                width: canvas.width,
                height: canvas.height,
                scale,
                levels,
                peaks,
                pulse,
                seconds,
                palette: paletteRef.current ?? theme,
                cover: coverRef.current,
                glow: !reduceMotion,
            });
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
            data-synth-style={style}
            className="block h-full w-full"
            style={{ color: 'var(--vora-text-secondary)' }}
        />
    );
}
