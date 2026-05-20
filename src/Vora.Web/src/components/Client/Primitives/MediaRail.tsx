import { useRef, type ReactNode } from 'react';

interface MediaRailProps {
    title: string;
    moreHref?: string;
    onMore?: () => void;
    children: ReactNode;
    className?: string;
}

export default function MediaRail({ title, moreHref, onMore, children, className }: MediaRailProps) {
    const scrollerRef = useRef<HTMLDivElement | null>(null);

    const scrollBy = (delta: number) => {
        const el = scrollerRef.current;
        if (!el) return;
        el.scrollBy({ left: delta, behavior: 'smooth' });
    };

    return (
        <section className={`px-8 ${className ?? ''}`}>
            <div className="mb-4 flex items-center justify-between gap-3">
                <h2 className="m-0 text-lg font-semibold" style={{ color: 'var(--vora-text-primary)', letterSpacing: '-0.01em' }}>
                    {title}
                </h2>
                <div className="flex items-center gap-2">
                    <button
                        type="button"
                        aria-label="Scroll left"
                        onClick={() => scrollBy(-600)}
                        className="hidden cursor-pointer items-center justify-center rounded-full p-2 transition-colors hover:bg-white/5 md:inline-flex"
                        style={{ color: 'var(--vora-text-muted)' }}
                    >
                        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><polyline points="15 18 9 12 15 6" /></svg>
                    </button>
                    <button
                        type="button"
                        aria-label="Scroll right"
                        onClick={() => scrollBy(600)}
                        className="hidden cursor-pointer items-center justify-center rounded-full p-2 transition-colors hover:bg-white/5 md:inline-flex"
                        style={{ color: 'var(--vora-text-muted)' }}
                    >
                        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><polyline points="9 18 15 12 9 6" /></svg>
                    </button>
                    {(moreHref || onMore) && (
                        <a
                            href={moreHref}
                            onClick={(e) => { if (onMore) { e.preventDefault(); onMore(); } }}
                            className="ml-1 cursor-pointer text-xs font-medium"
                            style={{ color: 'var(--vora-text-muted)' }}
                        >
                            View all →
                        </a>
                    )}
                </div>
            </div>
            <div
                ref={scrollerRef}
                className="flex gap-4 overflow-x-auto pb-2"
                style={{ scrollSnapType: 'x mandatory', scrollbarWidth: 'none' }}
            >
                {children}
            </div>
        </section>
    );
}
