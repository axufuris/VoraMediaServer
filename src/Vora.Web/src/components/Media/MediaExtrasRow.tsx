import { useState } from 'react';
import { createPortal } from 'react-dom';
import type { MediaVideo, MediaExtra } from '../../api/Media/mediaService';
import { streamingService } from '../../api/Streaming/streamingService';
import { usePlayer } from '../../contexts/usePlayer';
import MediaRow, { MediaRowItem } from '../Client/Primitives/MediaRow';
import VideoCard from '../Client/Primitives/VideoCard';

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
            <MediaRow title="Extras" variant="section">
                {extras.map(extra => (
                    <MediaRowItem key={extra.id}>
                        <VideoCard
                            title={extra.title}
                            label={formatExtraType(extra.extraType)}
                            onClick={() => playLocalExtra(extra)}
                        />
                    </MediaRowItem>
                ))}

                {videos.map(video => (
                    <MediaRowItem key={video.videoKey}>
                        <VideoCard
                            title={video.name ?? 'Video'}
                            label={video.type}
                            imageUrl={video.site === 'YouTube' ? `https://img.youtube.com/vi/${video.videoKey}/hqdefault.jpg` : undefined}
                            onClick={() => setPlayingVideo(video)}
                        />
                    </MediaRowItem>
                ))}
            </MediaRow>

            {playingVideo && createPortal(modalContent, document.body)}
        </>
    );
}
