import { describe, it, expect } from 'vitest';
import { providerStatus, providerTitle, receiveUrl } from './libraryUpdates';

describe('providerTitle', () => {
    it('drops the word Webhook from a provider name', () => {
        expect(providerTitle('Radarr Webhook')).toBe('Radarr');
        expect(providerTitle('Lidarr webhook')).toBe('Lidarr');
    });

    it('keeps a name that has no such word, or is only that word', () => {
        expect(providerTitle('Sonarr')).toBe('Sonarr');
        expect(providerTitle('Webhook')).toBe('Webhook');
    });
});

describe('providerStatus', () => {
    it('is Off before anything else when the plugin is switched off', () => {
        expect(providerStatus({ isEnabled: false, isConfigured: false })).toEqual({ label: 'Off', tone: 'neutral' });
        expect(providerStatus({ isEnabled: false, isConfigured: true })).toEqual({ label: 'Off', tone: 'neutral' });
    });

    it('needs a secret when on without one', () => {
        expect(providerStatus({ isEnabled: true, isConfigured: false })).toEqual({ label: 'Needs a secret', tone: 'warn' });
    });

    it('is Ready when on and configured', () => {
        expect(providerStatus({ isEnabled: true, isConfigured: true })).toEqual({ label: 'Ready', tone: 'ok' });
    });
});

describe('receiveUrl', () => {
    it('puts the page\'s origin in front of the receive path', () => {
        expect(receiveUrl('/api/webhooks/radarr_webhook')).toBe(`${window.location.origin}/api/webhooks/radarr_webhook`);
    });
});
