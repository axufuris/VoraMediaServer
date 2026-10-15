import type { LibraryMigrationUserStatusVM } from '../api/LibraryMigration/libraryMigrationService';

type ImportCounts = Pick<LibraryMigrationUserStatusVM,
    'watchStatesFetched' | 'watchStatesImported' | 'watchStatesMerged' | 'ratingsFetched' | 'ratingsImported' | 'ratingsMerged'>;

const number = (value: number) => value.toLocaleString('en-US');

function kindSummary(label: string, fetched: number, imported: number, merged: number): string | null {
    if (fetched === 0) return null;
    const missing = Math.max(0, fetched - imported - merged);
    return [
        `${label}: ${number(imported)} ${imported === 1 ? 'title' : 'titles'} from ${number(fetched)} Plex ${fetched === 1 ? 'entry' : 'entries'}`,
        merged > 0 ? `${number(merged)} shared a title` : null,
        missing > 0 ? `${number(missing)} not found in Vora` : 'none missing',
    ].filter(Boolean).join(', ');
}

// Entries that land on a title another entry also matched (both halves of a
// multi-episode file, the same film in two Plex libraries) were imported onto
// it, so they are counted apart from the ones Vora has no title for.
export function migrationSummary(counts: ImportCounts, watchLabel = 'Watch states'): string {
    return [
        kindSummary(watchLabel, counts.watchStatesFetched, counts.watchStatesImported, counts.watchStatesMerged),
        kindSummary('Ratings', counts.ratingsFetched, counts.ratingsImported, counts.ratingsMerged),
    ].filter(Boolean).join(' · ');
}

export function sharedTitleNote(counts: ImportCounts): string | null {
    return counts.watchStatesMerged + counts.ratingsMerged > 0
        ? 'Plex entries share a title when they are parts of one multi-episode file, or the same title in two Plex libraries.'
        : null;
}
