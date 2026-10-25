import React, { useState, type ReactNode } from 'react';
import { thumbUrl } from '../../../utils/thumbnails';
import { episodeCornerLabel, posterCaption, type PosterCaptionItem } from '../../../utils/posterCaption';
import ArtMosaic from './ArtMosaic';
import MediaPlaceholder from './MediaPlaceholder';
import PosterRemoveButton from './PosterRemoveButton';
import { CornerChip, PosterCorner, UnplayedCountBadge, WatchedBadge } from './WatchedBadge';
import { secureImageUrl } from '../../../utils/secureImageUrl';
import type { MediaMenuTarget } from '../../Media/ItemMenu/mediaItemMenu';
import { useMediaMenuTrigger } from '../../Media/ItemMenu/useMediaMenuTrigger';

export type MediaCardShape = 'poster' | 'still' | 'square' | 'circle';
export type MediaCardSize = 'xs' | 'sm' | 'md' | 'lg';
export type MediaCardImageFit = 'cover' | 'contain';

export interface MediaCardProps {
    item?: PosterCaptionItem;
    title?: string;
    captionLines?: string[];
    // One more caption line that isn't plain text, e.g. a Last.fm figure.
    captionExtra?: ReactNode;
    imageUrl?: string | null;
    // 'contain' draws the whole image inset on the card's surface, for channel
    // and station logos that a crop would cut in half.
    imageFit?: MediaCardImageFit;
    // Loads imageUrl as-is instead of through the server's resize cache, which
    // only fetches from known artwork hosts. Podcast feeds and IPTV playlists
    // point at any host, so their art would otherwise come back a placeholder.
    uncachedImage?: boolean;
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
    // The library title this card stands for. Right-clicking the card, or
    // holding it on a touch screen, opens that title's menu. Build it with
    // mediaMenuTarget(), which leaves it out for anything that is not a movie,
    // show, season or episode.
    menu?: MediaMenuTarget | null;
    // The one card a page has chosen, such as the episode a season page
    // describes: ringed in the accent and marked as current.
    selected?: boolean;
    // A round play button over the artwork, for a card whose click does
    // something else (selects it). Shown on hover and focus, always on touch.
    onPlay?: () => void;
    playLabel?: string;
    onFocus?: () => void;
    className?: string;
}

const ASPECT: Record<MediaCardShape, string> = {
    poster: '2 / 3',
    still: '16 / 9',
    square: '1 / 1',
    circle: '1 / 1',
};

const WIDTH_VAR: Record<MediaCardSize, string> = {
    xs: 'var(--vora-card-w-xs)',
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

// The single media tile used everywhere in the client: home rails, discover
// rows, library and collection grids, recommendation rows, watchlist, search.
// It draws the artwork plus the caption block underneath. What goes into that
// caption is decided by posterCaption() from the item's type — pass `item` and
// the card captions itself, or pass title/captionLines for non-media tiles.
export default function MediaCard({
    item, title, captionLines, captionExtra, imageUrl, imageFit = 'cover', uncachedImage, mosaicUrls,
    shape = 'poster', size = 'md', fill, onClick,
    isPlayed, unplayedCount, progressPercent, inWatchlist,
    badge, hoverBadge, bottomLeftBadge, onRemove, onDelete, menu, selected, onPlay, playLabel, onFocus, className,
}: MediaCardProps) {
    const menuTrigger = useMediaMenuTrigger(menu);
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

    // Derived from the item rather than passed in, so an episode is labelled
    // the same wherever it is drawn — a Continue Watching rail, a search
    // result, the season list — without every call site remembering to ask.
    const cornerLabel = episodeCornerLabel(item);

    // An episode's chip owns the top-right slot: that is where its check goes,
    // folded in, instead of taking a second circle beside the number.
    const statusBadge = badge ?? (
        cornerLabel ? (
            <CornerChip label={cornerLabel} watched={isPlayed} />
        ) : unplayedCount != null && unplayedCount > 0 ? (
            <UnplayedCountBadge count={unplayedCount} />
        ) : isPlayed ? (
            <WatchedBadge />
        ) : undefined
    );

    return (
        <div
            role={onClick ? 'button' : undefined}
            tabIndex={onClick ? 0 : undefined}
            aria-current={selected ? 'true' : undefined}
            onClick={onClick}
            onKeyDown={handleKeyDown}
            onFocus={onFocus ? (e: React.FocusEvent<HTMLDivElement>) => { if (e.target === e.currentTarget) onFocus(); } : undefined}
            {...menuTrigger}
            className={`group block text-left ${onClick ? 'cursor-pointer' : ''} ${className ?? ''}`}
            style={{ ...menuTrigger?.style, width: fill ? '100%' : WIDTH_VAR[size], background: 'transparent', border: 'none', padding: 0 }}
        >
            <div
                className={`relative isolate overflow-hidden border ${selected ? 'border-[var(--vora-accent-500)]' : 'border-[var(--vora-border-subtle)] group-hover:border-[var(--vora-accent-500)]'}`}
                style={{
                    aspectRatio: ASPECT[shape],
                    borderRadius: round ? '50%' : 'var(--vora-radius-md)',
                    boxShadow: selected ? '0 0 0 2px var(--vora-accent-500), var(--vora-shadow-md)' : 'var(--vora-shadow-md)',
                    background: 'var(--vora-bg-surface)',
                    transition: 'transform var(--vora-duration-med, 240ms) var(--vora-ease-out), border-color var(--vora-duration-med, 240ms) var(--vora-ease-out)',
                }}
            >
                {mosaic ? (
                    <ArtMosaic urls={mosaic} className="opacity-80 transition-opacity group-hover:opacity-100" />
                ) : showImage ? (
                    <img
                        src={uncachedImage ? secureImageUrl(imageUrl, imageFit === 'contain' ? 'logo' : 'poster') : thumbUrl(imageUrl, THUMB_WIDTH[shape], imageFit === 'contain' ? 'logo' : shape === 'still' ? 'still' : undefined)}
                        alt={displayTitle}
                        loading="lazy"
                        decoding="async"
                        onError={() => setFailedUrl(imageUrl ?? null)}
                        className={imageFit === 'contain' ? 'h-full w-full object-contain p-[14%]' : 'h-full w-full object-cover'}
                    />
                ) : (
                    <MediaPlaceholder title={displayTitle} variant={round ? 'actor' : shape === 'still' ? 'still' : 'poster'} />
                )}

                {inWatchlist && (
                    <div
                        className="absolute left-2 top-2 z-10 rounded p-1.5 backdrop-blur-sm"
                        style={{ background: 'color-mix(in srgb, var(--vora-bg-canvas) 72%, transparent)', border: '1px solid var(--vora-border-strong)', color: 'var(--vora-accent-500)' }}
                    >
                        <svg width="14" height="14" viewBox="0 0 20 20" fill="currentColor"><path d="M5 4a2 2 0 012-2h6a2 2 0 012 2v14l-5-2.5L5 18V4z" /></svg>
                    </div>
                )}

                {statusBadge && <PosterCorner landscape={shape === 'still'}>{statusBadge}</PosterCorner>}

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
                        style={{ background: 'var(--vora-danger-500)', color: 'var(--vora-text-primary)', border: '1px solid color-mix(in srgb, var(--vora-text-primary) 85%, transparent)', boxShadow: 'var(--vora-shadow-md)' }}
                    >
                        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2.25}>
                            <polyline points="3 6 5 6 21 6" />
                            <path d="M19 6l-1 14a2 2 0 01-2 2H8a2 2 0 01-2-2L5 6" />
                            <line x1="10" y1="11" x2="10" y2="17" />
                            <line x1="14" y1="11" x2="14" y2="17" />
                        </svg>
                    </button>
                )}

                {onPlay && (
                    <div className="absolute inset-0 z-20 flex items-center justify-center opacity-0 transition-opacity group-hover:opacity-100 group-focus-within:opacity-100 [@media(hover:none)]:opacity-100">
                        <button
                            type="button"
                            aria-label={playLabel ?? `Play ${displayTitle}`}
                            title={playLabel ?? `Play ${displayTitle}`}
                            onClick={e => { e.stopPropagation(); onPlay(); }}
                            onKeyDown={e => e.stopPropagation()}
                            className="flex h-11 w-11 cursor-pointer items-center justify-center rounded-full transition-transform hover:scale-105"
                            style={{ background: 'var(--vora-accent-500)', color: 'var(--vora-accent-contrast)', border: 'none', boxShadow: 'var(--vora-shadow-lg)', paddingLeft: 3 }}
                        >
                            <svg width="18" height="18" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><polygon points="5 3 19 12 5 21 5 3" /></svg>
                        </button>
                    </div>
                )}

                {bottomLeftBadge && <div className="absolute bottom-2 left-2 z-10">{bottomLeftBadge}</div>}

                {progressPercent != null && progressPercent > 0 && (
                    <div className="absolute bottom-2 left-2 right-2 z-10 h-1 overflow-hidden rounded-full" style={{ background: 'color-mix(in srgb, var(--vora-text-primary) 20%, transparent)' }}>
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
                    {captionExtra && (
                        <div className="mt-0.5 truncate" style={{ color: 'var(--vora-text-muted)', fontSize: 'var(--vora-card-caption-size)' }}>
                            {captionExtra}
                        </div>
                    )}
                </div>
            )}
        </div>
    );
}
