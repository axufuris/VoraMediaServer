import type { ReactNode } from 'react';
import CinematicBackdrop from './CinematicBackdrop';

interface HeroProps {
    backdropSrc?: string | null;
    transitionKey?: string | number;
    eyebrow?: ReactNode;
    title: ReactNode;
    meta?: ReactNode;
    description?: ReactNode;
    ctas?: ReactNode;
    indicator?: ReactNode;
}

export default function Hero({ backdropSrc, transitionKey, eyebrow, title, meta, description, ctas, indicator }: HeroProps) {
    return (
        <section className="relative isolate">
            <CinematicBackdrop src={backdropSrc} intensity="hero" transitionKey={transitionKey} />
            <div className="absolute inset-0 flex items-end">
                <div className="w-full px-16 pb-20 max-w-3xl">
                    {eyebrow && (
                        <div
                            className="mb-4 inline-flex items-center gap-2 rounded-full px-3 py-1.5 text-xs font-medium"
                            style={{
                                background: 'rgba(255, 255, 255, 0.06)',
                                border: '1px solid var(--vora-border-subtle)',
                                color: 'var(--vora-text-secondary)',
                            }}
                        >
                            {eyebrow}
                        </div>
                    )}
                    <h1
                        className="m-0 font-semibold"
                        style={{
                            color: 'var(--vora-text-primary)',
                            fontSize: 'clamp(36px, 4.5vw, 56px)',
                            lineHeight: 1.05,
                            letterSpacing: '-0.02em',
                        }}
                    >
                        {title}
                    </h1>
                    {meta && <div className="mt-3 flex flex-wrap gap-3 text-sm" style={{ color: 'var(--vora-text-secondary)' }}>{meta}</div>}
                    {description && (
                        <p className="mt-4 max-w-2xl text-base leading-relaxed" style={{ color: 'var(--vora-text-secondary)' }}>
                            {description}
                        </p>
                    )}
                    {ctas && <div className="mt-6 flex flex-wrap gap-3">{ctas}</div>}
                </div>
            </div>
            {indicator && (
                <div className="pointer-events-none absolute bottom-6 left-1/2 -translate-x-1/2">
                    {indicator}
                </div>
            )}
        </section>
    );
}
