import { useEffect, useRef, useState } from 'react';

export type BackdropIntensity = 'hero' | 'detail' | 'ambient';

interface CinematicBackdropProps {
    src?: string | null;
    intensity?: BackdropIntensity;
    parallax?: boolean;
    transitionKey?: string | number;
    className?: string;
}

const HEIGHT_BY_INTENSITY: Record<BackdropIntensity, string> = {
    hero: '50vh',
    detail: '70vh',
    ambient: '30vh',
};

export default function CinematicBackdrop({ src, intensity = 'detail', parallax, transitionKey, className }: CinematicBackdropProps) {
    const [currentSrc, setCurrentSrc] = useState<string | null>(src ?? null);
    const [opacity, setOpacity] = useState<number>(src ? 1 : 0);
    const lastKeyRef = useRef<string | number | undefined>(transitionKey);
    const containerRef = useRef<HTMLDivElement | null>(null);

    useEffect(() => {
        const key = transitionKey ?? src ?? undefined;
        if (key === lastKeyRef.current) return;
        lastKeyRef.current = key;
        queueMicrotask(() => setOpacity(0));
        const t = window.setTimeout(() => {
            setCurrentSrc(src ?? null);
            setOpacity(src ? 1 : 0);
        }, 320);
        return () => window.clearTimeout(t);
    }, [src, transitionKey]);

    useEffect(() => {
        if (!parallax || typeof window === 'undefined') return;
        const reduce = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
        if (reduce) return;
        const onScroll = () => {
            const el = containerRef.current;
            if (!el) return;
            const offset = window.scrollY * 0.4;
            el.style.transform = `translate3d(0, ${offset}px, 0)`;
        };
        window.addEventListener('scroll', onScroll, { passive: true });
        return () => window.removeEventListener('scroll', onScroll);
    }, [parallax]);

    const height = HEIGHT_BY_INTENSITY[intensity];

    return (
        <div
            className={`relative w-full overflow-hidden ${className ?? ''}`}
            style={{ height, minHeight: intensity === 'detail' ? 540 : intensity === 'hero' ? 420 : 240 }}
            aria-hidden="true"
        >
            <div
                ref={containerRef}
                className="absolute inset-0 will-change-transform"
                style={{
                    transition: 'opacity 560ms var(--vora-ease-out, ease-out)',
                    opacity,
                    backgroundImage: currentSrc ? `url("${currentSrc}")` : 'none',
                    backgroundSize: 'cover',
                    backgroundPosition: 'center',
                    backgroundColor: 'var(--vora-bg-sunken)',
                }}
            />
            <div
                className="absolute inset-0 pointer-events-none"
                style={{
                    backgroundImage: intensity === 'detail'
                        ? 'linear-gradient(180deg, rgba(0,0,0,0.35) 0%, rgba(0,0,0,0.15) 20%, rgba(0,0,0,0.55) 60%, var(--vora-bg-canvas) 96%)'
                        : 'linear-gradient(180deg, rgba(0,0,0,0) 0%, rgba(0,0,0,0) 30%, var(--vora-bg-canvas) 96%)',
                }}
            />
            <div
                className="absolute inset-0 pointer-events-none"
                style={{
                    backgroundImage: intensity === 'detail'
                        ? 'linear-gradient(90deg, rgba(0,0,0,0.78) 0%, rgba(0,0,0,0.55) 35%, rgba(0,0,0,0.15) 70%, rgba(0,0,0,0) 100%)'
                        : 'linear-gradient(90deg, rgba(0,0,0,0.55) 0%, rgba(0,0,0,0) 65%)',
                }}
            />
        </div>
    );
}
