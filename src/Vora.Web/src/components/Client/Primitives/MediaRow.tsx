import { useRef, type ReactNode } from 'react';
import SectionHeader from './SectionHeader';

export type MediaRowVariant = 'page' | 'section';

interface MediaRowProps {
    title?: ReactNode;
    subtitle?: ReactNode;
    moreHref?: string;
    onMore?: () => void;
    actions?: ReactNode;
    variant?: MediaRowVariant;
    children: ReactNode;
    className?: string;
}

// The single horizontal rail used by every scrolling row in the client: home
// smart lists, Continue Watching, discover rows, recommendations, seasons, cast
// and extras. `page` sits directly on a page canvas and owns its own gutter;
// `section` sits inside a block that already has padding and gets an underlined
// header instead.
export default function MediaRow({
    title, subtitle, moreHref, onMore, actions,
    variant = 'page', children, className,
}: MediaRowProps) {
    const scrollerRef = useRef<HTMLDivElement | null>(null);

    const scrollByPage = (direction: 1 | -1) => {
        const el = scrollerRef.current;
        if (!el) return;
        el.scrollBy({ left: direction * Math.round(el.clientWidth * 0.85), behavior: 'smooth' });
    };

    const isPage = variant === 'page';
    // A row with nothing to label carries no header at all — including no
    // scroll arrows, which would otherwise float alone above the cards.
    const hasHeader = title !== undefined || subtitle !== undefined || actions !== undefined;

    const rowActions = !hasHeader ? undefined : (
        <>
            <button
                type="button"
                aria-label="Scroll left"
                onClick={() => scrollByPage(-1)}
                className="hidden cursor-pointer items-center justify-center rounded-full p-2 transition-colors hover:bg-white/5 md:inline-flex"
                style={{ color: 'var(--vora-text-muted)' }}
            >
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><polyline points="15 18 9 12 15 6" /></svg>
            </button>
            <button
                type="button"
                aria-label="Scroll right"
                onClick={() => scrollByPage(1)}
                className="hidden cursor-pointer items-center justify-center rounded-full p-2 transition-colors hover:bg-white/5 md:inline-flex"
                style={{ color: 'var(--vora-text-muted)' }}
            >
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><polyline points="9 18 15 12 9 6" /></svg>
            </button>
            {(moreHref || onMore) && (
                <a
                    href={moreHref ?? '#'}
                    onClick={(e) => { if (onMore) { e.preventDefault(); onMore(); } }}
                    className="ml-1 cursor-pointer text-xs font-medium"
                    style={{ color: 'var(--vora-text-muted)' }}
                >
                    View all →
                </a>
            )}
            {actions}
        </>
    );

    return (
        <section
            className={className}
            style={isPage ? { paddingInline: 'var(--vora-row-gutter)' } : undefined}
        >
            <SectionHeader title={title} subtitle={subtitle} actions={rowActions} underline={!isPage} />
            <div
                ref={scrollerRef}
                className="flex overflow-x-auto pb-2"
                style={{ gap: 'var(--vora-card-gap)', scrollSnapType: 'x mandatory', scrollbarWidth: 'none' }}
            >
                {children}
            </div>
        </section>
    );
}

// Every direct child of a MediaRow scroller goes through this so snap alignment
// and shrink behaviour are identical across rows.
export function MediaRowItem({ children }: { children: ReactNode }) {
    return <div style={{ scrollSnapAlign: 'start', flex: 'none' }}>{children}</div>;
}
