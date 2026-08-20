import type { MouseEvent } from 'react';

// A dark, outlined circular X used as the hover "remove" affordance on posters
// (Continue Watching, collection items, …). Deliberately a fixed dark scrim with
// a light icon so it stays legible over any artwork and over the accent-colored
// unwatched-count badge, rather than blending into it. Turns danger-red on hover.
export default function PosterRemoveButton({ onClick, title = 'Remove' }: { onClick: (e: MouseEvent) => void; title?: string }) {
    return (
        <button
            type="button"
            aria-label={title}
            title={title}
            onClick={(e) => { e.stopPropagation(); onClick(e); }}
            className="inline-flex h-8 w-8 cursor-pointer items-center justify-center rounded-full transition-all"
            style={{
                background: 'rgba(20, 20, 28, 0.88)',
                color: '#fafafa',
                border: '1.5px solid rgba(255,255,255,0.4)',
                boxShadow: '0 2px 8px rgba(0,0,0,0.5)',
                backdropFilter: 'blur(6px)',
            }}
            onMouseEnter={(e) => {
                e.currentTarget.style.background = 'var(--vora-danger-500)';
                e.currentTarget.style.borderColor = 'rgba(255,255,255,0.7)';
                e.currentTarget.style.transform = 'scale(1.1)';
            }}
            onMouseLeave={(e) => {
                e.currentTarget.style.background = 'rgba(20, 20, 28, 0.88)';
                e.currentTarget.style.borderColor = 'rgba(255,255,255,0.4)';
                e.currentTarget.style.transform = 'scale(1)';
            }}
        >
            <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5">
                <line x1="18" y1="6" x2="6" y2="18" />
                <line x1="6" y1="6" x2="18" y2="18" />
            </svg>
        </button>
    );
}
