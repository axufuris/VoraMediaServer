import type { DiscoveryRequestStatus } from '../api/Discovery/discoveryService';
import type { LibraryStatus } from '../components/Client/Primitives/WatchedBadge';

// Where a Discover title stands, as the corner icon says it. In a library, or a
// request already fulfilled, is a title the viewer can play. A request still
// waiting on someone or being fetched is a clock. A denied request promises
// nothing, so it gets no icon; neither does a title nobody has asked for. In a
// library wins over requested. The Android and Roku clients apply the same rule.
export function discoveryLibraryStatus(inLibrary?: boolean | null, requestStatus?: DiscoveryRequestStatus | null): LibraryStatus | undefined {
    if (inLibrary || requestStatus === 'Available') return 'inLibrary';
    if (requestStatus === 'Pending' || requestStatus === 'Approved' || requestStatus === 'Processing') return 'requested';
    return undefined;
}
