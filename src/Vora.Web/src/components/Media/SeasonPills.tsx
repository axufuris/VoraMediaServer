import { useEffect, useRef } from 'react';
import type { Season } from '../../api/Media/mediaService';
import { seasonName } from '../../utils/seasonLabel';
import { revealInRow } from '../../utils/revealInRow';

interface SeasonPillsProps {
    seasons: Season[];
    currentId: string;
    onPick: (season: Season) => void;
    // The show the seasons belong to: its pill comes first and opens it.
    showTitle?: string;
    onPickShow?: () => void;
}

// The show and its seasons at the top of a season page, as the TV apps' season
// tabs, so the show or another season is one click away. A long run of seasons
// scrolls rather than wrapping, so the hero keeps its height.
export default function SeasonPills({ seasons, currentId, onPick, showTitle, onPickShow }: SeasonPillsProps) {
    const rowRef = useRef<HTMLElement>(null);

    useEffect(() => {
        const row = rowRef.current;
        const current = row?.querySelector<HTMLElement>('[aria-current="page"]');
        if (row && current) revealInRow(row, current);
    }, [currentId, seasons]);

    return (
        <nav ref={rowRef} aria-label="Seasons" className="flex gap-2 overflow-x-auto pb-1" style={{ scrollbarWidth: 'none' }}>
            {onPickShow && (
                <button
                    type="button"
                    className="vora-pill shrink-0 cursor-pointer whitespace-nowrap rounded-full px-4 py-1.5 text-sm font-semibold"
                    data-active={false}
                    title={showTitle}
                    aria-label={showTitle ? `Show: ${showTitle}` : 'Show'}
                    onClick={onPickShow}
                >
                    Show
                </button>
            )}
            {seasons.map(season => {
                const current = season.id === currentId;
                return (
                    <button
                        key={season.id}
                        type="button"
                        className="vora-pill shrink-0 cursor-pointer whitespace-nowrap rounded-full px-4 py-1.5 text-sm font-semibold"
                        data-active={current}
                        aria-current={current ? 'page' : undefined}
                        onClick={() => { if (!current) onPick(season); }}
                    >
                        {seasonName(season.seasonNumber, season.title)}
                    </button>
                );
            })}
        </nav>
    );
}
