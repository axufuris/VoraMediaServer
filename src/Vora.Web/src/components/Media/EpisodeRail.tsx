import { useEffect, useRef, type KeyboardEvent } from 'react';
import type { Episode } from '../../api/Media/mediaService';
import MediaRow, { MediaRowItem } from '../Client/Primitives/MediaRow';
import MediaCard from '../Client/Primitives/MediaCard';
import { mediaMenuTarget } from './ItemMenu/mediaItemMenu';
import { revealInRow } from '../../utils/revealInRow';

interface EpisodeRailProps {
    episodes: Episode[];
    seasonNumber?: number;
    serverId?: string;
    // The episode the page describes, ringed. Left out where a click opens the
    // episode's season instead of selecting it here.
    selectedId?: string | null;
    onSelect: (episode: Episode) => void;
    // A season page selects on focus too, so Tab and the arrow keys move the
    // selection the way a TV remote moves focus.
    selectOnFocus?: boolean;
    onPlay: (episode: Episode) => void;
    onChanged?: () => void;
}

const isPartWatched = (episode: Episode) => !episode.isPlayed && (episode.resumePositionSeconds ?? 0) > 0;

const tilesIn = (rail: HTMLElement | null) => Array.from(rail?.querySelectorAll<HTMLElement>('[data-episode-id]') ?? []);

const progressOf = (episode: Episode) =>
    isPartWatched(episode) && episode.durationMinutes
        ? Math.min(100, ((episode.resumePositionSeconds ?? 0) / (episode.durationMinutes * 60)) * 100)
        : undefined;

// A season's episodes as a row of stills, as on the TV apps. A click selects
// an episode (or opens it on its season), the round button on it plays it from
// where it was left, and Left and Right step through the row.
export default function EpisodeRail({ episodes, seasonNumber, serverId, selectedId, onSelect, selectOnFocus, onPlay, onChanged }: EpisodeRailProps) {
    const railRef = useRef<HTMLDivElement>(null);

    useEffect(() => {
        if (!selectedId) return;
        const tile = tilesIn(railRef.current).find(candidate => candidate.dataset.episodeId === selectedId);
        const scroller = tile?.closest<HTMLElement>('.overflow-x-auto');
        if (tile && scroller) revealInRow(scroller, tile);
    }, [selectedId, episodes]);

    if (episodes.length === 0) return null;

    const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
        if (event.key !== 'ArrowRight' && event.key !== 'ArrowLeft') return;
        const all = tilesIn(railRef.current);
        const current = all.findIndex(tile => tile.contains(event.target as Node));
        if (current < 0) return;
        const next = all[current + (event.key === 'ArrowRight' ? 1 : -1)];
        if (!next) return;
        event.preventDefault();
        next.querySelector<HTMLElement>('[role="button"]')?.focus();
        const scroller = next.closest<HTMLElement>('.overflow-x-auto');
        if (scroller) revealInRow(scroller, next);
    };

    return (
        <div ref={railRef} onKeyDown={onKeyDown}>
            <MediaRow title="Episodes" variant="section">
                {episodes.map(episode => (
                    <MediaRowItem key={episode.id}>
                        <div data-episode-id={episode.id}>
                            <MediaCard
                                item={{
                                    type: 'Episode', title: episode.title, seasonNumber, episodeNumber: episode.episodeNumber,
                                    endEpisodeNumber: episode.endEpisodeNumber, releaseDate: episode.releaseDate, withinSeason: true,
                                }}
                                imageUrl={episode.posterUrl}
                                shape="still"
                                isPlayed={episode.isPlayed}
                                progressPercent={progressOf(episode)}
                                selected={selectedId ? episode.id === selectedId : undefined}
                                onClick={() => onSelect(episode)}
                                onFocus={selectOnFocus ? () => onSelect(episode) : undefined}
                                onPlay={() => onPlay(episode)}
                                playLabel={`${isPartWatched(episode) ? 'Resume' : 'Play'} ${episode.title}`}
                                menu={mediaMenuTarget({ id: episode.id, type: 'Episode' }, serverId, { onChanged })}
                            />
                        </div>
                    </MediaRowItem>
                ))}
            </MediaRow>
        </div>
    );
}
