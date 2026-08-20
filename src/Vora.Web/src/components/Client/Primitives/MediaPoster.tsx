import { useEffect, useState, type ReactNode } from 'react';
import { thumbUrl } from '../../../utils/thumbnails';
import MediaPlaceholder from './MediaPlaceholder';

export type PosterVariant = 'standard' | 'xl' | 'actor';

interface MediaPosterProps {
    imageUrl?: string | null;
    title: string;
    subtitle?: string;
    badge?: ReactNode;
    hoverBadge?: ReactNode;
    bottomLeftBadge?: ReactNode;
    progressPercent?: number;
    isPlayed?: boolean;
    onClick?: () => void;
    variant?: PosterVariant;
    width?: number;
    fill?: boolean;
    className?: string;
}

const ASPECT_BY_VARIANT: Record<PosterVariant, string> = {
    standard: '2 / 3',
    xl: '2 / 3',
    actor: '1 / 1',
};

const DEFAULT_WIDTH_BY_VARIANT: Record<PosterVariant, number> = {
    standard: 192,
    xl: 240,
    actor: 96,
};

export default function MediaPoster({ imageUrl, title, subtitle, badge, hoverBadge, bottomLeftBadge, progressPercent, isPlayed, onClick, variant = 'standard', width, fill, className }: MediaPosterProps) {
    const aspect = ASPECT_BY_VARIANT[variant];
    const round = variant === 'actor';
    const widthStyle = fill ? '100%' : (width ?? DEFAULT_WIDTH_BY_VARIANT[variant]);

    // Fall back to the branded placeholder if the image URL is broken (404/etc.),
    // not just when it's absent. Reset when the URL changes (lists reuse the node).
    const [failed, setFailed] = useState(false);
    useEffect(() => { setFailed(false); }, [imageUrl]);
    const showImage = !!imageUrl && !failed;

    const handleKeyDown = onClick
        ? (e: React.KeyboardEvent<HTMLDivElement>) => {
            if (e.key === 'Enter' || e.key === ' ') {
                e.preventDefault();
                onClick();
            }
        }
        : undefined;

    return (
        <div
            role={onClick ? 'button' : undefined}
            tabIndex={onClick ? 0 : undefined}
            onClick={onClick}
            onKeyDown={handleKeyDown}
            className={`group block text-left ${onClick ? 'cursor-pointer' : ''} ${className ?? ''}`}
            style={{ width: widthStyle, background: 'transparent', border: 'none', padding: 0 }}
        >
            <div
                className="relative overflow-hidden border border-[var(--vora-border-subtle)] group-hover:border-[var(--vora-accent-500)]"
                style={{
                    aspectRatio: aspect,
                    borderRadius: round ? '50%' : 'var(--vora-radius-md)',
                    boxShadow: 'var(--vora-shadow-md)',
                    background: 'var(--vora-bg-surface)',
                    transition: 'transform var(--vora-duration-med, 240ms) var(--vora-ease-out), border-color var(--vora-duration-med, 240ms) var(--vora-ease-out)',
                }}
            >
                {showImage ? (
                    <img
                        src={thumbUrl(imageUrl!, 360)}
                        alt={title}
                        loading="lazy"
                        decoding="async"
                        onError={() => setFailed(true)}
                        className="h-full w-full object-cover"
                    />
                ) : (
                    <MediaPlaceholder title={title} variant={round ? 'actor' : 'poster'} />
                )}
                {badge ? (
                    <div className="absolute right-2 top-2">
                        {badge}
                    </div>
                ) : isPlayed ? (
                    <div
                        className="absolute right-2 top-2 rounded-full p-1 backdrop-blur-sm"
                        style={{ background: 'var(--vora-bg-overlay)', border: '1px solid var(--vora-border-subtle)', boxShadow: 'var(--vora-shadow-md)' }}
                    >
                        <svg className="h-5 w-5" style={{ color: 'var(--vora-accent-500)' }} fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={3} d="M5 13l4 4L19 7" /></svg>
                    </div>
                ) : null}
                {hoverBadge && (
                    <div className="absolute right-2 top-2 z-20 opacity-0 transition-opacity group-hover:opacity-100">
                        {hoverBadge}
                    </div>
                )}
                {bottomLeftBadge && (
                    <div className="absolute left-2 bottom-2">
                        {bottomLeftBadge}
                    </div>
                )}
                {progressPercent != null && progressPercent > 0 && (
                    <div className="absolute bottom-2 left-2 right-2 h-1 overflow-hidden rounded-full" style={{ background: 'rgba(255, 255, 255, 0.2)' }}>
                        <div className="h-full" style={{ width: `${Math.min(100, Math.max(0, progressPercent))}%`, background: 'var(--vora-accent-500)' }} />
                    </div>
                )}
            </div>
            {(title || subtitle) && (
                <div className={`${round ? 'mt-2 text-center' : 'mt-2.5'}`}>
                    <div className="truncate text-sm font-medium" style={{ color: 'var(--vora-text-primary)' }}>{title}</div>
                    {subtitle && <div className="mt-0.5 truncate text-xs" style={{ color: 'var(--vora-text-muted)' }}>{subtitle}</div>}
                </div>
            )}
        </div>
    );
}
