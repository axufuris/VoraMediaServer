import { type ReactNode } from 'react';
import type { MediaCardSize } from './MediaCard';
import SectionHeader from './SectionHeader';

const WIDTH_VAR: Record<MediaCardSize, string> = {
    sm: 'var(--vora-card-w-sm)',
    md: 'var(--vora-card-w-md)',
    lg: 'var(--vora-card-w-lg)',
};

// The grid counterpart to MediaRow: same card metrics and the same section
// heading, laid out as a wrapping grid instead of a horizontal rail. Used by
// library, collection, watchlist, search and filmography pages so a poster is
// the same size whether it's in a row or a grid.
export default function MediaGrid({ title, subtitle, actions, size = 'md', children, className }: {
    title?: ReactNode;
    subtitle?: ReactNode;
    actions?: ReactNode;
    size?: MediaCardSize;
    children: ReactNode;
    className?: string;
}) {
    return (
        <section className={className}>
            <SectionHeader title={title} subtitle={subtitle} actions={actions} underline />
            <div
                style={{
                    display: 'grid',
                    gap: 'var(--vora-card-gap)',
                    gridTemplateColumns: `repeat(auto-fill, minmax(var(--vora-card-min-w), ${WIDTH_VAR[size]}))`,
                }}
            >
                {children}
            </div>
        </section>
    );
}
