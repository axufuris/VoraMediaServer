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

// crypto.randomUUID only exists on a secure page (https, or localhost). A server
// opened as http://192.168.1.10:8080 has no such function, and calling it threw
// while the app was still loading, leaving a blank page on the first visit.
function newDeviceId(): string {
    if (typeof crypto.randomUUID === 'function') return crypto.randomUUID();

    const bytes = crypto.getRandomValues(new Uint8Array(16));
    bytes[6] = (bytes[6] & 0x0f) | 0x40;
    bytes[8] = (bytes[8] & 0x3f) | 0x80;
    const hex = Array.from(bytes, byte => byte.toString(16).padStart(2, '0')).join('');
    return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}

// The device id identifies this browser to the server (X-Vora-Device-Id). It is
// mirrored into a long-lived cookie so clearing localStorage alone — or storage
// eviction — doesn't mint a brand-new device record: the cookie restores the
// same id. A new id is only generated when both localStorage and the cookie are
// gone (a full site-data clear or a different browser).
export function getOrCreateDeviceId(): string {
    let id = localStorage.getItem(StorageKeys.deviceId);
    if (!id) {
        id = readCookie(DEVICE_ID_COOKIE) ?? newDeviceId();
        localStorage.setItem(StorageKeys.deviceId, id);
    }
    writeDeviceIdCookie(id);
    return id;
}
