import React, { useState, type ReactNode } from 'react';
import { thumbUrl } from '../../../utils/thumbnails';
import { posterCaption, type PosterCaptionItem } from '../../../utils/posterCaption';
import MediaPlaceholder from './MediaPlaceholder';
import PosterRemoveButton from './PosterRemoveButton';

export type MediaCardShape = 'poster' | 'still' | 'square' | 'circle';
export type MediaCardSize = 'sm' | 'md' | 'lg';

export interface MediaCardProps {
    item?: PosterCaptionItem;
    title?: string;
    captionLines?: string[];
    imageUrl?: string | null;
    mosaicUrls?: string[];
    shape?: MediaCardShape;
    size?: MediaCardSize;
    fill?: boolean;
    onClick?: () => void;
    isPlayed?: boolean;
    unplayedCount?: number;
    progressPercent?: number;
    inWatchlist?: boolean;
    badge?: ReactNode;
    hoverBadge?: ReactNode;
    bottomLeftBadge?: ReactNode;
    onRemove?: (e: React.MouseEvent) => void;
    onDelete?: (e: React.MouseEvent) => void;
    className?: string;
}

const ASPECT: Record<MediaCardShape, string> = {
    poster: '2 / 3',
    still: '16 / 9',
    square: '1 / 1',
    circle: '1 / 1',
};

const WIDTH_VAR: Record<MediaCardSize, string> = {
    sm: 'var(--vora-card-w-sm)',
    md: 'var(--vora-card-w-md)',
    lg: 'var(--vora-card-w-lg)',
};

const THUMB_WIDTH: Record<MediaCardShape, number> = {
    poster: 360,
    still: 500,
    square: 360,
    circle: 240,
};

function Mosaic({ urls }: { urls: string[] }) {
    const tile = 'h-full w-full object-cover';
    if (urls.length >= 4) {
        return (
            <div className="grid h-full w-full grid-cols-2 grid-rows-2 opacity-80 transition-opacity group-hover:opacity-100">
                {urls.slice(0, 4).map((url, i) => <img key={i} src={url} alt="" className={tile} />)}
            </div>
        );
    }
    if (urls.length === 3) {
        return (
            <div className="flex h-full w-full opacity-80 transition-opacity group-hover:opacity-100">
                <img src={urls[0]} alt="" className="h-full w-1/2 object-cover" />
                <div className="flex h-full w-1/2 flex-col">
                    <img src={urls[1]} alt="" className="h-1/2 w-full object-cover" />
                    <img src={urls[2]} alt="" className="h-1/2 w-full object-cover" />
                </div>
            </div>
        );
    }
    return (
        <div className="flex h-full w-full opacity-80 transition-opacity group-hover:opacity-100">
            {urls.slice(0, 2).map((url, i) => <img key={i} src={url} alt="" className="h-full w-1/2 object-cover" />)}
        </div>
    );
}

// The single media tile used everywhere in the client: home rails, discover
// rows, library and collection grids, recommendation rows, watchlist, search.
// It draws the artwork plus the caption block underneath. What goes into that
// caption is decided by posterCaption() from the item's type — pass `item` and
// the card captions itself, or pass title/captionLines for non-media tiles.
export default function MediaCard({
    item, title, captionLines, imageUrl, mosaicUrls,
    shape = 'poster', size = 'md', fill, onClick,
    isPlayed, unplayedCount, progressPercent, inWatchlist,
    badge, hoverBadge, bottomLeftBadge, onRemove, onDelete, className,
}: MediaCardProps) {
    const caption = item ? posterCaption(item) : { title: title ?? '', lines: captionLines ?? [] };
    const lines = captionLines ?? caption.lines;
    const displayTitle = title ?? caption.title;

    // Track which URL failed rather than a bare flag: lists reuse this node as
    // they re-render, so a new URL must get a fresh attempt without an effect.
    const [failedUrl, setFailedUrl] = useState<string | null>(null);

    const mosaic = mosaicUrls && mosaicUrls.length >= 2 ? mosaicUrls : null;
    const showImage = !mosaic && !!imageUrl && failedUrl !== imageUrl;
    const round = shape === 'circle';

    const handleKeyDown = onClick
        ? (e: React.KeyboardEvent<HTMLDivElement>) => {
            if (e.key === 'Enter' || e.key === ' ') {
                e.preventDefault();
                onClick();
            }
        }
        : undefined;

    const statusBadge = badge ?? (
        unplayedCount != null && unplayedCount > 0 ? (
            <span
                className="inline-flex min-w-[1.5rem] items-center justify-center rounded-full px-1.5 py-0.5 font-bold"
                style={{ background: 'var(--vora-accent-500)', color: 'var(--vora-accent-contrast)', fontSize: 'var(--vora-card-badge-size)' }}
            >
                {unplayedCount}
            </span>
        ) : isPlayed ? (
            <span
                className="inline-flex h-6 w-6 items-center justify-center rounded-full backdrop-blur-sm"
                style={{ background: 'rgba(8, 8, 11, 0.72)', border: '1px solid rgba(255, 255, 255, 0.2)', color: 'var(--vora-accent-500)' }}
            >
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={3}><path strokeLinecap="round" strokeLinejoin="round" d="M5 13l4 4L19 7" /></svg>
            </span>
        ) : undefined
    );

    return (
        <div
            role={onClick ? 'button' : undefined}
            tabIndex={onClick ? 0 : undefined}
            onClick={onClick}
            onKeyDown={handleKeyDown}
            className={`group block text-left ${onClick ? 'cursor-pointer' : ''} ${className ?? ''}`}
            style={{ width: fill ? '100%' : WIDTH_VAR[size], background: 'transparent', border: 'none', padding: 0 }}
        >
            <div
                className="relative overflow-hidden border border-[var(--vora-border-subtle)] group-hover:border-[var(--vora-accent-500)]"
                style={{
                    aspectRatio: ASPECT[shape],
                    borderRadius: round ? '50%' : 'var(--vora-radius-md)',
                    boxShadow: 'var(--vora-shadow-md)',
                    background: 'var(--vora-bg-surface)',
                    transition: 'transform var(--vora-duration-med, 240ms) var(--vora-ease-out), border-color var(--vora-duration-med, 240ms) var(--vora-ease-out)',
                }}
            >
                {mosaic ? (
                    <Mosaic urls={mosaic} />
                ) : showImage ? (
                    <img
                        src={thumbUrl(imageUrl!, THUMB_WIDTH[shape], shape === 'still' ? 'still' : undefined)}
                        alt={displayTitle}
                        loading="lazy"
                        decoding="async"
                        onError={() => setFailedUrl(imageUrl ?? null)}
                        className="h-full w-full object-cover"
                    />
                ) : (
                    <MediaPlaceholder title={displayTitle} variant={round ? 'actor' : shape === 'still' ? 'still' : 'poster'} />
                )}

                {inWatchlist && (
                    <div
                        className="absolute left-2 top-2 z-10 rounded p-1.5 backdrop-blur-sm"
                        style={{ background: 'rgba(8, 8, 11, 0.72)', border: '1px solid rgba(255, 255, 255, 0.2)', color: 'var(--vora-accent-500)' }}
                    >
                        <svg width="14" height="14" viewBox="0 0 20 20" fill="currentColor"><path d="M5 4a2 2 0 012-2h6a2 2 0 012 2v14l-5-2.5L5 18V4z" /></svg>
                    </div>
                )}

                {statusBadge && <div className="absolute right-2 top-2 z-10">{statusBadge}</div>}

                {hoverBadge && (
                    <div className="absolute right-2 top-2 z-20 opacity-0 transition-opacity group-hover:opacity-100">
                        {hoverBadge}
                    </div>
                )}

                {onRemove && (
                    <div className="absolute right-2 top-2 z-20 opacity-0 transition-opacity group-hover:opacity-100 group-focus-within:opacity-100">
                        <PosterRemoveButton onClick={onRemove} />
                    </div>
                )}

                {onDelete && (
                    <button
                        type="button"
                        onClick={onDelete}
                        title="Delete"
                        aria-label={`Delete ${displayTitle}`}
                        className="absolute left-2 top-2 z-20 cursor-pointer rounded-md p-1.5 opacity-0 transition-all hover:scale-105 group-hover:opacity-100 group-focus-within:opacity-100"
                        style={{ background: 'var(--vora-danger-500)', color: '#ffffff', border: '1px solid rgba(255, 255, 255, 0.85)', boxShadow: 'var(--vora-shadow-md)' }}
                    >
                        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2.25}>
                            <polyline points="3 6 5 6 21 6" />
                            <path d="M19 6l-1 14a2 2 0 01-2 2H8a2 2 0 01-2-2L5 6" />
                            <line x1="10" y1="11" x2="10" y2="17" />
                            <line x1="14" y1="11" x2="14" y2="17" />
                        </svg>
                    </button>
                )}

                {bottomLeftBadge && <div className="absolute bottom-2 left-2 z-10">{bottomLeftBadge}</div>}

                {progressPercent != null && progressPercent > 0 && (
                    <div className="absolute bottom-2 left-2 right-2 z-10 h-1 overflow-hidden rounded-full" style={{ background: 'rgba(255, 255, 255, 0.2)' }}>
                        <div className="h-full" style={{ width: `${Math.min(100, Math.max(0, progressPercent))}%`, background: 'var(--vora-accent-500)' }} />
                    </div>
                )}
            </div>

            {(displayTitle || lines.length > 0) && (
                <div className={round ? 'mt-2 text-center' : 'mt-2.5'}>
                    <div
                        className="truncate font-medium transition-colors group-hover:text-[var(--vora-accent-text)]"
                        style={{ color: 'var(--vora-text-primary)', fontSize: 'var(--vora-card-title-size)' }}
                        title={displayTitle}
                    >
                        {displayTitle}
                    </div>
                    {lines.map((line, i) => (
                        <div
                            key={i}
                            className="mt-0.5 truncate"
                            style={{ color: 'var(--vora-text-muted)', fontSize: 'var(--vora-card-caption-size)' }}
                        >
                            {line}
                        </div>
                    ))}
                </div>
            )}
        </div>
    );
}
