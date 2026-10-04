import type { IptvChannelVM } from '../api/Iptv/iptvAdminService';
import type { IptvRecordingSessionVM } from '../api/Iptv/dvrService';
import { dvrPlaybackService } from '../api/Iptv/dvrPlaybackService';
import type { LibraryItem } from '../api/Media/libraryService';
import { musicService } from '../api/Music/musicService';
import type { PodcastFeedEpisodeVM } from '../api/Podcasts/podcastService';
import type { PlayableMedia } from '../contexts/usePlayer';
import { audioQualityStore } from './audioQuality';
import { serverVault } from './serverVault';

const resolveServer = (serverId?: string) => (serverId ? serverVault.getServer(serverId) : serverVault.getActiveServer());

export function channelPlayable(channel: IptvChannelVM, nowPlaying: string | null | undefined, serverId?: string): PlayableMedia {
    const isRadio = channel.kind === 'Radio';
    return {
        id: channel.id,
        title: channel.name,
        subtitle: nowPlaying || channel.groupTitle || (isRadio ? 'Live Radio' : 'Live TV'),
        posterUrl: channel.logoUrl,
        streamUrl: channel.streamUrl,
        serverId: resolveServer(serverId)?.id,
        container: 'hls',
        playbackContextType: isRadio ? 'LiveRadio' : 'LiveTv',
    };
}

// The same resume rule as the Podcasts tab: a finished episode starts over,
// and a few seconds in counts as not started.
export function podcastPlayable(episode: PodcastFeedEpisodeVM, serverId?: string): PlayableMedia {
    return {
        id: episode.id,
        title: episode.title,
        subtitle: episode.showTitle,
        posterUrl: episode.artworkUrl || episode.showArtworkUrl,
        streamUrl: episode.audioUrl,
        serverId: resolveServer(serverId)?.id,
        container: 'audio',
        playbackContextType: 'Podcast',
        startPosition: episode.isPlayed ? 0 : (episode.positionSeconds > 5 ? episode.positionSeconds : 0),
    };
}

export function trackPlayable(track: LibraryItem, serverId?: string): PlayableMedia {
    const server = resolveServer(serverId);
    const baseUrl = server?.url || (import.meta.env.VITE_API_BASE_URL as string | undefined)?.replace(/\/api\/?$/, '') || '';
    return {
        id: track.id,
        title: track.title,
        subtitle: track.artist ?? '',
        posterUrl: track.posterUrl,
        streamUrl: musicService.getTrackStreamUrl(track.id, baseUrl, audioQualityStore.get()),
        serverId: server?.id,
        container: 'audio',
        playbackContextType: 'Music',
    };
}

export async function recordingPlayable(recording: IptvRecordingSessionVM, fallbackSubtitle: string, serverId?: string): Promise<PlayableMedia> {
    const server = resolveServer(serverId);
    const { url } = await dvrPlaybackService.playDvrSession(recording.id, server?.id);
    return {
        id: recording.id,
        title: recording.title,
        subtitle: recording.episodeTitle || fallbackSubtitle,
        streamUrl: `${import.meta.env.VITE_API_URL || ''}${url}`,
        serverId: server?.id,
        container: 'mp4',
        playbackContextType: 'Dvr',
        commercialMarkers: recording.commercialMarkersJson ? JSON.parse(recording.commercialMarkersJson) : [],
    };
}
