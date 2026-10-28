export const SelectedEpisodeParam = 'episode';

export interface MediaPathTarget {
    id: string;
    type?: string;
    seasonId?: string | null;
}

// Where a title opens. An episode has no page of its own: it opens its season
// with that episode selected, as the TV apps do, so the season is where an
// episode is browsed, played and rated.
// A show with one season opens on that season the same way. Opening the show
// with this state (the season's Show pill does) keeps the show's own page.
export interface MediaPageState {
    showPage?: boolean;
}

export const ShowPageState: MediaPageState = { showPage: true };

export function mediaPath(target: MediaPathTarget, serverId?: string): string {
    const base = serverId ? `/server/${serverId}` : '';
    if (target.type === 'Episode' && target.seasonId) {
        return `${base}/media/${target.seasonId}?${SelectedEpisodeParam}=${encodeURIComponent(target.id)}`;
    }
    return `${base}/media/${target.id}`;
}
