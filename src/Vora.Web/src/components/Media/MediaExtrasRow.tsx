import { useState } from 'react';
import { createPortal } from 'react-dom';
import type { MediaVideo } from '../../api/Media/mediaService';
import MediaRow from '../Common/MediaRow';

interface Props {
    videos: MediaVideo[];
}

export default function MediaExtrasRow({ videos }: Props) {
    const [playingExtra, setPlayingExtra] = useState<MediaVideo | null>(null);

    if (!videos || videos.length === 0) return null;

    const modalContent = playingExtra ? (
        <div className="fixed inset-0 z-[99999] bg-black flex items-center justify-center">
            <button
                onClick={() => setPlayingExtra(null)}
                className="absolute top-6 left-1/2 -translate-x-1/2 px-6 py-2.5 flex items-center gap-2 rounded-full bg-black/70 hover:bg-[var(--vora-bg-sunken)] text-[var(--vora-text-primary)] transition-colors backdrop-blur-md cursor-pointer z-10 border border-white/20 shadow-2xl font-bold tracking-wider text-sm"
                title="Close Video"
            >
                <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2.5} d="M6 18L18 6M6 6l12 12" /></svg>
                CLOSE
            </button>
            <div className="w-full h-full relative flex items-center justify-center">
                <iframe
                    className="w-full h-full border-none bg-black"
                    src={playingExtra.site === 'Vimeo' ? `https://player.vimeo.com/video/${playingExtra.videoKey}?autoplay=1` : `https://www.youtube.com/embed/${playingExtra.videoKey}?autoplay=1`}
                    title={playingExtra.name}
                    allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture; web-share"
                    allowFullScreen
                ></iframe>
            </div>
        </div>
    ) : null;

    return (
        <>
            <MediaRow title="Extras" variant="detail" gap="6">
                {videos.map(video => (
                    <div key={video.videoKey} onClick={() => setPlayingExtra(video)} className="w-72 shrink-0 flex flex-col group cursor-pointer text-left">
                        <div className="aspect-video rounded-md overflow-hidden bg-[var(--vora-bg-canvas)] mb-3 border border-[var(--vora-border-subtle)] group-hover:border-[var(--vora-accent-500)] transition-colors shadow-md relative">
                            <img
                                src={video.site === 'YouTube' ? `https://img.youtube.com/vi/${video.videoKey}/hqdefault.jpg` : ''}
                                alt={video.name}
                                className="w-full h-full object-cover opacity-80 group-hover:opacity-100 transition-opacity"
                            />
                            <div className="absolute inset-0 flex items-center justify-center">
                                <div className="w-12 h-12 rounded-full bg-black/60 group-hover:bg-[var(--vora-accent-500)]/90 flex items-center justify-center pl-1 shadow-lg transition-colors">
                                    <svg className="w-6 h-6 text-[var(--vora-text-primary)]" fill="currentColor" viewBox="0 0 20 20"><path d="M4 4l12 6-12 6z" /></svg>
                                </div>
                            </div>
                            {video.type && (
                                <div className="absolute top-2 left-2 bg-black/80 px-2 py-1 rounded text-[10px] font-bold text-[var(--vora-text-primary)] uppercase tracking-wider">
                                    {video.type}
                                </div>
                            )}
                        </div>
                        <h3 className="font-bold text-[var(--vora-text-secondary)] text-sm line-clamp-2 group-hover:text-[var(--vora-text-primary)] transition-colors">
                            {video.name}
                        </h3>
                    </div>
                ))}
            </MediaRow>

            {playingExtra && createPortal(modalContent, document.body)}
        </>
    );
}
