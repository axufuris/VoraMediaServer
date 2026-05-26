import { musicService, type ArtistTrackVM } from '../../../../api/Music/musicService';
import { type PlayableMedia } from '../../../../contexts/usePlayer';
import { serverVault } from '../../../../utils/serverVault';
import { audioQualityStore } from '../../../../utils/audioQuality';

interface MusicLikesViewProps {
    likedTracks: ArtistTrackVM[];
    likedCount: number;
    serverId?: string;
    isShuffled: boolean;
    toggleShuffle: () => void;
    playQueue: (items: PlayableMedia[], startIndex: number) => void;
    onUnlike: (trackId: string) => void;
    formatDuration: (seconds?: number) => string;
}

export default function MusicLikesView({
    likedTracks,
    likedCount,
    serverId,
    isShuffled,
    toggleShuffle,
    playQueue,
    onUnlike,
    formatDuration,
}: MusicLikesViewProps) {
    const buildItems = (): PlayableMedia[] => {
        const server = serverId ? serverVault.getServer(serverId) : serverVault.getActiveServer();
        const baseUrl = server?.url || '';
        return likedTracks.map(t => ({
            id: t.id,
            title: t.title,
            subtitle: t.albumTitle ?? 'Liked Songs',
            posterUrl: t.albumArtworkUrl,
            streamUrl: musicService.getTrackStreamUrl(t.id, baseUrl, audioQualityStore.get()),
            serverId: server?.id,
            container: 'audio' as const,
            playbackContextType: 'Music' as const
        }));
    };

    return (
        <>
            <div className="flex flex-col sm:flex-row items-center sm:items-end gap-4 sm:gap-6 mb-8 pb-6 border-b border-[var(--vora-border-subtle)] text-center sm:text-left">
                <div className="w-32 h-32 sm:w-40 sm:h-40 rounded bg-gradient-to-br from-orange-600 to-purple-900 border border-orange-400/30 flex items-center justify-center shrink-0 shadow-lg">
                    <svg className="w-16 h-16 sm:w-20 sm:h-20 text-[var(--vora-text-primary)]" fill="currentColor" viewBox="0 0 24 24"><path d="M12 21.35l-1.45-1.32C5.4 15.36 2 12.28 2 8.5 2 5.42 4.42 3 7.5 3c1.74 0 3.41.81 4.5 2.09C13.09 3.81 14.76 3 16.5 3 19.58 3 22 5.42 22 8.5c0 3.78-3.4 6.86-8.55 11.54L12 21.35z" /></svg>
                </div>
                <div className="flex-1 min-w-0">
                    <div className="text-xs uppercase tracking-widest text-[var(--vora-text-secondary)] font-bold mb-1">Playlist</div>
                    <h2 className="text-3xl sm:text-4xl font-bold text-[var(--vora-text-primary)]">Liked Songs</h2>
                    <p className="text-sm text-[var(--vora-text-secondary)] mt-2">{likedCount} {likedCount === 1 ? 'track' : 'tracks'}</p>
                </div>
                {likedTracks.length > 0 && (
                    <div className="flex flex-wrap items-center gap-2 justify-center sm:justify-end">
                        <button
                            type="button"
                            onClick={() => {
                                if (isShuffled) toggleShuffle();
                                playQueue(buildItems(), 0);
                            }}
                            className="text-sm px-4 py-2 bg-[var(--vora-accent-500)] hover:bg-[var(--vora-accent-500)] text-[var(--vora-text-primary)] font-bold rounded transition-colors cursor-pointer flex items-center gap-2"
                        >
                            <svg className="w-4 h-4" fill="currentColor" viewBox="0 0 24 24"><path d="M8 5v14l11-7z" /></svg>
                            Play
                        </button>
                        <button
                            type="button"
                            onClick={() => {
                                if (!isShuffled) toggleShuffle();
                                playQueue(buildItems(), 0);
                            }}
                            className="text-sm px-4 py-2 bg-[var(--vora-bg-surface)] hover:bg-[var(--vora-bg-raised)] text-[var(--vora-text-primary)] hover:text-[var(--vora-text-primary)] rounded transition-colors cursor-pointer flex items-center gap-2"
                        >
                            <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 4l5 5m0 0V5m0 4H5m11-4l5 5m0 0V5m0 4h-4m-2 7l7 7m-7-7l-7 7m14 0v-4m0 4h-4" /></svg>
                            Shuffle
                        </button>
                    </div>
                )}
            </div>

            {likedTracks.length === 0 ? (
                <div className="text-[var(--vora-text-muted)] py-12 text-center bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] rounded-lg">
                    <p className="mb-2">No liked songs yet.</p>
                    <p className="text-xs">Tap the heart on a track to add it here.</p>
                </div>
            ) : (
                <div className="space-y-1">
                    {likedTracks.map((t, idx) => (
                        <div
                            key={t.id}
                            onClick={() => playQueue(buildItems(), idx)}
                            className="w-full text-left flex items-center gap-3 p-2 hover:bg-[var(--vora-bg-sunken)] border border-transparent hover:border-[var(--vora-border-subtle)] rounded transition-all cursor-pointer group"
                        >
                            <div className="w-10 h-10 rounded bg-[var(--vora-bg-canvas)] border border-[var(--vora-border-subtle)] flex items-center justify-center overflow-hidden shrink-0">
                                {t.albumArtworkUrl
                                    ? <img src={t.albumArtworkUrl} alt="" className="w-full h-full object-cover" />
                                    : <svg className="w-5 h-5 text-[var(--vora-text-disabled)]" fill="currentColor" viewBox="0 0 24 24"><path d="M12 3v10.55c-.59-.34-1.27-.55-2-.55-2.21 0-4 1.79-4 4s1.79 4 4 4 4-1.79 4-4V7h4V3h-6z" /></svg>}
                            </div>
                            <div className="flex-1 min-w-0">
                                <div className="text-sm text-[var(--vora-text-primary)] group-hover:text-[var(--vora-text-primary)] truncate">{t.title}</div>
                                <div className="text-xs text-[var(--vora-text-muted)] truncate">{t.albumTitle ?? ''}</div>
                            </div>
                            <button
                                type="button"
                                onClick={(e) => {
                                    e.stopPropagation();
                                    onUnlike(t.id);
                                }}
                                className="p-1.5 rounded text-[var(--vora-accent-text)] hover:text-[var(--vora-accent-text)] transition-colors cursor-pointer"
                                title="Remove from Liked Songs"
                            >
                                <svg className="w-4 h-4" fill="currentColor" viewBox="0 0 24 24"><path d="M12 21.35l-1.45-1.32C5.4 15.36 2 12.28 2 8.5 2 5.42 4.42 3 7.5 3c1.74 0 3.41.81 4.5 2.09C13.09 3.81 14.76 3 16.5 3 19.58 3 22 5.42 22 8.5c0 3.78-3.4 6.86-8.55 11.54L12 21.35z" /></svg>
                            </button>
                            <div className="text-xs text-[var(--vora-text-muted)] w-12 text-right">{formatDuration(t.durationSeconds)}</div>
                        </div>
                    ))}
                </div>
            )}
        </>
    );
}
