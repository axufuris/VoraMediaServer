import { type ArtistTrackVM } from '../../../../api/Music/musicService';

interface MusicTopViewProps {
    topTracks: ArtistTrackVM[];
    isShuffled: boolean;
    toggleShuffle: () => void;
    playArtistTrackList: (tracks: ArtistTrackVM[], startIndex: number) => void;
    formatDuration: (seconds?: number) => string;
}

export default function MusicTopView({
    topTracks,
    isShuffled,
    toggleShuffle,
    playArtistTrackList,
    formatDuration,
}: MusicTopViewProps) {
    return (
        <>
            <div className="flex flex-col sm:flex-row items-center sm:items-end gap-4 sm:gap-6 mb-8 pb-6 border-b border-[var(--vora-border-subtle)] text-center sm:text-left">
                <div className="w-32 h-32 sm:w-40 sm:h-40 rounded bg-gradient-to-br from-indigo-600 to-cyan-900 border border-cyan-400/30 flex items-center justify-center shrink-0 shadow-lg">
                    <svg className="w-16 h-16 sm:w-20 sm:h-20 text-[var(--vora-text-primary)]" fill="currentColor" viewBox="0 0 24 24"><path d="M12 1l3 6h6l-5 4 2 7-6-4-6 4 2-7-5-4h6z" /></svg>
                </div>
                <div className="flex-1 min-w-0">
                    <div className="text-xs uppercase tracking-widest text-[var(--vora-text-secondary)] font-bold mb-1">Mix</div>
                    <h2 className="text-3xl sm:text-4xl font-bold text-[var(--vora-text-primary)]">Your Top Tracks</h2>
                    <p className="text-sm text-[var(--vora-text-secondary)] mt-2">{topTracks.length} {topTracks.length === 1 ? 'track' : 'tracks'} based on your play history</p>
                </div>
                {topTracks.length > 0 && (
                    <div className="flex flex-wrap items-center gap-2 justify-center sm:justify-end">
                        <button
                            type="button"
                            onClick={() => {
                                if (isShuffled) toggleShuffle();
                                playArtistTrackList(topTracks, 0);
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
                                playArtistTrackList(topTracks, 0);
                            }}
                            className="text-sm px-4 py-2 bg-[var(--vora-bg-surface)] hover:bg-[var(--vora-bg-raised)] text-[var(--vora-text-primary)] hover:text-[var(--vora-text-primary)] rounded transition-colors cursor-pointer flex items-center gap-2"
                        >
                            <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 4l5 5m0 0V5m0 4H5m11-4l5 5m0 0V5m0 4h-4m-2 7l7 7m-7-7l-7 7m14 0v-4m0 4h-4" /></svg>
                            Shuffle
                        </button>
                    </div>
                )}
            </div>

            {topTracks.length === 0 ? (
                <div className="text-[var(--vora-text-muted)] py-12 text-center bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] rounded-lg">
                    <p className="mb-2">No play history yet.</p>
                    <p className="text-xs">Play some music — tracks you listen to past 30 seconds will show up here.</p>
                </div>
            ) : (
                <div className="space-y-1">
                    {topTracks.map((t, idx) => (
                        <div
                            key={t.id}
                            onClick={() => playArtistTrackList(topTracks, idx)}
                            className="w-full text-left flex items-center gap-3 p-2 hover:bg-[var(--vora-bg-sunken)] border border-transparent hover:border-[var(--vora-border-subtle)] rounded transition-all cursor-pointer group"
                        >
                            <div className="w-8 text-right text-sm text-[var(--vora-text-muted)] group-hover:text-[var(--vora-accent-text)] tabular-nums">{idx + 1}</div>
                            <div className="w-10 h-10 rounded bg-[var(--vora-bg-canvas)] border border-[var(--vora-border-subtle)] flex items-center justify-center overflow-hidden shrink-0">
                                {t.albumArtworkUrl
                                    ? <img src={t.albumArtworkUrl} alt="" className="w-full h-full object-cover" />
                                    : <svg className="w-5 h-5 text-[var(--vora-text-disabled)]" fill="currentColor" viewBox="0 0 24 24"><path d="M12 3v10.55c-.59-.34-1.27-.55-2-.55-2.21 0-4 1.79-4 4s1.79 4 4 4 4-1.79 4-4V7h4V3h-6z" /></svg>}
                            </div>
                            <div className="flex-1 min-w-0">
                                <div className="text-sm text-[var(--vora-text-primary)] group-hover:text-[var(--vora-text-primary)] truncate">{t.title}</div>
                                <div className="text-xs text-[var(--vora-text-muted)] truncate">{t.albumTitle ?? ''}</div>
                            </div>
                            <div className="text-xs text-[var(--vora-text-muted)] w-12 text-right">{formatDuration(t.durationSeconds)}</div>
                        </div>
                    ))}
                </div>
            )}
        </>
    );
}
