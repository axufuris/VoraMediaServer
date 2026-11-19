import { describe, it, expect, afterEach } from 'vitest';
import { connectionStatus, formatAgo, lastDeliveryFailed, lastDeliveryText, receiveUrl } from './libraryUpdates';
import { serverVault } from '../../../utils/serverVault';
import { formatDate } from '../../../utils/serverTime';

const NOW = Date.parse('2026-11-18T12:00:00Z');
const ago = (minutes: number) => new Date(NOW - minutes * 60_000).toISOString();

describe('connectionStatus', () => {
    it('is Ready when the connection is on', () => {
        expect(connectionStatus({ isEnabled: true })).toEqual({ label: 'Ready', tone: 'ok' });
    });

    it('is Off when the connection is off', () => {
        expect(connectionStatus({ isEnabled: false })).toEqual({ label: 'Off', tone: 'neutral' });
    });
});

describe('formatAgo', () => {
    it('says just now inside the first minute, and for a clock a little ahead', () => {
        expect(formatAgo(ago(0), NOW)).toBe('just now');
        expect(formatAgo(new Date(NOW + 30_000).toISOString(), NOW)).toBe('just now');
    });

    it('counts minutes, hours and days', () => {
        expect(formatAgo(ago(5), NOW)).toBe('5 min ago');
        expect(formatAgo(ago(59), NOW)).toBe('59 min ago');
        expect(formatAgo(ago(60 * 3), NOW)).toBe('3 h ago');
        expect(formatAgo(ago(60 * 24 * 2), NOW)).toBe('2 d ago');
    });

    it('gives a date after a week', () => {
        expect(formatAgo(ago(60 * 24 * 10), NOW)).toBe(formatDate(ago(60 * 24 * 10)));
    });

    it('reads a timestamp with no zone as UTC', () => {
        expect(formatAgo('2026-11-18T11:55:00', NOW)).toBe('5 min ago');
    });

    it('is empty for a value it cannot read', () => {
        expect(formatAgo('not a date', NOW)).toBe('');
    });
});

describe('lastDeliveryText', () => {
    it('says nothing has been received when there is no delivery', () => {
        expect(lastDeliveryText({ lastDeliveryAt: null, lastDeliveryOk: null }, NOW)).toBe('Nothing received yet');
    });

    it('says ok for a delivery that worked', () => {
        expect(lastDeliveryText({ lastDeliveryAt: ago(5), lastDeliveryOk: true }, NOW)).toBe('Last: 5 min ago, ok');
    });

    it('says failed in words for a delivery that did not', () => {
        expect(lastDeliveryText({ lastDeliveryAt: ago(12), lastDeliveryOk: false }, NOW)).toBe('Last: 12 min ago, failed');
    });
});

describe('lastDeliveryFailed', () => {
    it('is true only for a delivery that is known to have failed', () => {
        expect(lastDeliveryFailed({ lastDeliveryAt: ago(1), lastDeliveryOk: false })).toBe(true);
        expect(lastDeliveryFailed({ lastDeliveryAt: ago(1), lastDeliveryOk: true })).toBe(false);
        expect(lastDeliveryFailed({ lastDeliveryAt: null, lastDeliveryOk: null })).toBe(false);
    });
});

describe('receiveUrl', () => {
    afterEach(() => localStorage.clear());

    it('puts the page\'s origin in front of the receive path', () => {
        expect(receiveUrl('/api/webhooks/c1')).toBe(`${window.location.origin}/api/webhooks/c1`);
    });

    it('uses the address of the server being managed when it is not the one serving the page', () => {
        serverVault.addOrUpdateServer({ id: 'second', name: 'Second', url: 'http://second:8080/', token: 't', profileId: 'p', isAdmin: true });

        expect(receiveUrl('/api/webhooks/c1', 'second')).toBe('http://second:8080/api/webhooks/c1');
    });

    it('falls back to the origin for a server it has no address for', () => {
        expect(receiveUrl('/api/webhooks/c1', 'unknown')).toBe(`${window.location.origin}/api/webhooks/c1`);
    });
});
