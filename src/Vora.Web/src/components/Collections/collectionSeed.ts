import type { CollectionDetails, CollectionSortOrder } from '../../api/Collections/collectionService';
import { emptyDefinition, type SmartPlaylistDefinition } from '../../api/Music/smartPlaylistService';
import { pruneRules, savedSmartMedia, smartMediaForLibrary, type SmartCollectionMedia } from './smartCollectionMedia';
import { seasonalWindowFrom, type SeasonalWindow } from './seasonalWindow';

export const SMART_CONTENT = '__smart__';

export const defaultSmartDefinition = (): SmartPlaylistDefinition => ({ ...emptyDefinition(), limit: 2000, sortBy: 'DateAdded', sortDirection: 'Desc' });

export const parseSmartDefinition = (rulesJson?: string): SmartPlaylistDefinition => {
    if (!rulesJson) return defaultSmartDefinition();
    try {
        const parsed = JSON.parse(rulesJson) as SmartPlaylistDefinition;
        return parsed?.root ? parsed : defaultSmartDefinition();
    } catch {
        return defaultSmartDefinition();
    }
};

export interface CollectionSeed {
    title: string;
    description: string;
    sortTitle: string;
    posterUrl: string;
    posterChosen: boolean;
    backdropUrl: string;
    defaultSort: CollectionSortOrder;
    sortProviderId: string;
    externalListId: string;
    autoSyncChronology: boolean;
    season: SeasonalWindow;
    contentSyncProviderId: string;
    contentSyncExternalId: string;
    syncIntervalDays: number;
    mirrorList: boolean;
    smartDefinition: SmartPlaylistDefinition;
    globalSmartMedia: SmartCollectionMedia;
    lockedFields: string[];
}

export function blankSeed(): CollectionSeed {
    return {
        title: '',
        description: '',
        sortTitle: '',
        posterUrl: '',
        posterChosen: false,
        backdropUrl: '',
        defaultSort: 'ReleaseDateAsc',
        sortProviderId: '',
        externalListId: '',
        autoSyncChronology: false,
        season: seasonalWindowFrom(),
        contentSyncProviderId: '',
        contentSyncExternalId: '',
        syncIntervalDays: 30,
        mirrorList: false,
        smartDefinition: defaultSmartDefinition(),
        globalSmartMedia: 'Mixed',
        lockedFields: [],
    };
}

export function cloneSeed(source: CollectionDetails, isGlobal: boolean, libraryType?: string): CollectionSeed {
    const globalSmartMedia = savedSmartMedia(source.smartMediaType) ?? 'Mixed';
    const media: SmartCollectionMedia = isGlobal ? globalSmartMedia : (smartMediaForLibrary(libraryType) ?? 'Movies');
    const definition = parseSmartDefinition(source.rulesJson);

    return {
        title: source.title,
        description: source.description || '',
        sortTitle: source.sortTitle || '',
        posterUrl: source.posterUrl || '',
        posterChosen: true,
        backdropUrl: source.backdropUrl || '',
        defaultSort: source.defaultSort,
        sortProviderId: source.sortProviderId || '',
        externalListId: source.externalListId || '',
        autoSyncChronology: source.autoSyncChronology || false,
        season: seasonalWindowFrom(source.visibleStartDate, source.visibleEndDate, source.visibleEveryYear),
        contentSyncProviderId: source.rulesJson ? SMART_CONTENT : (source.contentSyncProviderId || ''),
        contentSyncExternalId: source.contentSyncExternalId || '',
        syncIntervalDays: source.syncIntervalDays || 30,
        mirrorList: source.mirrorList || false,
        smartDefinition: { ...definition, root: pruneRules(definition.root, media) },
        globalSmartMedia,
        lockedFields: [...(source.lockedFields || [])],
    };
}
