import { useState } from 'react';
import { createPortal } from 'react-dom';
import type { MediaVideo, MediaExtra } from '../../api/Media/mediaService';
import { streamingService } from '../../api/Streaming/streamingService';
import { usePlayer } from '../../contexts/usePlayer';
import MediaRow from '../Common/MediaRow';

interface Props {
    videos: MediaVideo[];
    extras?: MediaExtra[];
    serverId?: string;
}

function formatExtraType(type: string): string {
    return type.replace(/([a-z])([A-Z])/g, '$1 $2');
}

export default function MediaExtrasRow({ videos, extras = [], serverId }: Props) {
    const { playMedia } = usePlayer();
    const [playingVideo, setPlayingVideo] = useState<MediaVideo | null>(null);

    const hasContent = (videos && videos.length > 0) || (extras && extras.length > 0);
    if (!hasContent) return null;

    const playLocalExtra = async (extra: MediaExtra) => {
        try {
            const session = await streamingService.startExtraSession(extra.id, 0, serverId);
            playMedia({
                id: extra.id,
                title: extra.title,
                streamUrl: session.streamUrl,
                sessionId: session.sessionId,
                serverId,
                strategy: session.strategy,
                videoStrategy: session.videoStrategy,
                audioStrategy: session.audioStrategy,
                videoCodec: session.videoCodec,
                audioCodec: session.audioCodec,
                container: session.container,
                bandwidthKbps: session.bandwidthKbps,
                outputResolution: session.outputResolution,
                outputHdrType: session.outputHdrType,
                videoTrackId: session.videoTrackId,
                audioTrackId: session.audioTrackId,
                subtitleTrackId: session.subtitleTrackId,
                isExtra: true,
            });
        } catch (err) {
            console.error('Failed to start extra playback', err);
        }
    };

    const modalContent = playingVideo ? (
        <div className="fixed inset-0 z-[99999] bg-black flex items-center justify-center">
            <button
                onClick={() => setPlayingVideo(null)}
                className="absolute top-6 left-1/2 -translate-x-1/2 px-6 py-2.5 flex items-center gap-2 rounded-full bg-black/70 hover:bg-[var(--vora-bg-sunken)] text-[var(--vora-text-primary)] transition-colors backdrop-blur-md cursor-pointer z-10 border border-white/20 shadow-2xl font-bold tracking-wider text-sm"
                title="Close Video"
            >
                <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2.5} d="M6 18L18 6M6 6l12 12" /></svg>
                CLOSE
            </button>
            <div className="w-full h-full relative flex items-center justify-center">
                <iframe
                    className="w-full h-full border-none bg-black"
                    src={playingVideo.site === 'Vimeo' ? `https://player.vimeo.com/video/${playingVideo.videoKey}?autoplay=1` : `https://www.youtube.com/embed/${playingVideo.videoKey}?autoplay=1`}
                    title={playingVideo.name}
                    allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture; web-share"
                    allowFullScreen
                ></iframe>
            </div>
        </div>
    ) : null;

    return (
        <>
            <MediaRow title="Extras" variant="detail" gap="6">
                {extras.map(extra => (
                    <button
                        key={extra.id}
                        type="button"
                        onClick={() => playLocalExtra(extra)}
                        className="w-72 shrink-0 flex flex-col group cursor-pointer text-left"
                    >
                        <div className="aspect-video rounded-md overflow-hidden bg-gradient-to-br from-[var(--vora-bg-raised)] to-[var(--vora-bg-canvas)] mb-3 border border-[var(--vora-border-subtle)] group-hover:border-[var(--vora-accent-500)] transition-colors shadow-md relative flex items-center justify-center">
                            <svg className="w-10 h-10 text-[var(--vora-text-muted)] opacity-50" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M7 4v16M17 4v16M3 8h4m10 0h4M3 12h18M3 16h4m10 0h4M4 20h16a1 1 0 001-1V5a1 1 0 00-1-1H4a1 1 0 00-1 1v14a1 1 0 001 1z" /></svg>
                            <div className="absolute inset-0 flex items-center justify-center">
                                <div className="w-12 h-12 rounded-full bg-black/60 group-hover:bg-[var(--vora-accent-500)]/90 flex items-center justify-center pl-1 shadow-lg transition-colors">
                                    <svg className="w-6 h-6 text-[var(--vora-text-primary)]" fill="currentColor" viewBox="0 0 20 20"><path d="M4 4l12 6-12 6z" /></svg>
                                </div>
                            </div>
                            <div className="absolute top-2 left-2 bg-black/80 px-2 py-1 rounded text-[10px] font-bold text-[var(--vora-text-primary)] uppercase tracking-wider">
                                {formatExtraType(extra.extraType)}
                            </div>
                        </div>
                        <h3 className="font-bold text-[var(--vora-text-secondary)] text-sm line-clamp-2 group-hover:text-[var(--vora-text-primary)] transition-colors">
                            {extra.title}
                        </h3>
                    </button>
                ))}

                {videos.map(video => (
                    <div key={video.videoKey} onClick={() => setPlayingVideo(video)} className="w-72 shrink-0 flex flex-col group cursor-pointer text-left">
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

            {playingVideo && createPortal(modalContent, document.body)}
        </>
    );
}
