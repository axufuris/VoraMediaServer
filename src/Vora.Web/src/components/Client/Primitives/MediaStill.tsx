import { useEffect, useState, type ReactNode } from 'react';
import { thumbUrl } from '../../../utils/thumbnails';
import MediaPlaceholder from './MediaPlaceholder';

interface MediaStillProps {
    imageUrl?: string | null;
    title: string;
    subtitle?: string;
    captionLines?: string[];
    badge?: ReactNode;
    bottomLeftBadge?: ReactNode;
    progressPercent?: number;
    onClick?: () => void;
    width?: number;
    fill?: boolean;
    className?: string;
}

export default function MediaStill({ imageUrl, title, subtitle, captionLines, badge, bottomLeftBadge, progressPercent, onClick, width = 280, fill, className }: MediaStillProps) {
    const widthStyle = fill ? '100%' : width;
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
                className="relative overflow-hidden"
                style={{
                    aspectRatio: '16 / 9',
                    borderRadius: 'var(--vora-radius-md)',
                    boxShadow: 'var(--vora-shadow-md)',
                    background: 'var(--vora-bg-surface)',
                    border: '1px solid var(--vora-border-subtle)',
                    transition: 'transform var(--vora-duration-med, 240ms) var(--vora-ease-out)',
                }}
            >
                {showImage ? (
                    <img src={thumbUrl(imageUrl!, 500, 'still')} alt={title} loading="lazy" decoding="async" onError={() => setFailed(true)} className="h-full w-full object-cover" />
                ) : (
                    <MediaPlaceholder title={title} variant="still" />
                )}
                {badge && <div className="absolute right-2 top-2">{badge}</div>}
                {bottomLeftBadge && <div className="absolute left-2 bottom-2">{bottomLeftBadge}</div>}
                {progressPercent != null && progressPercent > 0 && (
                    <div className="absolute bottom-2 left-2 right-2 h-1 overflow-hidden rounded-full" style={{ background: 'rgba(255, 255, 255, 0.2)' }}>
                        <div className="h-full" style={{ width: `${Math.min(100, Math.max(0, progressPercent))}%`, background: 'var(--vora-accent-500)' }} />
                    </div>
                )}
            </div>
            <div className="mt-2.5">
                <div className="truncate text-sm font-medium" style={{ color: 'var(--vora-text-primary)' }}>{title}</div>
                {captionLines && captionLines.length > 0
                    ? captionLines.map((line, i) => (
                        <div key={i} className="mt-0.5 truncate text-xs" style={{ color: 'var(--vora-text-muted)' }}>{line}</div>
                    ))
                    : subtitle && <div className="mt-0.5 truncate text-xs" style={{ color: 'var(--vora-text-muted)' }}>{subtitle}</div>}
            </div>
        </div>
    );
}
