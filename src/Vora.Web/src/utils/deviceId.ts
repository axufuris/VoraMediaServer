import { StorageKeys } from './storageKeys';

const DEVICE_ID_COOKIE = 'vora_device_id';
const TEN_YEARS_SECONDS = 60 * 60 * 24 * 365 * 10;

function readCookie(name: string): string | null {
    const match = document.cookie.split('; ').find(c => c.startsWith(`${name}=`));
    return match ? decodeURIComponent(match.slice(name.length + 1)) : null;
}

function writeDeviceIdCookie(id: string): void {
    document.cookie = `${DEVICE_ID_COOKIE}=${encodeURIComponent(id)}; path=/; max-age=${TEN_YEARS_SECONDS}; SameSite=Lax`;
}

// The device id identifies this browser to the server (X-Vora-Device-Id). It is
// mirrored into a long-lived cookie so clearing localStorage alone — or storage
// eviction — doesn't mint a brand-new device record: the cookie restores the
// same id. A new id is only generated when both localStorage and the cookie are
// gone (a full site-data clear or a different browser).
export function getOrCreateDeviceId(): string {
    let id = localStorage.getItem(StorageKeys.deviceId);
    if (!id) {
        id = readCookie(DEVICE_ID_COOKIE) ?? crypto.randomUUID();
        localStorage.setItem(StorageKeys.deviceId, id);
    }
    writeDeviceIdCookie(id);
    return id;
}
