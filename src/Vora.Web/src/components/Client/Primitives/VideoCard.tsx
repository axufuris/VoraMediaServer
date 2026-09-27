import { useState } from 'react';

export interface VideoCardProps {
    title: string;
    imageUrl?: string | null;
    label?: string | null;
    onClick: () => void;
}

// The single 16:9 tile for trailers, teasers and local extras — used by the
// media details Extras row and the discovery details Trailers row.
export default function VideoCard({ title, imageUrl, label, onClick }: VideoCardProps) {
    const [failedUrl, setFailedUrl] = useState<string | null>(null);
    const showImage = !!imageUrl && failedUrl !== imageUrl;

    return (
        <button
            type="button"
            onClick={onClick}
            className="group flex cursor-pointer flex-col text-left"
            style={{ width: 'var(--vora-video-w)', background: 'transparent', border: 'none', padding: 0 }}
        >
            <div
                className="relative w-full overflow-hidden border border-[var(--vora-border-subtle)] transition-colors group-hover:border-[var(--vora-accent-500)]"
                style={{
                    aspectRatio: '16 / 9',
                    borderRadius: 'var(--vora-radius-md)',
                    boxShadow: 'var(--vora-shadow-md)',
                    background: 'var(--vora-bg-sunken)',
                }}
            >
                {showImage ? (
                    <img
                        src={imageUrl!}
                        alt={title}
                        loading="lazy"
                        decoding="async"
                        onError={() => setFailedUrl(imageUrl ?? null)}
                        className="h-full w-full object-cover transition-transform duration-300 group-hover:scale-[1.03]"
                    />
                ) : (
                    <div className="flex h-full w-full items-center justify-center" style={{ color: 'var(--vora-text-disabled)' }}>
                        <svg width="40" height="40" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.5}>
                            <path strokeLinecap="round" strokeLinejoin="round" d="M7 4v16M17 4v16M3 8h4m10 0h4M3 12h18M3 16h4m10 0h4M4 20h16a1 1 0 001-1V5a1 1 0 00-1-1H4a1 1 0 00-1 1v14a1 1 0 001 1z" />
                        </svg>
                    </div>
                )}
                <div className="absolute inset-0 flex items-center justify-center">
                    <div
                        className="flex h-14 w-14 items-center justify-center rounded-full pl-1 backdrop-blur-sm transition-all group-hover:scale-110"
                        style={{
                            background: 'rgba(250, 250, 250, 0.92)',
                            color: '#111114',
                            border: '1px solid rgba(255, 255, 255, 0.9)',
                            boxShadow: '0 4px 16px rgba(0, 0, 0, 0.55)',
                        }}
                    >
                        <svg width="24" height="24" viewBox="0 0 20 20" fill="currentColor"><path d="M4 4l12 6-12 6z" /></svg>
                    </div>
                </div>
                {label && (
                    <div
                        className="absolute left-2 top-2 rounded px-2 py-1 font-bold uppercase tracking-wider backdrop-blur-md"
                        style={{
                            background: 'rgba(8, 8, 11, 0.78)',
                            color: '#fafafa',
                            border: '1px solid rgba(255, 255, 255, 0.2)',
                            fontSize: 'var(--vora-card-badge-size)',
                            letterSpacing: '0.06em',
                        }}
                    >
                        {label}
                    </div>
                )}
            </div>
            <div
                className="mt-2.5 line-clamp-2 font-semibold transition-colors group-hover:text-[var(--vora-accent-text)]"
                style={{ color: 'var(--vora-text-primary)', fontSize: 'var(--vora-card-title-size)' }}
            >
                {title}
            </div>
        </button>
    );
}
