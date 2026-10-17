import { beforeEach, describe, expect, it } from 'vitest';
import type { LibrarySortVM } from '../api/Users/profileService';
import { fieldOfSortKey, profileDefaultSort, rememberLibrarySort, SORT_KEYS, sortKeyOfField, startingLibrarySort } from './librarySort';

const serverDefault: LibrarySortVM = { sort: null, descending: false };
const newestReleases: LibrarySortVM = { sort: 'ReleaseDate', descending: true };

beforeEach(() => localStorage.clear());

describe('profileDefaultSort', () => {
    it('opens home videos newest first and everything else by title when the profile has no sort', () => {
        expect(profileDefaultSort(serverDefault, 'HomeVideo')).toEqual({ sortBy: 'releaseDate', sortDir: 'desc' });
        expect(profileDefaultSort(serverDefault, 'Movie')).toEqual({ sortBy: 'title', sortDir: 'asc' });
        expect(profileDefaultSort(null, 'TvShow')).toEqual({ sortBy: 'title', sortDir: 'asc' });
    });

    it('uses the profile sort for every kind of library', () => {
        expect(profileDefaultSort(newestReleases, 'Movie')).toEqual({ sortBy: 'releaseDate', sortDir: 'desc' });
        expect(profileDefaultSort({ sort: 'Title', descending: false }, 'HomeVideo')).toEqual({ sortBy: 'title', sortDir: 'asc' });
    });
});

describe('startingLibrarySort', () => {
    it('keeps a sort picked on the library page', () => {
        rememberLibrarySort('profile-a', 'movies', { sortBy: 'dateAdded', sortDir: 'desc' }, newestReleases);

        expect(startingLibrarySort('profile-a', 'movies', newestReleases, 'Movie')).toEqual({ sortBy: 'dateAdded', sortDir: 'desc' });
    });

    it('moves every library to a new profile default', () => {
        rememberLibrarySort('profile-a', 'movies', { sortBy: 'dateAdded', sortDir: 'desc' }, serverDefault);

        expect(startingLibrarySort('profile-a', 'movies', newestReleases, 'Movie')).toEqual({ sortBy: 'releaseDate', sortDir: 'desc' });
    });

    it('keeps one profile\'s pick away from another profile on the same device', () => {
        rememberLibrarySort('profile-a', 'movies', { sortBy: 'duration', sortDir: 'asc' }, serverDefault);

        expect(startingLibrarySort('profile-b', 'movies', serverDefault, 'Movie')).toEqual({ sortBy: 'title', sortDir: 'asc' });
    });

    it('reads a sort saved before profiles had one, until the profile picks a default', () => {
        localStorage.setItem('vora_library_sort_movies', 'criticRating');
        localStorage.setItem('vora_library_sortdir_movies', 'desc');

        expect(startingLibrarySort('profile-a', 'movies', serverDefault, 'Movie')).toEqual({ sortBy: 'criticRating', sortDir: 'desc' });
        expect(startingLibrarySort('profile-a', 'movies', newestReleases, 'Movie')).toEqual({ sortBy: 'releaseDate', sortDir: 'desc' });
    });

    it('drops the old keys once a sort is picked again', () => {
        localStorage.setItem('vora_library_sort_movies', 'criticRating');
        localStorage.setItem('vora_library_sortdir_movies', 'desc');

        rememberLibrarySort('profile-a', 'movies', { sortBy: 'title', sortDir: 'asc' }, serverDefault);

        expect(localStorage.getItem('vora_library_sort_movies')).toBeNull();
        expect(localStorage.getItem('vora_library_sortdir_movies')).toBeNull();
    });

    it('ignores a saved sort it does not recognise', () => {
        localStorage.setItem('vora_library_sort_profile-a_movies', JSON.stringify({ sortBy: 'loudness', sortDir: 'asc', chosenUnder: 'server' }));
        localStorage.setItem('vora_library_sort_shows', 'not-a-sort');

        expect(startingLibrarySort('profile-a', 'movies', serverDefault, 'Movie')).toEqual({ sortBy: 'title', sortDir: 'asc' });
        expect(startingLibrarySort('profile-a', 'shows', serverDefault, 'TvShow')).toEqual({ sortBy: 'title', sortDir: 'asc' });
    });
});

it('maps every sort on the library page to a profile sort and back', () => {
    for (const key of SORT_KEYS) {
        expect(sortKeyOfField(fieldOfSortKey(key))).toBe(key);
    }
});
