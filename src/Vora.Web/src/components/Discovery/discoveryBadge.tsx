import type { ReactNode } from 'react';
import type { DiscoveryItem } from '../../api/Discovery/discoveryService';
import { LibraryStatusBadge } from '../Client/Primitives/WatchedBadge';
import { discoveryLibraryStatus } from '../../utils/discoveryLibraryStatus';

// The corner box of a Discover tile: where the title stands against the
// libraries (a play triangle or a clock) and, beside it, a bookmark when it is on
// the viewer's watchlist. A title with neither gets no box. This replaces the
// text pills and the separate bookmark the tile used to carry.
export function discoveryBadge(item: Pick<DiscoveryItem, 'inLibrary' | 'requestStatus'>, onWatchlist: boolean): ReactNode {
    const status = discoveryLibraryStatus(item.inLibrary, item.requestStatus);
    return status || onWatchlist ? <LibraryStatusBadge status={status} bookmarked={onWatchlist} /> : undefined;
}
