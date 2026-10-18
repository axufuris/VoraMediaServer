import { useCallback, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { mediaService, type Episode } from '../../api/Media/mediaService';
import StarRating from '../Client/Primitives/StarRating';
import { VoraRatingBadge } from '../Client/Primitives/RatingBadge';
import ArtImage from '../Client/Primitives/ArtImage';
import { PlayIcon } from '../Client/Primitives/ActionIcons';
import { CornerChip, PosterCorner } from '../Client/Primitives/WatchedBadge';
import { episodeCornerLabel } from '../../utils/posterCaption';
import { StorageKeys } from '../../utils/storageKeys';
import { formatDate } from '../../utils/serverTime';
import { mediaMenuTarget } from './ItemMenu/mediaItemMenu';
import MediaMenuArea from './ItemMenu/MediaMenuArea';

interface Props {
    episodes: Episode[];
    serverId?: string;
    onChanged?: () => void;
    // With it, the episode's picture plays it (from where it was left) and its
    // title and overview still open its page.
    onPlay?: (episode: Episode) => void;
}

const isPartWatched = (episode: Episode) => !episode.isPlayed && (episode.resumePositionSeconds ?? 0) > 0;

export default function MediaEpisodesList({ episodes, serverId, onChanged, onPlay }: Props) {
    const navigate = useNavigate();
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

    if (!episodes || episodes.length === 0) return null;

    return (
        <div>
            <h2 className="text-2xl font-bold mb-6 text-[var(--vora-text-primary)] border-b border-[var(--vora-border-subtle)] pb-2">Episodes</h2>
            <div className="space-y-4">
                {episodes.map(ep => {
                    const ratingValue = ep.id in localRatings ? localRatings[ep.id] : (ep.myRating ?? null);

                    return (
                        <MediaMenuArea key={ep.id} target={mediaMenuTarget({ id: ep.id, type: 'Episode' }, serverId, { onChanged })} className="flex flex-col sm:flex-row gap-6 p-4 bg-[var(--vora-bg-sunken)]/50 hover:bg-[var(--vora-bg-sunken)] rounded-lg border border-transparent hover:border-[var(--vora-border-subtle)] transition-colors group">
                            <button
                                type="button"
                                aria-label={onPlay ? `${isPartWatched(ep) ? 'Resume' : 'Play'} ${ep.title}` : `Open ${ep.title}`}
                                onClick={() => onPlay ? onPlay(ep) : navigate(serverId ? `/server/${serverId}/media/${ep.id}` : `/media/${ep.id}`)}
                                className="w-full sm:w-48 shrink-0 relative aspect-video bg-[var(--vora-bg-canvas)] rounded-md overflow-hidden shadow-[var(--vora-shadow-md)] cursor-pointer border-0 p-0 text-left"
                            >
                                <ArtImage src={ep.posterUrl} alt={ep.title} variant="still" imgClassName="w-full h-full object-contain bg-[var(--vora-bg-sunken)]" />
                                <div className="absolute inset-0 flex items-center justify-center bg-[color-mix(in_srgb,var(--vora-bg-canvas)_40%,transparent)] opacity-0 group-hover:opacity-100 group-focus-within:opacity-100 transition-opacity">
                                    {onPlay && (
                                        <span className="flex h-11 w-11 items-center justify-center rounded-full shadow-[var(--vora-shadow-lg)]" style={{ background: 'var(--vora-accent-500)', color: 'var(--vora-accent-contrast)' }}>
                                            <PlayIcon />
                                        </span>
                                    )}
                                </div>

                                <PosterCorner>
                                    <CornerChip
                                        label={episodeCornerLabel({ type: 'Episode', ...ep }) ?? `E${ep.episodeNumber}`}
                                        watched={ep.isPlayed}
                                    />
                                </PosterCorner>

                                {isPartWatched(ep) && ep.durationMinutes ? (
                                    <div data-testid="episode-progress" className="absolute bottom-0 left-0 right-0 h-1 bg-[var(--vora-bg-sunken)] z-10">
                                        <div className="h-full" style={{ width: `${Math.min(100, ((ep.resumePositionSeconds ?? 0) / (ep.durationMinutes * 60)) * 100)}%`, background: 'var(--vora-accent-500)' }}></div>
                                    </div>
                                ) : null}
                            </button>
                            <div className="flex-1 flex flex-col justify-center">
                                <h4
                                    onClick={() => navigate(serverId ? `/server/${serverId}/media/${ep.id}` : `/media/${ep.id}`)}
                                    className="text-lg font-bold text-[var(--vora-text-primary)] cursor-pointer transition-colors"
                                    style={{ color: 'var(--vora-text-primary)' }}
                                >
                                    {ep.endEpisodeNumber && ep.endEpisodeNumber > ep.episodeNumber ? `${ep.episodeNumber}-${ep.endEpisodeNumber}` : ep.episodeNumber}. {ep.title}
                                </h4>
                                <div className="flex flex-wrap items-center gap-x-3 gap-y-1 text-xs font-semibold mt-1 mb-2" style={{ color: 'var(--vora-text-muted)' }}>
                                    {ep.releaseDate && <span>{formatDate(ep.releaseDate)}</span>}
                                    {ep.durationMinutes && <span>{Math.round(ep.durationMinutes)} min</span>}
                                    <div className="flex items-center gap-1.5" onClick={(e) => e.stopPropagation()}>
                                        <StarRating
                                            value={ratingValue}
                                            onChange={(next) => handleRate(ep.id, next)}
                                            size={14}
                                            ariaLabel={`Rate ${ep.title}`}
                                            title={ratingValue != null ? 'Click the same star to clear' : 'Rate this episode'}
                                        />
                                    </div>
                                    {ep.serverAdminRating != null && !isAdmin && (
                                        <span className="inline-flex items-center gap-1.5">
                                            Admin
                                            <VoraRatingBadge value={ep.serverAdminRating} label="Server admin's rating" />
                                        </span>
                                    )}
                                </div>
                                <p
                                    onClick={() => navigate(serverId ? `/server/${serverId}/media/${ep.id}` : `/media/${ep.id}`)}
                                    className="text-sm line-clamp-3 leading-relaxed cursor-pointer"
                                    style={{ color: 'var(--vora-text-secondary)' }}
                                >
                                    {ep.overview || "No overview available for this episode."}
                                </p>
                            </div>
                        </MediaMenuArea>
                    );
                })}
            </div>
        </div>
    );
}