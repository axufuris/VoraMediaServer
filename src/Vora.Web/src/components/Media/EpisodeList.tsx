import { useCallback, useState } from 'react';
import { mediaService, type Episode } from '../../api/Media/mediaService';
import StarRating from '../Client/Primitives/StarRating';
import { VoraRatingBadge } from '../Client/Primitives/RatingBadge';
import MediaCard from '../Client/Primitives/MediaCard';
import { StorageKeys } from '../../utils/storageKeys';
import { formatDate } from '../../utils/serverTime';
import { mediaMenuTarget } from './ItemMenu/mediaItemMenu';
import MediaMenuArea from './ItemMenu/MediaMenuArea';

interface EpisodeListProps {
    episodes: Episode[];
    seasonNumber?: number;
    serverId?: string;
    // The episode the page describes, highlighted. Left out where picking an
    // episode opens its season instead of selecting it here.
    selectedId?: string | null;
    onSelect: (episode: Episode) => void;
    onPlay: (episode: Episode) => void;
    onChanged?: () => void;
}

const isPartWatched = (episode: Episode) => !episode.isPlayed && (episode.resumePositionSeconds ?? 0) > 0;

const progressOf = (episode: Episode) =>
    isPartWatched(episode) && episode.durationMinutes
        ? Math.min(100, ((episode.resumePositionSeconds ?? 0) / (episode.durationMinutes * 60)) * 100)
        : undefined;

const numberOf = (episode: Episode) =>
    episode.endEpisodeNumber && episode.endEpisodeNumber > episode.episodeNumber
        ? `${episode.episodeNumber}-${episode.endEpisodeNumber}`
        : `${episode.episodeNumber}`;

// A season's episodes down the page, each with its still and plot: the layout
// a profile can pick instead of the row. The still plays the episode from where
// it was left, and its title and plot pick it, as a tile in the row does.
export default function EpisodeList({ episodes, seasonNumber, serverId, selectedId, onSelect, onPlay, onChanged }: EpisodeListProps) {
    const [localRatings, setLocalRatings] = useState<Record<string, number | null>>({});
    const isAdmin = typeof window !== 'undefined' && window.localStorage.getItem(StorageKeys.isServerAdmin) === 'true';

    const handleRate = useCallback(async (episodeId: string, next: number | null) => {
        setLocalRatings(prev => ({ ...prev, [episodeId]: next }));
        try {
            await mediaService.setRating(episodeId, next, serverId);
        } catch {
            setLocalRatings(prev => {
                const copy = { ...prev };
                delete copy[episodeId];
                return copy;
            });
        }
    }, [serverId]);

    if (episodes.length === 0) return null;

    return (
        <section>
            <h2 className="m-0 mb-6 border-b pb-2 text-2xl font-bold" style={{ color: 'var(--vora-text-primary)', borderColor: 'var(--vora-border-subtle)' }}>Episodes</h2>
            <div className="flex flex-col gap-4">
                {episodes.map(episode => {
                    const ratingValue = episode.id in localRatings ? localRatings[episode.id] : (episode.myRating ?? null);
                    const selected = !!selectedId && episode.id === selectedId;
                    return (
                        <MediaMenuArea
                            key={episode.id}
                            target={mediaMenuTarget({ id: episode.id, type: 'Episode' }, serverId, { onChanged })}
                            data-episode-id={episode.id}
                            aria-current={selected ? 'true' : undefined}
                            className={`vora-row-interactive flex flex-col gap-6 rounded-lg border p-4 sm:flex-row ${selected ? '' : 'border-transparent'}`}
                            style={selected ? { background: 'var(--vora-accent-soft)', borderColor: 'var(--vora-accent-500)' } : undefined}
                        >
                            <div className="w-full shrink-0 sm:w-[var(--vora-card-w-lg)]">
                                <MediaCard
                                    item={{
                                        type: 'Episode', title: episode.title, seasonNumber, episodeNumber: episode.episodeNumber,
                                        endEpisodeNumber: episode.endEpisodeNumber, withinSeason: true,
                                    }}
                                    title=""
                                    captionLines={[]}
                                    imageUrl={episode.posterUrl}
                                    shape="still"
                                    fill
                                    isPlayed={episode.isPlayed}
                                    progressPercent={progressOf(episode)}
                                    onPlay={() => onPlay(episode)}
                                    playLabel={`${isPartWatched(episode) ? 'Resume' : 'Play'} ${episode.title}`}
                                />
                            </div>
                            <div className="flex min-w-0 flex-1 flex-col justify-center">
                                <button
                                    type="button"
                                    onClick={() => onSelect(episode)}
                                    className="m-0 cursor-pointer border-0 bg-transparent p-0 text-left text-lg font-bold transition-colors hover:text-[var(--vora-accent-text)]"
                                    style={{ color: 'var(--vora-text-primary)' }}
                                >
                                    {numberOf(episode)}. {episode.title}
                                </button>
                                <div className="mb-2 mt-1 flex flex-wrap items-center gap-x-3 gap-y-1 text-xs font-semibold" style={{ color: 'var(--vora-text-muted)' }}>
                                    {episode.releaseDate && <span>{formatDate(episode.releaseDate)}</span>}
                                    {episode.durationMinutes ? <span>{Math.round(episode.durationMinutes)} min</span> : null}
                                    <StarRating
                                        value={ratingValue}
                                        onChange={next => handleRate(episode.id, next)}
                                        size={14}
                                        ariaLabel={`Rate ${episode.title}`}
                                        title={ratingValue != null ? 'Click the same star to clear' : 'Rate this episode'}
                                    />
                                    {episode.serverAdminRating != null && !isAdmin && (
                                        <span className="inline-flex items-center gap-1.5">
                                            Admin
                                            <VoraRatingBadge value={episode.serverAdminRating} label="Server admin's rating" />
                                        </span>
                                    )}
                                </div>
                                <p
                                    onClick={() => onSelect(episode)}
                                    className="m-0 line-clamp-3 cursor-pointer text-sm leading-relaxed"
                                    style={{ color: 'var(--vora-text-secondary)' }}
                                >
                                    {episode.overview || 'No overview available for this episode.'}
                                </p>
                            </div>
                        </MediaMenuArea>
                    );
                })}
            </div>
        </section>
    );
}
