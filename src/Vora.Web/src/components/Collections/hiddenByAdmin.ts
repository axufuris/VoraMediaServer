import type { CollectionSummary } from '../../api/Collections/collectionService';

// A list of collections the moment an admin hides or shows one, before the
// server's next answer. Hidden by an admin outranks every other reason;
// showing it again leaves any other reason (out of season, below the library's
// minimum) to the list fetched next.
export const withHiddenByAdmin = (collections: CollectionSummary[], id: string, hidden: boolean): CollectionSummary[] =>
    collections.map(c => c.id === id ? { ...c, hiddenReason: hidden ? 'Hidden' : null } : c);
