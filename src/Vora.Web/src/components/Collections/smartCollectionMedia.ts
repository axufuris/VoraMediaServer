import type { PlaylistMediaType, SmartPlaylistField, SmartPlaylistRuleGroup } from '../../api/Music/smartPlaylistService';

// What a smart collection's rules match. A collection holds whole titles, so
// "Shows" means the shows themselves, never their episodes.
export type SmartCollectionMedia = 'Movies' | 'Shows' | 'Mixed';

export const SMART_MEDIA_OPTIONS: { value: SmartCollectionMedia; label: string }[] = [
    { value: 'Movies', label: 'Movies' },
    { value: 'Shows', label: 'Shows' },
    { value: 'Mixed', label: 'Movies & shows' }
];

export type RuleFieldKind = 'string' | 'int' | 'date' | 'bool';

export interface RuleFieldDef {
    value: SmartPlaylistField;
    label: string;
    kind: RuleFieldKind;
}

// Content-only fields — no profile-scoped ones (watched / my rating / last
// played), since a collection is shared by every profile. Shows match on the
// show itself (first-air year, its genres and ratings): no season or episode
// fields, and no duration, which a show doesn't have.
const TITLE_FIELDS: RuleFieldDef[] = [
    { value: 'Title', label: 'Title', kind: 'string' },
    { value: 'Genre', label: 'Genre', kind: 'string' },
    { value: 'ContentRating', label: 'Content Rating', kind: 'string' },
    { value: 'ReleaseYear', label: 'Year', kind: 'int' },
    { value: 'ServerAdminRating', label: 'Admin Rating', kind: 'int' },
    { value: 'AudienceRating', label: 'Audience Rating', kind: 'int' },
    { value: 'DateAdded', label: 'Date Added', kind: 'date' }
];

const MOVIE_FIELDS: RuleFieldDef[] = [
    ...TITLE_FIELDS.slice(0, 4),
    { value: 'DurationSeconds', label: 'Duration (seconds)', kind: 'int' },
    ...TITLE_FIELDS.slice(4)
];

export function collectionFieldsFor(media: PlaylistMediaType): RuleFieldDef[] {
    return media === 'Shows' || media === 'Mixed' ? TITLE_FIELDS : MOVIE_FIELDS;
}

// A library collection matches its library's kind; undefined for a library
// that holds neither (music, live TV).
export function smartMediaForLibrary(libraryType?: string | null): 'Movies' | 'Shows' | undefined {
    switch ((libraryType ?? '').toLowerCase()) {
        case 'movie':
        case 'homevideo':
            return 'Movies';
        case 'tvshow':
            return 'Shows';
        default:
            return undefined;
    }
}

// The saved kind, as long as it is one a collection can hold.
export function savedSmartMedia(value?: string | null): SmartCollectionMedia | undefined {
    return value === 'Movies' || value === 'Shows' || value === 'Mixed' ? value : undefined;
}

// Drops rules on fields the new kind doesn't offer (Duration when switching
// to shows), so the editor never shows a rule it can't edit.
export function pruneRules(group: SmartPlaylistRuleGroup, media: SmartCollectionMedia): SmartPlaylistRuleGroup {
    const allowed = new Set(collectionFieldsFor(media).map(f => f.value));
    return {
        ...group,
        rules: group.rules.filter(rule => allowed.has(rule.field)),
        groups: group.groups.map(sub => pruneRules(sub, media))
    };
}
