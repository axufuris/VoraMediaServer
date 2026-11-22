import { describe, expect, it } from 'vitest';
import type { IptvPlaylistVM } from '../../../api/Iptv/iptvAdminService';
import { describeSource, describeTestResult, draftFromPlaylist, emptySourceDraft, maskUrlCredentials, missingSourceField, sourceRequestFields } from './iptvSourceDraft';

const playlist = (overrides: Partial<IptvPlaylistVM>): IptvPlaylistVM => ({
    id: 'p1',
    name: 'Provider',
    isActive: true,
    supportsWebPlayback: true,
    maxConcurrentStreams: 0,
    defaultChannelKind: 'Tv',
    ...overrides,
});

describe('maskUrlCredentials', () => {
    it.each([
        ['http://viewer:example-password@iptv.example.com/list.m3u', 'http://•••@iptv.example.com/list.m3u'],
        ['http://provider.example.com:8080/get.php?username=viewer&password=example-password&type=m3u_plus', 'http://provider.example.com:8080/get.php?username=•••&password=•••&type=m3u_plus'],
        ['http://provider.example.com/live/viewer/example-password/1001.ts', 'http://provider.example.com/live/•••/•••/1001.ts'],
        ['https://iptv-org.github.io/iptv/countries/us.m3u', 'https://iptv-org.github.io/iptv/countries/us.m3u'],
    ])('masks %s', (url, expected) => {
        expect(maskUrlCredentials(url)).toBe(expected);
    });
});

describe('describeSource', () => {
    it('names an Xtream account by its host and username, never its password', () => {
        expect(describeSource(playlist({ sourceType: 'Xtream', serverUrl: 'http://provider.example.com:8080', username: 'viewer', passwordIsSet: true })))
            .toBe('Xtream Codes · provider.example.com:8080 · viewer');
    });

    it('shows an M3U address with its credentials masked', () => {
        expect(describeSource(playlist({ m3uUrl: 'http://provider.example.com/get.php?username=viewer&password=example-password' })))
            .toBe('http://provider.example.com/get.php?username=•••&password=•••');
    });
});

describe('sourceRequestFields', () => {
    it('sends a new password only when one was typed', () => {
        const draft = { ...emptySourceDraft(), sourceType: 'Xtream' as const, serverUrl: ' provider.example.com:8080 ', username: 'viewer' };

        expect(sourceRequestFields(draft)).toMatchObject({ sourceType: 'Xtream', serverUrl: 'provider.example.com:8080', m3uUrl: '', newPassword: null, clearPassword: false });
        expect(sourceRequestFields({ ...draft, password: { value: 'example-password', clear: false, editing: true } }).newPassword).toBe('example-password');
        expect(sourceRequestFields({ ...draft, password: { value: 'typed', clear: true, editing: false } })).toMatchObject({ newPassword: null, clearPassword: true });
    });

    it('clears the headers when the fields are emptied', () => {
        const draft = draftFromPlaylist(playlist({ m3uUrl: 'https://iptv.example.com/list.m3u', userAgent: 'VLC/3.0', referer: 'https://portal.example.com/' }));

        expect(sourceRequestFields({ ...draft, userAgent: '', referer: ' ' })).toMatchObject({ userAgent: '', referer: '', serverUrl: '' });
    });
});

describe('HDHomeRun source', () => {
    const tuner = { ...emptySourceDraft(), sourceType: 'HdHomeRun' as const, serverUrl: ' 192.168.1.50 ', username: 'left over', userAgent: 'left over', referer: 'left over', password: { value: 'left over', clear: false, editing: true } };

    it('sends only its address, with no login or headers left over from another source', () => {
        expect(sourceRequestFields(tuner)).toEqual({
            sourceType: 'HdHomeRun',
            m3uUrl: '',
            serverUrl: '192.168.1.50',
            username: '',
            newPassword: null,
            clearPassword: true,
            userAgent: '',
            referer: '',
            useProviderGuide: true,
        });
    });

    it('asks for the address and nothing else', () => {
        expect(missingSourceField({ ...tuner, serverUrl: ' ' }, false)).toBe("Enter the HDHomeRun's address.");
        expect(missingSourceField(tuner, false)).toBeNull();
    });

    it('is named by its host', () => {
        expect(describeSource({ sourceType: 'HdHomeRun', serverUrl: 'http://192.168.1.50' } as IptvPlaylistVM)).toBe('HDHomeRun · 192.168.1.50');
    });

    it('sums up a working tuner in tuners, not connections', () => {
        expect(describeTestResult({ succeeded: true, accountStatus: 'HDHomeRun CONNECT (HDHR3-US)', accountMaxConnections: 2, channelCount: 18 }, 'channels', 'HdHomeRun'))
            .toBe('Connected · HDHomeRun CONNECT (HDHR3-US) · 2 tuners · 18 channels');
        expect(describeTestResult({ succeeded: true, accountMaxConnections: 1, channelCount: 1 }, 'channels', 'HdHomeRun'))
            .toBe('Connected · 1 tuner · 1 channels');
    });
});

describe('missingSourceField', () => {
    it('asks for everything an Xtream account needs', () => {
        const draft = { ...emptySourceDraft(), sourceType: 'Xtream' as const };

        expect(missingSourceField(draft, false)).toBe('Enter the server address.');
        expect(missingSourceField({ ...draft, serverUrl: 'provider.example.com' }, false)).toBe('Enter the username.');
        expect(missingSourceField({ ...draft, serverUrl: 'provider.example.com', username: 'viewer' }, false)).toBe('Enter the password.');
        expect(missingSourceField({ ...draft, serverUrl: 'provider.example.com', username: 'viewer' }, true)).toBeNull();
    });

    it('lets an M3U playlist go without a login', () => {
        expect(missingSourceField({ ...emptySourceDraft(), m3uUrl: 'https://iptv.example.com/list.m3u' }, false)).toBeNull();
    });
});

describe('describeTestResult', () => {
    it('sums up a working account', () => {
        expect(describeTestResult({ succeeded: true, accountStatus: 'Active', accountMaxConnections: 2, channelCount: 4512 }, 'channels'))
            .toBe('Connected · account active · 2 connections · 4,512 channels');
    });

    it('passes the server explanation through', () => {
        expect(describeTestResult({ succeeded: false, error: 'The provider rejected the username or password.', channelCount: 0 }, 'channels'))
            .toBe('The provider rejected the username or password.');
    });
});
