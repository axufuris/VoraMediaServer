import { profileDeviceSettingsService } from '../../../../api/Users/profileDeviceSettingsService';
import { StorageKeys } from '../../../../utils/storageKeys';
import { emptyGuidePrefs, parseGuidePrefs } from './useGuideData';

/** `favorites` with `externalId` added at the end (once) when `on`, else taken out. */
export function withFavorite(favorites: string[], externalId: string, on: boolean): string[] {
    const rest = favorites.filter(id => id !== externalId);
    return on ? [...rest, externalId] : rest;
}

/**
 * The player's heart: sets one channel on or off in the profile's TV
 * favourites. The server's current prefs are read first, so a filter or a
 * favourite changed on another device is kept, then the blob goes back with
 * the favourites in it (the server takes `favoriteChannels` as the profile's
 * whole list). Resolves to the favourites as saved; rejects when the server
 * can't be reached.
 */
export async function setChannelFavorite(
    profileId: string,
    deviceId: string,
    externalId: string,
    on: boolean,
    serverId?: string,
): Promise<string[]> {
    const current = await profileDeviceSettingsService.getIptvPrefs(profileId, deviceId, serverId);
    const prefs = parseGuidePrefs(current) ?? emptyGuidePrefs();
    const next = { ...prefs, favoriteChannels: withFavorite(prefs.favoriteChannels, externalId, on) };
    const json = JSON.stringify(next);
    await profileDeviceSettingsService.saveIptvPrefs(profileId, deviceId, json, serverId);
    localStorage.setItem(StorageKeys.iptvPrefs(profileId, deviceId), json);
    return next.favoriteChannels;
}
