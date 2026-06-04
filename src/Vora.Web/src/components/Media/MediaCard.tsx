import React from 'react';

export interface MediaCardProps {
    id: string;
    title: string;
    subtitle?: string | number | null;
    imageUrl?: string;
    type?: 'Movie' | 'TvShow' | 'Season' | 'Episode' | 'Collection' | 'Playlist' | string;
    aspectRatio?: 'poster' | 'video' | 'square';
    isPlayed?: boolean;
    unplayedCount?: number;
    inWatchlist?: boolean;
    progressPercent?: number;
    multiPosters?: string[];
    onClick: () => void;
    onDelete?: (e: React.MouseEvent) => void;
    onHide?: (e: React.MouseEvent) => void;
    isAdmin?: boolean;
}

export default function MediaCard({
    title, subtitle, imageUrl, type, aspectRatio = 'poster',
    isPlayed, unplayedCount, progressPercent, multiPosters, inWatchlist,
    onClick, onDelete, onHide, isAdmin
}: MediaCardProps) {

    const aspectClass = aspectRatio === 'video' ? 'aspect-video' : aspectRatio === 'square' ? 'aspect-square' : 'aspect-[2/3]';

    return (
        <div className="flex flex-col group cursor-pointer w-full" onClick={onClick}>
            <div className={`relative ${aspectClass} rounded-md overflow-hidden bg-[var(--vora-bg-sunken)] mb-2 border border-[var(--vora-border-subtle)] group-hover:border-[var(--vora-accent-500)] transition-colors shadow-[var(--vora-shadow-md)]`}>

                {/* Images — Plex-style adaptive mosaic. Never duplicates a tile
                    to fill empty slots: 2 → side-by-side, 3 → 1 large + 2 stacked,
                    4+ → 2×2 grid. Falls through to single imageUrl below for 1
                    item or when multiPosters is empty. */}
                {multiPosters && multiPosters.length >= 4 ? (
                    <div className="grid grid-cols-2 grid-rows-2 w-full h-full opacity-80 group-hover:opacity-100 transition-opacity">
                        <img src={multiPosters[0]} className="w-full h-full object-cover border-r border-b border-[var(--vora-bg-raised)]" alt="" />
                        <img src={multiPosters[1]} className="w-full h-full object-cover border-l border-b border-[var(--vora-bg-raised)]" alt="" />
                        <img src={multiPosters[2]} className="w-full h-full object-cover border-r border-t border-[var(--vora-bg-raised)]" alt="" />
                        <img src={multiPosters[3]} className="w-full h-full object-cover border-l border-t border-[var(--vora-bg-raised)]" alt="" />
                    </div>
                ) : multiPosters && multiPosters.length === 3 ? (
                    <div className="flex w-full h-full opacity-80 group-hover:opacity-100 transition-opacity">
                        <img src={multiPosters[0]} className="w-1/2 h-full object-cover border-r border-[var(--vora-bg-raised)]" alt="" />
                        <div className="flex flex-col w-1/2 h-full">
                            <img src={multiPosters[1]} className="w-full h-1/2 object-cover border-b border-[var(--vora-bg-raised)]" alt="" />
                            <img src={multiPosters[2]} className="w-full h-1/2 object-cover border-t border-[var(--vora-bg-raised)]" alt="" />
                        </div>
                    </div>
                ) : multiPosters && multiPosters.length === 2 ? (
                    <div className="flex w-full h-full opacity-80 group-hover:opacity-100 transition-opacity">
                        <img src={multiPosters[0]} className="w-1/2 h-full object-cover border-r border-[var(--vora-bg-raised)]" alt="" />
                        <img src={multiPosters[1]} className="w-1/2 h-full object-cover border-l border-[var(--vora-bg-raised)]" alt="" />
                    </div>
                ) : imageUrl ? (
                    <img src={imageUrl} alt={title} className={`w-full h-full ${type === 'Episode' ? 'object-contain bg-black' : 'object-cover'} ${type === 'Playlist' ? 'opacity-80 group-hover:opacity-100 transition-opacity' : ''}`} />
                ) : (
                    <div className="w-full h-full flex items-center justify-center text-[var(--vora-text-muted)] text-sm bg-[var(--vora-bg-sunken)]">
                        {type === 'Playlist' ? <svg className="w-12 h-12" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M4 6h16M4 10h16M4 14h16M4 18h16" /></svg> : 'No Image'}
                    </div>
                )}

                {/* Overlays & Badges */}
                {inWatchlist && (
                    <div className="absolute top-2 left-2 bg-[var(--vora-bg-raised)]/90 backdrop-blur-sm rounded p-1.5 shadow-[var(--vora-shadow-md)] border border-[var(--vora-border-subtle)] z-10">
                        <svg className="w-4 h-4 text-[var(--vora-accent-500)]" fill="currentColor" viewBox="0 0 20 20"><path d="M5 4a2 2 0 012-2h6a2 2 0 012 2v14l-5-2.5L5 18V4z" /></svg>
                    </div>
                )}

                {isPlayed && (
                    <div className="absolute top-2 right-2 bg-[var(--vora-bg-overlay)] backdrop-blur-sm rounded-full p-1 shadow-[var(--vora-shadow-md)] border border-[var(--vora-border-subtle)] z-10">
                        <svg className="w-5 h-5 text-[var(--vora-accent-contrast)]" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={3} d="M5 13l4 4L19 7" /></svg>
                    </div>
                )}

                {unplayedCount !== undefined && unplayedCount > 0 && (
                    <div className="absolute top-2 right-2 bg-[var(--vora-accent-500)] rounded-full w-8 h-8 flex items-center justify-center text-[var(--vora-accent-contrast)] font-bold text-sm shadow-[var(--vora-shadow-md)] border border-[var(--vora-accent-hover)] z-10">
                        {unplayedCount}
                    </div>
                )}

                {progressPercent !== undefined && progressPercent > 0 && (
                    <div className="absolute bottom-0 left-0 right-0 h-1.5 bg-[var(--vora-bg-overlay)] backdrop-blur-sm z-10">
                        <div className="h-full bg-[var(--vora-accent-500)] rounded-r-full" style={{ width: `${progressPercent}%` }}></div>
                    </div>
                )}

                {/* Hover Actions */}
                <div className="absolute inset-0 bg-[var(--vora-bg-overlay)] opacity-0 group-hover:opacity-100 transition-opacity flex items-center justify-center pointer-events-none">
                    {(type === 'Collection' || type === 'Playlist') && (
                        <div className="w-12 h-12 rounded-full border-2 border-[var(--vora-accent-contrast)] flex items-center justify-center pl-1">
                            <svg className="w-6 h-6 text-[var(--vora-accent-contrast)]" fill="currentColor" viewBox="0 0 20 20"><path d="M4 4l12 6-12 6z" /></svg>
                        </div>
                    )}
                </div>

                {onHide && (
                    <button onClick={onHide} className="absolute top-2 right-2 bg-[var(--vora-bg-overlay)] hover:bg-[var(--vora-danger-500)] backdrop-blur-sm rounded-full p-1 shadow-[var(--vora-shadow-md)] border border-[var(--vora-border-subtle)] z-20 opacity-0 group-hover:opacity-100 transition-all cursor-pointer" title="Remove">
                        <svg className="w-4 h-4 text-[var(--vora-accent-contrast)]" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" /></svg>
                    </button>
                )}

                {isAdmin && onDelete && (
                    <button onClick={onDelete} className="absolute top-2 right-2 bg-[var(--vora-bg-overlay)] border border-[var(--vora-danger-500)]/50 hover:bg-[var(--vora-danger-500)] text-[var(--vora-danger-text)] hover:text-[var(--vora-accent-contrast)] rounded-md p-1.5 opacity-0 group-hover:opacity-100 transition-all z-20 cursor-pointer shadow-[var(--vora-shadow-md)]" title="Delete">
                        <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" /></svg>
                    </button>
                )}
            </div>

            <h3 className="font-semibold text-sm text-[var(--vora-text-primary)] truncate group-hover:text-[var(--vora-accent-text)] transition-colors">{title}</h3>
            {subtitle && <p className="text-xs text-[var(--vora-text-muted)] font-medium truncate">{subtitle}</p>}
        </div>
    );
}
