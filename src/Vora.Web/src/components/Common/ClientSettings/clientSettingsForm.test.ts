import { describe, expect, it } from 'vitest';
import type { ClientSettingsVM } from '../../../api/Users/profileService';
import {
    Automatic,
    ServerDefault,
    clientSettingsFormFrom,
    clientSettingsRequestFrom,
    playbackFormFrom,
    sortFormFrom,
    sortRequestFrom,
    trackFormFrom,
    trackRequestFrom,
} from './clientSettingsForm';

const settings: ClientSettingsVM = {
    playback: { autoSkipIntro: true, autoSkipCredits: false, autoPlayNext: false, upNextOrder: 'WhereILeftOff', episodeLayout: 'List', minimumCreditsSceneSeconds: 40 },
    tracks: { audioLanguage: 'spa', subtitleLanguage: null, subtitleMode: 'Always', preferHearingImpairedSubtitles: true, serverLanguage: 'eng', serverSubtitleMode: 'Smart' },
    librarySort: { sort: 'DateAdded', descending: true },
};

describe('the client settings form', () => {
    it('is read from the settings and written back as the same settings', () => {
        const request = clientSettingsRequestFrom(clientSettingsFormFrom(settings));

        expect(request.playback).toEqual(settings.playback);
        expect(request.tracks).toEqual({ audioLanguage: 'spa', subtitleLanguage: null, subtitleMode: 'Always', preferHearingImpairedSubtitles: true });
        expect(request.librarySort).toEqual(settings.librarySort);
    });

    it('reads a server too old to send the newer playback fields as the older behaviour', () => {
        const form = playbackFormFrom({ autoSkipIntro: false, autoSkipCredits: false, autoPlayNext: true, minimumCreditsSceneSeconds: 15 });

        expect(form.upNextOrder).toBe('InOrder');
        expect(form.episodeLayout).toBe('Row');
    });

    it('shows no track choice as Automatic and sends it as nothing', () => {
        const form = trackFormFrom({ preferHearingImpairedSubtitles: false, serverLanguage: 'eng', serverSubtitleMode: 'Default' });

        expect(form).toEqual({ audioLanguage: Automatic, subtitleLanguage: Automatic, subtitleMode: Automatic, preferSdh: false });
        expect(trackRequestFrom(form)).toEqual({ audioLanguage: null, subtitleLanguage: null, subtitleMode: null, preferHearingImpairedSubtitles: false });
    });

    it('shows no sort as the server default, and never sends a direction for it', () => {
        const form = sortFormFrom({ sort: null, descending: true });

        expect(form).toEqual({ sortBy: ServerDefault, descending: false });
        expect(sortRequestFrom({ sortBy: ServerDefault, descending: true })).toEqual({ sort: null, descending: false });
    });

    it('turns a sort field into its key and back', () => {
        expect(sortFormFrom({ sort: 'ReleaseDate', descending: false })).toEqual({ sortBy: 'releaseDate', descending: false });
        expect(sortRequestFrom({ sortBy: 'releaseDate', descending: true })).toEqual({ sort: 'ReleaseDate', descending: true });
    });
});
