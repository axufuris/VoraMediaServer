import { describe, it, expect } from 'vitest';
import { ADMIN_NAV, Icons, resolveAdminPath } from './adminNavData';

describe('the External Connections nav entry', () => {
    const entry = ADMIN_NAV.find(e => e.pathTemplate === '/admin/external-connections');

    it('sits in the Server section, right after Plugins', () => {
        expect(entry).toBeDefined();
        expect(entry?.section).toBe('Server');
        expect(entry?.label).toBe('External Connections');
        const index = ADMIN_NAV.findIndex(e => e.pathTemplate === '/admin/external-connections');
        expect(ADMIN_NAV[index - 1].label).toBe('Plugins');
    });

    it('uses an icon that exists', () => {
        expect(entry && Icons[entry.icon]).toBeTruthy();
    });

    it('can be found in the palette by what the page is about', () => {
        expect(entry?.keywords).toEqual(expect.arrayContaining([
            'external', 'connection', 'request', 'requests', 'radarr', 'sonarr', 'lidarr', 'webhook', 'notify', 'import', 'integration',
        ]));
    });

    it('moves under the server on the multi-server form', () => {
        expect(resolveAdminPath('/admin/external-connections', 's1')).toBe('/admin/server/s1/external-connections');
        expect(resolveAdminPath('/admin/external-connections')).toBe('/admin/external-connections');
        expect(resolveAdminPath('/admin/external-connections/webhooks', 's1')).toBe('/admin/server/s1/external-connections/webhooks');
    });

    it('is the only entry for its path', () => {
        expect(ADMIN_NAV.filter(e => e.pathTemplate === '/admin/external-connections')).toHaveLength(1);
    });
});

describe('the pages that moved into External Connections', () => {
    it('leaves no Library Updates entry behind', () => {
        expect(ADMIN_NAV.find(e => e.label === 'Library Updates')).toBeUndefined();
        expect(ADMIN_NAV.filter(e => e.pathTemplate.includes('library-updates'))).toHaveLength(0);
    });

    it('no longer files Request Servers under System Settings', () => {
        const settings = ADMIN_NAV.find(e => e.pathTemplate === '/admin/settings');
        expect(settings?.keywords).toBeDefined();
        expect(settings?.keywords?.join(' ')).not.toMatch(/request/i);
    });

    it('keeps the Request Queue where it was', () => {
        const queue = ADMIN_NAV.find(e => e.pathTemplate === '/admin/requests');
        expect(queue?.section).toBe('Library');
        expect(queue?.label).toBe('Request Queue');
    });
});
