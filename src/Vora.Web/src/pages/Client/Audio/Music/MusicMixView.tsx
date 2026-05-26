import { type GeneratedMixDetailVM } from '../../../../api/Music/musicService';

interface MusicMixViewProps {
    isLoading: boolean;
    currentMix: GeneratedMixDetailVM | null;
    isShuffled: boolean;
    toggleShuffle: () => void;
    playMixFromIndex: (startIndex: number) => void;
    formatDuration: (seconds?: number) => string;
}

export default function MusicMixView({
    isLoading,
    currentMix,
    isShuffled,
    toggleShuffle,
    playMixFromIndex,
    formatDuration,
}: MusicMixViewProps) {
    if (isLoading) {
        return <div className="text-[var(--vora-text-muted)] py-12 text-center">Loading mix...</div>;
    }
    if (!currentMix) {
        return <div className="text-[var(--vora-text-muted)] py-12 text-center">Mix not found.</div>;
    }

    return (
        <>
            <div className="flex flex-col sm:flex-row items-center sm:items-end gap-4 sm:gap-6 mb-8 pb-6 border-b border-[var(--vora-border-subtle)] text-center sm:text-left">
                <div className="w-32 h-32 sm:w-40 sm:h-40 rounded bg-gradient-to-br from-orange-700 via-purple-900 to-indigo-900 border border-orange-400/30 flex items-center justify-center shrink-0 shadow-lg overflow-hidden relative">
                    {currentMix.artworkUrl
                        ? <img src={currentMix.artworkUrl} alt="" className="w-full h-full object-cover opacity-60" />
                        : null}
                    <div className="absolute inset-0 flex flex-col items-center justify-center text-[var(--vora-text-primary)] drop-shadow-lg">
                        <div className="text-xs uppercase tracking-widest text-orange-300/90 font-bold">Daily Mix {currentMix.slot}</div>
                        <div className="text-lg sm:text-xl font-bold text-center px-2">{currentMix.descriptionTag ?? 'Mix'}</div>
                    </div>
                </div>
                <div className="flex-1 min-w-0">
                    <div className="text-xs uppercase tracking-widest text-[var(--vora-text-secondary)] font-bold mb-1">Made for You</div>
                    <h2 className="text-3xl sm:text-4xl font-bold text-[var(--vora-text-primary)] truncate">{currentMix.name}</h2>
                    <p className="text-sm text-[var(--vora-text-secondary)] mt-2">{currentMix.tracks.length} tracks{currentMix.lastDriftAt ? ` • Updated ${new Date(currentMix.lastDriftAt).toLocaleDateString()}` : ''}</p>
                    {currentMix.tracks.length > 0 && (
                        <div className="flex flex-wrap items-center gap-2 mt-4 justify-center sm:justify-start">
                            <button
                                type="button"
                                onClick={() => {
                                    if (isShuffled) toggleShuffle();
                                    playMixFromIndex(0);
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
                                    playMixFromIndex(0);
                                }}
                                className="text-sm px-4 py-2 bg-[var(--vora-bg-surface)] hover:bg-[var(--vora-bg-raised)] text-[var(--vora-text-primary)] hover:text-[var(--vora-text-primary)] rounded transition-colors cursor-pointer flex items-center gap-2"
                            >
                                <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 4l5 5m0 0V5m0 4H5m11-4l5 5m0 0V5m0 4h-4m-2 7l7 7m-7-7l-7 7m14 0v-4m0 4h-4" /></svg>
                                Shuffle
                            </button>
                        </div>
                    )}
                </div>
            </div>

            {currentMix.tracks.length === 0 ? (
                <div className="text-[var(--vora-text-muted)] py-12 text-center bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] rounded-lg">
                    <p className="mb-2">This mix is empty.</p>
                    <p className="text-xs">It will populate once you build more play history.</p>
                </div>
            ) : (
                <div className="space-y-1">
                    {currentMix.tracks.map((t, idx) => (
                        <div
                            key={t.id}
                            onClick={() => playMixFromIndex(idx)}
                            className="w-full text-left flex items-center gap-3 p-2 hover:bg-[var(--vora-bg-sunken)] border border-transparent hover:border-[var(--vora-border-subtle)] rounded transition-all cursor-pointer group"
                        >
                            <div className="w-8 text-right text-sm text-[var(--vora-text-muted)] group-hover:text-[var(--vora-accent-text)] tabular-nums shrink-0">{idx + 1}</div>
                            <div className="flex-1 min-w-0">
                                <div className="text-sm text-[var(--vora-text-primary)] group-hover:text-[var(--vora-text-primary)] truncate flex items-center gap-2">
                                    <span className="truncate">{t.title}</span>
                                    {t.contentRating && (
                                        <span className="text-[10px] sm:text-xs font-bold uppercase tracking-wide px-1.5 py-0.5 rounded bg-[var(--vora-bg-surface)] text-[var(--vora-text-secondary)] shrink-0">{t.contentRating}</span>
                                    )}
                                </div>
                                {t.artist && <div className="text-xs text-[var(--vora-text-muted)] truncate">{t.artist}</div>}
                            </div>
                            <div className="text-xs text-[var(--vora-text-muted)] shrink-0 tabular-nums">{formatDuration(t.durationSeconds)}</div>
                        </div>
                    ))}
                </div>
            )}
        </>
    );
}
