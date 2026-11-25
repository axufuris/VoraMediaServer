import type { ClientDeviceVM } from '../../../api/Users/deviceService';
import type { UserVM } from '../../../api/Users/userService';

export const ALL_USERS = 'all';
export const NO_USER = 'none';
export const UNKNOWN_USER = 'unknown';

export const NICKNAME_MAX_LENGTH = 64;

export function normalizeNickname(nickname: string): string {
    return nickname.trim().replace(/\s+/g, ' ');
}

export function deviceLabel(device: Pick<ClientDeviceVM, 'nickname' | 'deviceName' | 'clientName' | 'deviceId'>): string {
    return [device.nickname, device.deviceName, device.clientName, device.deviceId].find(name => name?.trim()) ?? '';
}

export function userKeyOf(device: ClientDeviceVM, users: UserVM[]): string {
    if (!device.lastUserId) return NO_USER;
    return users.some(user => user.id === device.lastUserId) ? device.lastUserId : UNKNOWN_USER;
}

export function lastProfileOf(device: ClientDeviceVM, users: UserVM[]) {
    const user = users.find(candidate => candidate.id === device.lastUserId);
    return user?.profiles.find(profile => profile.id === device.lastProfileId) ?? null;
}

function userLabel(key: string, users: UserVM[]): string {
    if (key === NO_USER) return 'Anonymous';
    if (key === UNKNOWN_USER) return 'Unknown user';
    return users.find(user => user.id === key)?.displayName ?? 'Unknown user';
}

function searchableText(device: ClientDeviceVM, users: UserVM[]): string {
    const user = users.find(candidate => candidate.id === device.lastUserId);
    return [
        device.nickname,
        device.deviceName,
        device.clientName,
        device.deviceType,
        device.operatingSystem,
        device.lastIpAddress,
        device.location,
        user?.displayName,
        lastProfileOf(device, users)?.name,
    ].filter(Boolean).join('\n').toLowerCase();
}

export interface DeviceFilters {
    query: string;
    userKey: string;
}

export const NO_FILTERS: DeviceFilters = { query: '', userKey: ALL_USERS };

export function filtersApply(filters: DeviceFilters): boolean {
    return filters.query.trim() !== '' || filters.userKey !== ALL_USERS;
}

export function filterDevices(devices: ClientDeviceVM[], users: UserVM[], filters: DeviceFilters): ClientDeviceVM[] {
    const terms = filters.query.toLowerCase().split(/\s+/).filter(Boolean);
    return devices.filter(device => {
        if (filters.userKey !== ALL_USERS && userKeyOf(device, users) !== filters.userKey) return false;
        if (terms.length === 0) return true;
        const text = searchableText(device, users);
        return terms.every(term => text.includes(term));
    });
}

export interface DeviceGroup {
    key: string;
    label: string;
    devices: ClientDeviceVM[];
}

const LAST = [UNKNOWN_USER, NO_USER];

export function groupByLastUser(devices: ClientDeviceVM[], users: UserVM[]): DeviceGroup[] {
    const groups = new Map<string, DeviceGroup>();
    for (const device of devices) {
        const key = userKeyOf(device, users);
        const group = groups.get(key) ?? { key, label: userLabel(key, users), devices: [] };
        group.devices.push(device);
        groups.set(key, group);
    }
    const rank = (key: string) => LAST.indexOf(key) + 1;
    return [...groups.values()].sort((a, b) =>
        (rank(a.key) - rank(b.key)) || a.label.localeCompare(b.label, undefined, { sensitivity: 'base' }));
}

export interface UserChoice {
    key: string;
    label: string;
    count: number;
}

export function userChoices(devices: ClientDeviceVM[], users: UserVM[]): UserChoice[] {
    return groupByLastUser(devices, users).map(group => ({ key: group.key, label: group.label, count: group.devices.length }));
}
