import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { getOrCreateDeviceId } from './deviceId';
import { StorageKeys } from './storageKeys';

const UUID_V4 = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/;

const clearDeviceCookie = () => {
    document.cookie = 'vora_device_id=; path=/; max-age=0';
};

describe('getOrCreateDeviceId', () => {
    beforeEach(() => {
        localStorage.clear();
        clearDeviceCookie();
    });

    afterEach(() => {
        vi.unstubAllGlobals();
        localStorage.clear();
        clearDeviceCookie();
    });

    it('makes an id on a plain http page, where crypto.randomUUID does not exist', () => {
        vi.stubGlobal('crypto', { getRandomValues: (bytes: Uint8Array) => bytes.fill(0xab) });

        const id = getOrCreateDeviceId();

        expect(id).toMatch(UUID_V4);
        expect(localStorage.getItem(StorageKeys.deviceId)).toBe(id);
    });

    it('uses crypto.randomUUID when the page has it', () => {
        vi.stubGlobal('crypto', { randomUUID: () => '3f2b8a1c-7d4e-4c5b-9a6f-0e1d2c3b4a59', getRandomValues: (bytes: Uint8Array) => bytes });

        expect(getOrCreateDeviceId()).toBe('3f2b8a1c-7d4e-4c5b-9a6f-0e1d2c3b4a59');
    });

    it('keeps the id it already has', () => {
        localStorage.setItem(StorageKeys.deviceId, 'known-device');

        expect(getOrCreateDeviceId()).toBe('known-device');
        expect(document.cookie).toContain('vora_device_id=known-device');
    });

    it('restores the id from its cookie when local storage was cleared', () => {
        document.cookie = 'vora_device_id=from-cookie; path=/';

        expect(getOrCreateDeviceId()).toBe('from-cookie');
        expect(localStorage.getItem(StorageKeys.deviceId)).toBe('from-cookie');
    });
});
