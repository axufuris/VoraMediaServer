import type { LibrarySortField, LibrarySortVM } from '../api/Users/profileService';
import { getProfileIdFromToken, StorageKeys } from './storageKeys';

export type SortKey =
    | 'title'
    | 'releaseDate'
    | 'dateAdded'
    | 'adminRating'
    | 'myRating'
    | 'criticRating'
    | 'audienceRating'
    | 'contentRating'
    | 'duration'
    | 'resolution';

export type SortDir = 'asc' | 'desc';

export interface LibrarySort {
    sortBy: SortKey;
    sortDir: SortDir;
}

export const SORT_LABELS: Record<SortKey, string> = {
    title: 'Title',
    releaseDate: 'Release Date',
    dateAdded: 'Date Added',
    adminRating: 'Admin Rating',
    myRating: 'My Rating',
    criticRating: 'Critic Rating',
    audienceRating: 'Audience Rating',
    contentRating: 'Content Rating',
    duration: 'Duration',
    resolution: 'Resolution',
};

export const HOME_VIDEO_SORT_LABELS: Record<SortKey, string> = { ...SORT_LABELS, releaseDate: 'Date' };

export const SORT_KEYS = Object.keys(SORT_LABELS) as SortKey[];

const FIELDS: Record<SortKey, LibrarySortField> = {
    title: 'Title',
    releaseDate: 'ReleaseDate',
    dateAdded: 'DateAdded',
    adminRating: 'AdminRating',
    myRating: 'MyRating',
    criticRating: 'CriticRating',
    audienceRating: 'AudienceRating',
    contentRating: 'ContentRating',
    duration: 'Duration',
    resolution: 'Resolution',
};

export const fieldOfSortKey = (key: SortKey): LibrarySortField => FIELDS[key];

export function sortKeyOfField(field: LibrarySortField): SortKey | null {
    return SORT_KEYS.find(key => FIELDS[key] === field) ?? null;
}

const isSortKey = (value: unknown): value is SortKey => typeof value === 'string' && value in SORT_LABELS;
const isSortDir = (value: unknown): value is SortDir => value === 'asc' || value === 'desc';

// What a library opens on when neither the profile nor the viewer has chosen:
// home videos by their dates, newest first; everything else by title.
export function serverDefaultSort(libraryType?: string): LibrarySort {
    return libraryType === 'HomeVideo' ? { sortBy: 'releaseDate', sortDir: 'desc' } : { sortBy: 'title', sortDir: 'asc' };
}

export function profileDefaultSort(preference: LibrarySortVM | null, libraryType?: string): LibrarySort {
    const key = preference?.sort ? sortKeyOfField(preference.sort) : null;
    return key ? { sortBy: key, sortDir: preference?.descending ? 'desc' : 'asc' } : serverDefaultSort(libraryType);
}

// A sort picked on a library's page is remembered for that library on this
// device, under the profile default it was picked under. Changing the default
// in Settings therefore moves every library to it.
const rememberedKey = (profileId: string, libraryId: string) => `vora_library_sort_${profileId}_${libraryId}`;
const legacySortKey = (libraryId: string) => `vora_library_sort_${libraryId}`;
const legacyDirKey = (libraryId: string) => `vora_library_sortdir_${libraryId}`;

const defaultSignature = (preference: LibrarySortVM | null) =>
    preference?.sort ? `${preference.sort}:${preference.descending ? 'desc' : 'asc'}` : 'server';

interface RememberedSort extends LibrarySort {
    chosenUnder: string;
}

const isRememberedSort = (value: unknown): value is RememberedSort => {
    if (typeof value !== 'object' || value === null) return false;
    const candidate = value as Partial<RememberedSort>;
    return isSortKey(candidate.sortBy) && isSortDir(candidate.sortDir) && typeof candidate.chosenUnder === 'string';
};

export function activeProfileId(): string {
    try {
        return getProfileIdFromToken(localStorage.getItem(StorageKeys.profileToken)) ?? '';
    } catch {
        return '';
    }
}

function readRememberedSort(profileId: string, libraryId: string, preference: LibrarySortVM | null): LibrarySort | null {
    const signature = defaultSignature(preference);
    try {
        const saved = localStorage.getItem(rememberedKey(profileId, libraryId));
        if (saved) {
            const parsed: unknown = JSON.parse(saved);
            if (!isRememberedSort(parsed) || parsed.chosenUnder !== signature) return null;
            return { sortBy: parsed.sortBy, sortDir: parsed.sortDir };
        }

        if (signature !== 'server') return null;
        const legacySort = localStorage.getItem(legacySortKey(libraryId));
        if (!isSortKey(legacySort)) return null;
        const legacyDir = localStorage.getItem(legacyDirKey(libraryId));
        return { sortBy: legacySort, sortDir: isSortDir(legacyDir) ? legacyDir : 'asc' };
    } catch {
        return null;
    }
}

export function startingLibrarySort(profileId: string, libraryId: string, preference: LibrarySortVM | null, libraryType?: string): LibrarySort {
    return readRememberedSort(profileId, libraryId, preference) ?? profileDefaultSort(preference, libraryType);
}

export function rememberLibrarySort(profileId: string, libraryId: string, sort: LibrarySort, preference: LibrarySortVM | null) {
    try {
        const remembered: RememberedSort = { ...sort, chosenUnder: defaultSignature(preference) };
        localStorage.setItem(rememberedKey(profileId, libraryId), JSON.stringify(remembered));
        localStorage.removeItem(legacySortKey(libraryId));
        localStorage.removeItem(legacyDirKey(libraryId));
    } catch {
        /* storage unavailable: the choice lasts until the page closes */
    }
}
