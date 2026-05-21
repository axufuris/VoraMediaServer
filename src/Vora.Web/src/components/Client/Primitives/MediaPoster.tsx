import type { ReactNode } from 'react';

export type PosterVariant = 'standard' | 'xl' | 'actor';

interface MediaPosterProps {
    imageUrl?: string | null;
    title: string;
    subtitle?: string;
    badge?: ReactNode;
    bottomLeftBadge?: ReactNode;
    progressPercent?: number;
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

export default function MediaPoster({ imageUrl, title, subtitle, badge, bottomLeftBadge, progressPercent, onClick, variant = 'standard', width, fill, className }: MediaPosterProps) {
    const aspect = ASPECT_BY_VARIANT[variant];
    const round = variant === 'actor';
    const widthStyle = fill ? '100%' : (width ?? DEFAULT_WIDTH_BY_VARIANT[variant]);

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
                className="relative overflow-hidden transition-transform"
                style={{
                    aspectRatio: aspect,
                    borderRadius: round ? '50%' : 'var(--vora-radius-md)',
                    boxShadow: 'var(--vora-shadow-md)',
                    background: 'var(--vora-bg-surface)',
                    border: '1px solid var(--vora-border-subtle)',
                    transition: 'transform var(--vora-duration-med, 240ms) var(--vora-ease-out)',
                }}
            >
                {imageUrl ? (
                    <img
                        src={imageUrl}
                        alt={title}
                        loading="lazy"
                        className="h-full w-full object-cover"
                    />
                ) : (
                    <div className="flex h-full w-full items-center justify-center" style={{ color: 'var(--vora-text-muted)' }}>
                        <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5">
                            <rect x="3" y="3" width="18" height="18" rx="2" />
                            <circle cx="9" cy="9" r="2" />
                            <path d="m21 15-5-5L5 21" />
                        </svg>
                    </div>
                )}
                {badge && (
                    <div className="absolute right-2 top-2">
                        {badge}
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
