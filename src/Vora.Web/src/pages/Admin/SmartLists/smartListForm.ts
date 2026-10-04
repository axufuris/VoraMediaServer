import type {
    CreateSmartListRequest,
    SmartListAdminDto,
    SmartListRulesDto,
    SmartListSortBy,
    SmartListSource,
} from '../../../api/Collections/smartListService';

export interface SmartListForm {
    title: string;
    source: SmartListSource;
    mode: 'rules' | 'collection';
    collectionId: string;
    libraryId: string;
    mediaTypes: string[];
    decade: string;
    unwatchedOnly: boolean;
    days: string;
    sortBy: SmartListSortBy;
    maxItems: number;
    showOnHomepage: boolean;
    showToFriends: boolean;
    seasonal: boolean;
    startMonth: number;
    startDay: number;
    endMonth: number;
    endDay: number;
    // Rules this editor has no controls for (genres, a content rating). Kept so
    // saving a list never quietly drops them.
    preservedRules: SmartListRulesDto;
}

export interface Option<T extends string> {
    value: T;
    label: string;
}

export const SOURCE_OPTIONS: (Option<SmartListSource> & { description: string })[] = [
    { value: 'Library', label: 'Movies & TV', description: 'Titles from your libraries, picked by rules or from a collection.' },
    { value: 'FavoriteChannels', label: 'Favorite Live TV channels', description: "Each profile's favorite channels, with what is on now. Plays straight from the row." },
    { value: 'FavoriteStations', label: 'Favorite radio stations', description: "Each profile's favorite Internet radio stations." },
    { value: 'NewPodcastEpisodes', label: 'New podcast episodes', description: 'The latest episodes from the shows each profile subscribes to.' },
    { value: 'RecentlyAddedMusic', label: 'Music albums', description: 'Albums from your music libraries.' },
    { value: 'RecentRecordings', label: 'Recent DVR recordings', description: "Each profile's finished recordings, newest first." },
];

export const SORT_OPTIONS: Record<SmartListSource, Option<SmartListSortBy>[]> = {
    Library: [
        { value: 'DateAddedDesc', label: 'Recently added' },
        { value: 'ReleaseDateDesc', label: 'Newest released first' },
        { value: 'ReleaseDateAsc', label: 'Oldest released first' },
        { value: 'TitleAsc', label: 'Title A–Z' },
        { value: 'TopRated', label: 'Top rated' },
        { value: 'MostWatched', label: 'Most watched' },
        { value: 'Random', label: 'Random (shuffle)' },
    ],
    FavoriteChannels: [
        { value: 'TitleAsc', label: 'Name A–Z' },
        { value: 'DateAddedDesc', label: 'Recently favorited' },
        { value: 'Random', label: 'Random (shuffle)' },
    ],
    FavoriteStations: [
        { value: 'TitleAsc', label: 'Name A–Z' },
        { value: 'DateAddedDesc', label: 'Recently favorited' },
        { value: 'Random', label: 'Random (shuffle)' },
    ],
    NewPodcastEpisodes: [{ value: 'DateAddedDesc', label: 'Newest first' }],
    RecentlyAddedMusic: [
        { value: 'DateAddedDesc', label: 'Recently added' },
        { value: 'ReleaseDateDesc', label: 'Newest releases' },
        { value: 'TopRated', label: 'Most popular' },
        { value: 'TitleAsc', label: 'Artist A–Z' },
    ],
    RecentRecordings: [{ value: 'DateAddedDesc', label: 'Newest first' }],
};

export const MEDIA_TYPES: Option<string>[] = [
    { value: 'Movie', label: 'Movies' },
    { value: 'TvShow', label: 'TV shows' },
    { value: 'Season', label: 'Seasons' },
    { value: 'Episode', label: 'Episodes' },
    { value: 'Track', label: 'Songs' },
];

export const DECADES = [2020, 2010, 2000, 1990, 1980, 1970, 1960, 1950];

export const MONTHS = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];

// 2000 is a leap year, so February offers the 29th; the server checks the same way.
export const daysInMonth = (month: number): number => new Date(2000, month, 0).getDate();

export const MIN_ITEMS = 1;
export const MAX_ITEMS = 100;

export const clampItems = (value: number): number =>
    Number.isFinite(value) ? Math.min(MAX_ITEMS, Math.max(MIN_ITEMS, Math.round(value))) : 20;

export function sortOptionsFor(source: SmartListSource): Option<SmartListSortBy>[] {
    return SORT_OPTIONS[source];
}

// A source change keeps the sort when the new source offers it.
export function withSource(form: SmartListForm, source: SmartListSource): SmartListForm {
    const options = sortOptionsFor(source);
    const sortBy = options.some(o => o.value === form.sortBy) ? form.sortBy : options[0].value;
    return { ...form, source, sortBy, libraryId: source === 'Library' || source === 'RecentlyAddedMusic' ? form.libraryId : '' };
}

export function emptyForm(): SmartListForm {
    return {
        title: '',
        source: 'Library',
        mode: 'rules',
        collectionId: '',
        libraryId: '',
        mediaTypes: [],
        decade: '',
        unwatchedOnly: false,
        days: '',
        sortBy: 'DateAddedDesc',
        maxItems: 20,
        showOnHomepage: true,
        showToFriends: true,
        seasonal: false,
        startMonth: 12,
        startDay: 1,
        endMonth: 12,
        endDay: 31,
        preservedRules: {},
    };
}

export function parseRules(json: string | null | undefined): SmartListRulesDto {
    if (!json) return {};
    try {
        const parsed = JSON.parse(json) as SmartListRulesDto | null;
        return parsed && typeof parsed === 'object' ? parsed : {};
    } catch {
        return {};
    }
}

export function formFromList(list: SmartListAdminDto): SmartListForm {
    const rules = parseRules(list.filterRulesJson);
    const base = emptyForm();
    const hasWindow = list.activeStartMonth != null && list.activeStartDay != null && list.activeEndMonth != null && list.activeEndDay != null;
    const preservedRules: SmartListRulesDto = {};
    if (rules.genreIds && rules.genreIds.length > 0) preservedRules.genreIds = rules.genreIds;
    if (rules.contentRating) preservedRules.contentRating = rules.contentRating;

    return {
        ...base,
        title: list.title,
        source: list.source,
        mode: list.collectionId ? 'collection' : 'rules',
        collectionId: list.collectionId ?? '',
        libraryId: list.libraryId ?? '',
        mediaTypes: rules.mediaTypes ?? [],
        decade: rules.decade ? String(rules.decade) : '',
        unwatchedOnly: rules.unwatchedOnly === true,
        days: rules.days ? String(rules.days) : '',
        sortBy: list.sortBy,
        maxItems: list.maxItems,
        showOnHomepage: list.showOnHomepage,
        showToFriends: list.showToFriends,
        seasonal: hasWindow,
        startMonth: list.activeStartMonth ?? base.startMonth,
        startDay: list.activeStartDay ?? base.startDay,
        endMonth: list.activeEndMonth ?? base.endMonth,
        endDay: list.activeEndDay ?? base.endDay,
        preservedRules,
    };
}

function rulesFor(form: SmartListForm): SmartListRulesDto {
    const days = Number.parseInt(form.days, 10);
    const daysRule = Number.isFinite(days) && days > 0 ? { days } : {};

    switch (form.source) {
        case 'Library': {
            if (form.mode === 'collection') return {};
            const rules: SmartListRulesDto = { ...form.preservedRules };
            if (form.mediaTypes.length > 0) rules.mediaTypes = form.mediaTypes;
            if (form.decade) rules.decade = Number.parseInt(form.decade, 10);
            if (form.unwatchedOnly) rules.unwatchedOnly = true;
            return rules;
        }
        case 'NewPodcastEpisodes':
            return { ...(form.unwatchedOnly ? { unwatchedOnly: true } : {}), ...daysRule };
        case 'RecentRecordings':
            return daysRule;
        default:
            return {};
    }
}

export function requestFromForm(form: SmartListForm, displayOrder: number): CreateSmartListRequest {
    const rules = rulesFor(form);
    const isLibrary = form.source === 'Library';
    const takesLibrary = isLibrary || form.source === 'RecentlyAddedMusic';

    return {
        title: form.title.trim(),
        source: form.source,
        filterRulesJson: Object.keys(rules).length > 0 ? JSON.stringify(rules) : '{}',
        collectionId: isLibrary && form.mode === 'collection' && form.collectionId ? form.collectionId : null,
        libraryId: takesLibrary && form.libraryId && !(isLibrary && form.mode === 'collection') ? form.libraryId : null,
        sortBy: form.sortBy,
        maxItems: clampItems(form.maxItems),
        displayOrder,
        showOnHomepage: form.showOnHomepage,
        showToFriends: form.showToFriends,
        activeStartMonth: form.seasonal ? form.startMonth : null,
        activeStartDay: form.seasonal ? Math.min(form.startDay, daysInMonth(form.startMonth)) : null,
        activeEndMonth: form.seasonal ? form.endMonth : null,
        activeEndDay: form.seasonal ? Math.min(form.endDay, daysInMonth(form.endMonth)) : null,
    };
}

const shortDate = (month: number, day: number) => `${MONTHS[month - 1].slice(0, 3)} ${day}`;

export function describeList(
    list: SmartListAdminDto,
    collectionTitle: (id: string) => string | undefined,
    libraryName: (id: string) => string | undefined,
): string {
    const rules = parseRules(list.filterRulesJson);
    const parts: string[] = [];

    if (list.source === 'Library') {
        if (list.collectionId) {
            parts.push(`Collection: ${collectionTitle(list.collectionId) ?? 'unknown'}`);
        } else {
            const types = (rules.mediaTypes ?? []).map(t => MEDIA_TYPES.find(m => m.value === t)?.label ?? t);
            parts.push(types.length > 0 ? types.join(' & ') : 'Movies & TV');
            if (rules.decade) parts.push(`${rules.decade}s`);
            if (rules.genreIds && rules.genreIds.length > 0) parts.push(`${rules.genreIds.length} genre${rules.genreIds.length === 1 ? '' : 's'}`);
            if (rules.contentRating) parts.push(`Rated ${rules.contentRating}`);
            if (rules.unwatchedOnly) parts.push('Unwatched only');
        }
    } else {
        parts.push(SOURCE_OPTIONS.find(o => o.value === list.source)?.label ?? list.source);
        if (list.source === 'NewPodcastEpisodes' && rules.unwatchedOnly) parts.push('Unplayed only');
        if (rules.days) parts.push(`Last ${rules.days} days`);
    }

    if (list.libraryId) parts.push(libraryName(list.libraryId) ?? 'One library');
    if (list.activeStartMonth && list.activeStartDay && list.activeEndMonth && list.activeEndDay) {
        parts.push(`${shortDate(list.activeStartMonth, list.activeStartDay)} – ${shortDate(list.activeEndMonth, list.activeEndDay)}`);
    }

    return parts.join(' · ');
}
