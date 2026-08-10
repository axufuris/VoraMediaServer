export interface PosterTitleItem {
    type?: string;
    title: string;
    tvShowTitle?: string | null;
}

/**
 * Caption shown under a poster/card. For a season we prefix the show name so
 * you can tell which show it belongs to (e.g. "Loki: Season 2"), since a
 * season's own title is usually just "Season 1". Everything else keeps its
 * own title.
 */
export function posterTitle(item: PosterTitleItem): string {
    if (item.type === 'Season' && item.tvShowTitle) {
        return `${item.tvShowTitle}: ${item.title}`;
    }
    return item.title;
}
