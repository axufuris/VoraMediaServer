import { useEffect, useState } from 'react';
import { musicService, type ArtistTrackVM, type MoodTracksVM } from '../../../../api/Music/musicService';
import { ShuffleIcon } from '../../../../components/Client/Primitives/ActionIcons';
import BrowseTile from './BrowseTile';
import MusicTrackRow from './MusicTrackRow';

const MOOD_PAGE_SIZE = 100;
const MOOD_SHUFFLE_SIZE = 100;

interface MusicMoodViewProps {
    mood: string;
    serverId?: string;
    refreshKey: number;
    isShuffled: boolean;
    toggleShuffle: () => void;
    playArtistTrackList: (tracks: ArtistTrackVM[], startIndex: number) => void;
    formatDuration: (seconds?: number) => string;
    onMissing: () => void;
}

const songCount = (count: number) => `${count.toLocaleString()} ${count === 1 ? 'song' : 'songs'}`;

export default function MusicMoodView({
    mood,
    serverId,
    refreshKey,
    isShuffled,
    toggleShuffle,
    playArtistTrackList,
    formatDuration,
    onMissing,
}: MusicMoodViewProps) {
    const [page, setPage] = useState<MoodTracksVM | null>(null);
    const [tracks, setTracks] = useState<ArtistTrackVM[]>([]);
    const [isLoading, setIsLoading] = useState(true);
    const [isLoadingMore, setIsLoadingMore] = useState(false);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        let cancelled = false;
        queueMicrotask(() => {
            if (cancelled) return;
            setIsLoading(true);
            setError(null);
        });
        musicService.getMoodTracks(mood, 0, MOOD_PAGE_SIZE, serverId)
            .then(result => {
                if (cancelled) return;
                if (!result) {
                    onMissing();
                    return;
                }
                setPage(result);
                setTracks(result.tracks);
            })
            .catch(() => { if (!cancelled) setError('Could not load this mood. Try again in a moment.'); })
            .finally(() => { if (!cancelled) setIsLoading(false); });
        return () => { cancelled = true; };
    }, [mood, serverId, refreshKey, onMissing]);

    const loadMore = async () => {
        setIsLoadingMore(true);
        try {
            const next = await musicService.getMoodTracks(mood, tracks.length, MOOD_PAGE_SIZE, serverId);
            if (next) setTracks(current => [...current, ...next.tracks.filter(t => !current.some(c => c.id === t.id))]);
        } catch {
            setError('Could not load more songs. Try again in a moment.');
        } finally {
            setIsLoadingMore(false);
        }
    };

    const shuffle = async () => {
        try {
            const shuffled = await musicService.getMoodShuffle(mood, MOOD_SHUFFLE_SIZE, serverId);
            if (shuffled.length === 0) return;
            if (!isShuffled) toggleShuffle();
            playArtistTrackList(shuffled, 0);
        } catch {
            setError('Could not shuffle this mood. Try again in a moment.');
        }
    };

    if (isLoading && !page) {
        return <div className="text-[var(--vora-text-muted)] py-12 text-center">Loading mood...</div>;
    }
    if (!page) {
        return <div className="text-[var(--vora-text-muted)] py-12 text-center">{error ?? 'Mood not found.'}</div>;
    }

    return (
        <>
            <div className="flex flex-col sm:flex-row items-center sm:items-end gap-4 sm:gap-6 mb-8 pb-6 border-b border-[var(--vora-border-subtle)] text-center sm:text-left">
                <BrowseTile name={page.name} artworkUrl={page.tracks[0]?.albumArtworkUrl} className="shrink-0 !w-32 sm:!w-40" />
                <div className="flex-1 min-w-0">
                    <div className="text-xs uppercase tracking-widest text-[var(--vora-text-secondary)] font-bold mb-1">Mood</div>
                    <h2 className="text-3xl sm:text-4xl font-bold text-[var(--vora-text-primary)] truncate">{page.name}</h2>
                    <p className="text-sm text-[var(--vora-text-secondary)] mt-2">{songCount(page.totalCount)}, most listened first</p>
                    {tracks.length > 0 && (
                        <div className="flex flex-wrap items-center gap-2 mt-4 justify-center sm:justify-start">
                            <button
                                type="button"
                                onClick={() => {
                                    if (isShuffled) toggleShuffle();
                                    playArtistTrackList(tracks, 0);
                                }}
                                className="text-sm px-4 py-2 bg-[var(--vora-accent-500)] hover:bg-[var(--vora-accent-500)] text-[var(--vora-text-primary)] font-bold rounded transition-colors cursor-pointer flex items-center gap-2"
                            >
                                <svg className="w-4 h-4" fill="currentColor" viewBox="0 0 24 24"><path d="M8 5v14l11-7z" /></svg>
                                Play
                            </button>
                            <button
                                type="button"
                                onClick={shuffle}
                                className="text-sm px-4 py-2 bg-[var(--vora-bg-surface)] hover:bg-[var(--vora-bg-raised)] text-[var(--vora-text-primary)] hover:text-[var(--vora-text-primary)] rounded transition-colors cursor-pointer flex items-center gap-2"
                            >
                                <ShuffleIcon size={16} />
                                Shuffle
                            </button>
                        </div>
                    )}
                </div>
            </div>

            {error && <p role="alert" className="mb-4 text-sm text-[var(--vora-danger-text)]">{error}</p>}

            {tracks.length === 0 ? (
                <div className="text-[var(--vora-text-muted)] py-12 text-center bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] rounded-lg">
                    No songs you can play have this mood.
                </div>
            ) : (
                <div className="space-y-1">
                    {tracks.map((t, idx) => (
                        <MusicTrackRow
                            key={t.id}
                            track={t}
                            position={idx + 1}
                            onPlay={() => playArtistTrackList(tracks, idx)}
                            formatDuration={formatDuration}
                        />
                    ))}
                </div>
            )}

            {tracks.length < page.totalCount && (
                <div className="mt-6 flex justify-center">
                    <button
                        type="button"
                        onClick={loadMore}
                        disabled={isLoadingMore}
                        className="text-sm px-4 py-2 bg-[var(--vora-bg-surface)] hover:bg-[var(--vora-bg-raised)] text-[var(--vora-text-primary)] rounded transition-colors cursor-pointer disabled:cursor-wait disabled:opacity-60"
                    >
                        {isLoadingMore ? 'Loading…' : `Show more (${(page.totalCount - tracks.length).toLocaleString()} left)`}
                    </button>
                </div>
            )}
        </>
    );
}
