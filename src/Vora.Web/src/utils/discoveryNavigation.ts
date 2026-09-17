// Where a discovery-sourced tile should open.
//
// Discovery results carry `mediaItemId` when the server recognised the title as
// one it already holds. Ignoring it sends someone to the "not in your library"
// page for something sitting in their library — no Play button, no watch state,
// just an Add to Watchlist for a film they own.
//
// One helper because the rule was already implemented correctly in one place and
// wrongly in three; a shared function is what stops that happening again.
export interface DiscoveryNavigable {
    mediaItemId?: string | null;
    providerId: string;
    type: string;
    externalId: string;
}

export function discoveryTarget(item: DiscoveryNavigable, serverId?: string): string {
    const prefix = serverId ? `/server/${serverId}` : '';

    return item.mediaItemId
        ? `${prefix}/media/${item.mediaItemId}`
        : `${prefix}/discovery/${item.providerId}/${item.type}/${item.externalId}`;
}
