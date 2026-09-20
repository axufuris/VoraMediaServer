import { useState } from 'react';
import type { MediaVideo, MediaExtra } from '../../api/Media/mediaService';
import { streamingService } from '../../api/Streaming/streamingService';
import { usePlayer } from '../../contexts/usePlayer';
import MediaRow, { MediaRowItem } from '../Client/Primitives/MediaRow';
import VideoCard from '../Client/Primitives/VideoCard';
import TrailerOverlay from '../Client/Primitives/TrailerOverlay';

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

            <TrailerOverlay
                trailer={playingVideo ? { name: playingVideo.name ?? 'Video', videoKey: playingVideo.videoKey, site: playingVideo.site } : null}
                onClose={() => setPlayingVideo(null)}
            />
        </>
    );
}
