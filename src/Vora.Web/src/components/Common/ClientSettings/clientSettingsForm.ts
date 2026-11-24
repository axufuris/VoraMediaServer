import type {
    ClientSettingsVM,
    EpisodeLayout,
    LibrarySortVM,
    PlaybackPreferencesVM,
    SaveClientSettingsRequest,
    SaveTrackPreferencesRequest,
    SubtitleMode,
    TrackPreferencesVM,
    UpNextOrder,
} from '../../../api/Users/profileService';
import { fieldOfSortKey, sortKeyOfField, type SortKey } from '../../../utils/librarySort';

export const Automatic = '';
export const ServerDefault = 'server';

export interface PlaybackForm {
    autoSkipIntro: boolean;
    autoSkipCredits: boolean;
    autoPlayNext: boolean;
    upNextOrder: UpNextOrder;
    episodeLayout: EpisodeLayout;
    minimumCreditsSceneSeconds: number;
}

export interface TrackForm {
    audioLanguage: string;
    subtitleLanguage: string;
    subtitleMode: SubtitleMode | typeof Automatic;
    preferSdh: boolean;
}

export interface SortForm {
    sortBy: SortKey | typeof ServerDefault;
    descending: boolean;
}

export interface ClientSettingsForm {
    playback: PlaybackForm;
    tracks: TrackForm;
    sort: SortForm;
}

export const playbackFormFrom = (prefs: PlaybackPreferencesVM): PlaybackForm => ({
    autoSkipIntro: prefs.autoSkipIntro,
    autoSkipCredits: prefs.autoSkipCredits,
    autoPlayNext: prefs.autoPlayNext !== false,
    upNextOrder: prefs.upNextOrder ?? 'InOrder',
    episodeLayout: prefs.episodeLayout ?? 'Row',
    minimumCreditsSceneSeconds: prefs.minimumCreditsSceneSeconds,
});

export const playbackRequestFrom = (form: PlaybackForm): PlaybackPreferencesVM => ({ ...form });

export const trackFormFrom = (preferences: TrackPreferencesVM): TrackForm => ({
    audioLanguage: preferences.audioLanguage ?? Automatic,
    subtitleLanguage: preferences.subtitleLanguage ?? Automatic,
    subtitleMode: preferences.subtitleMode ?? Automatic,
    preferSdh: preferences.preferHearingImpairedSubtitles,
});

export const trackRequestFrom = (form: TrackForm): SaveTrackPreferencesRequest => ({
    audioLanguage: form.audioLanguage === Automatic ? null : form.audioLanguage,
    subtitleLanguage: form.subtitleLanguage === Automatic ? null : form.subtitleLanguage,
    subtitleMode: form.subtitleMode === Automatic ? null : form.subtitleMode,
    preferHearingImpairedSubtitles: form.preferSdh,
});

export const sortFormFrom = (sort: LibrarySortVM): SortForm => {
    const key = sort.sort ? sortKeyOfField(sort.sort) : null;
    return key ? { sortBy: key, descending: sort.descending } : { sortBy: ServerDefault, descending: false };
};

export const sortRequestFrom = (form: SortForm): LibrarySortVM => ({
    sort: form.sortBy === ServerDefault ? null : fieldOfSortKey(form.sortBy),
    descending: form.sortBy !== ServerDefault && form.descending,
});

export const clientSettingsFormFrom = (settings: ClientSettingsVM): ClientSettingsForm => ({
    playback: playbackFormFrom(settings.playback),
    tracks: trackFormFrom(settings.tracks),
    sort: sortFormFrom(settings.librarySort),
});

export const clientSettingsRequestFrom = (form: ClientSettingsForm): SaveClientSettingsRequest => ({
    playback: playbackRequestFrom(form.playback),
    tracks: trackRequestFrom(form.tracks),
    librarySort: sortRequestFrom(form.sort),
});
