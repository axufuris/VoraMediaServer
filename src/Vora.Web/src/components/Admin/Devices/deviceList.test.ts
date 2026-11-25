import { describe, expect, it } from 'vitest';
import type { ClientDeviceVM } from '../../../api/Users/deviceService';
import type { UserVM } from '../../../api/Users/userService';
import {
    ALL_USERS,
    NO_USER,
    UNKNOWN_USER,
    deviceLabel,
    filterDevices,
    filtersApply,
    groupByLastUser,
    normalizeNickname,
    userChoices,
    userKeyOf,
} from './deviceList';

const users = [
    { id: 'u-sam', displayName: 'Sam', profiles: [{ id: 'p-sam', name: 'Sam' }, { id: 'p-kids', name: 'Kids' }] },
    { id: 'u-alex', displayName: 'Alex', profiles: [{ id: 'p-alex', name: 'Alex' }] },
] as unknown as UserVM[];

const device = (patch: Partial<ClientDeviceVM>): ClientDeviceVM => ({
    id: patch.deviceId ?? 'id',
    deviceId: 'device',
    clientName: 'Vora Android',
    deviceName: 'Device',
    nickname: '',
    deviceType: 'TV',
    lastIpAddress: '10.0.0.2',
    operatingSystem: 'Android',
    location: 'Local Network',
    lastConnectedAt: '2026-11-24T10:00:00Z',
    isBlocked: false,
    ...patch,
});

const tv = device({ deviceId: 'tv', deviceName: 'Shield', nickname: 'Living room', lastIpAddress: '192.168.1.5', lastUserId: 'u-sam', lastProfileId: 'p-kids' });
const phone = device({ deviceId: 'phone', deviceName: 'Pixel 8', deviceType: 'Phone', lastUserId: 'u-alex', lastProfileId: 'p-alex' });
const web = device({ deviceId: 'web', deviceName: 'Chrome on Windows', clientName: 'Vora Web', deviceType: 'Browser', operatingSystem: 'Windows', lastUserId: 'deleted-user' });
const stray = device({ deviceId: 'stray', deviceName: 'Roku Ultra', clientName: 'Vora Roku', operatingSystem: 'Roku OS' });
const all = [tv, phone, web, stray];

describe('deviceLabel', () => {
    it('prefers the nickname, then the device name, the client and the id', () => {
        expect(deviceLabel(tv)).toBe('Living room');
        expect(deviceLabel(phone)).toBe('Pixel 8');
        expect(deviceLabel(device({ deviceName: '', clientName: 'Vora Roku' }))).toBe('Vora Roku');
        expect(deviceLabel(device({ deviceName: '', clientName: '', deviceId: 'abc-123' }))).toBe('abc-123');
    });

    it('ignores a nickname that is only white space', () => {
        expect(deviceLabel(device({ nickname: '   ', deviceName: 'Shield' }))).toBe('Shield');
    });
});

describe('normalizeNickname', () => {
    it('trims and turns runs of white space into one space, as the server does', () => {
        expect(normalizeNickname('  Living   room\tTV ')).toBe('Living room TV');
        expect(normalizeNickname('   ')).toBe('');
    });
});

describe('userKeyOf', () => {
    it('names the account, or none, or unknown when the account has been deleted', () => {
        expect(userKeyOf(tv, users)).toBe('u-sam');
        expect(userKeyOf(stray, users)).toBe(NO_USER);
        expect(userKeyOf(web, users)).toBe(UNKNOWN_USER);
    });
});

describe('filterDevices', () => {
    const find = (query: string, userKey = ALL_USERS) => filterDevices(all, users, { query, userKey }).map(entry => entry.deviceId);

    it('keeps everything when nothing is asked', () => {
        expect(find('')).toEqual(['tv', 'phone', 'web', 'stray']);
        expect(find('   ')).toEqual(['tv', 'phone', 'web', 'stray']);
    });

    it('finds a device by its nickname, its own name, its client, its address or its system', () => {
        expect(find('living')).toEqual(['tv']);
        expect(find('SHIELD')).toEqual(['tv']);
        expect(find('roku')).toEqual(['stray']);
        expect(find('192.168')).toEqual(['tv']);
        expect(find('windows')).toEqual(['web']);
    });

    it('finds a device by who used it last, account or profile', () => {
        expect(find('alex')).toEqual(['phone']);
        expect(find('kids')).toEqual(['tv']);
    });

    it('needs every word to match somewhere', () => {
        expect(find('living kids')).toEqual(['tv']);
        expect(find('living alex')).toEqual([]);
    });

    it('narrows to one last user, or to devices nobody has used', () => {
        expect(find('', 'u-sam')).toEqual(['tv']);
        expect(find('', NO_USER)).toEqual(['stray']);
        expect(find('', UNKNOWN_USER)).toEqual(['web']);
    });

    it('applies the user and the search together', () => {
        expect(find('pixel', 'u-alex')).toEqual(['phone']);
        expect(find('pixel', 'u-sam')).toEqual([]);
    });
});

describe('filtersApply', () => {
    it('is true once there is a search or a user chosen', () => {
        expect(filtersApply({ query: '', userKey: ALL_USERS })).toBe(false);
        expect(filtersApply({ query: '  ', userKey: ALL_USERS })).toBe(false);
        expect(filtersApply({ query: 'tv', userKey: ALL_USERS })).toBe(true);
        expect(filtersApply({ query: '', userKey: 'u-sam' })).toBe(true);
    });
});

describe('groupByLastUser', () => {
    it('groups by account in name order, with unknown and anonymous last', () => {
        const groups = groupByLastUser(all, users);

        expect(groups.map(group => [group.label, group.devices.map(entry => entry.deviceId)])).toEqual([
            ['Alex', ['phone']],
            ['Sam', ['tv']],
            ['Unknown user', ['web']],
            ['Anonymous', ['stray']],
        ]);
    });

    it("keeps each group's devices in the order given, most recent first", () => {
        const second = device({ deviceId: 'second', lastUserId: 'u-sam' });

        expect(groupByLastUser([second, tv], users)[0].devices.map(entry => entry.deviceId)).toEqual(['second', 'tv']);
    });

    it('has no group for an account with no device', () => {
        expect(groupByLastUser([phone], users).map(group => group.key)).toEqual(['u-alex']);
    });
});

describe('userChoices', () => {
    it('counts the devices each account used last', () => {
        expect(userChoices([...all, device({ deviceId: 'tablet', lastUserId: 'u-sam' })], users)).toEqual([
            { key: 'u-alex', label: 'Alex', count: 1 },
            { key: 'u-sam', label: 'Sam', count: 2 },
            { key: UNKNOWN_USER, label: 'Unknown user', count: 1 },
            { key: NO_USER, label: 'Anonymous', count: 1 },
        ]);
    });
});
