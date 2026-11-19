import { describe, it, expect } from 'vitest';
import { ADMIN_NAV, Icons, resolveAdminPath } from './adminNavData';

describe('the Library Updates nav entry', () => {
    const entry = ADMIN_NAV.find(e => e.pathTemplate === '/admin/library-updates');

    it('sits in the Library section', () => {
        expect(entry).toBeDefined();
        expect(entry?.section).toBe('Library');
        expect(entry?.label).toBe('Library Updates');
    });

    it('uses an icon that exists', () => {
        expect(entry && Icons[entry.icon]).toBeTruthy();
    });

    it('can be found in the palette by what the page is about', () => {
        expect(entry?.keywords).toEqual(expect.arrayContaining(['webhook', 'radarr', 'sonarr', 'lidarr', 'import', 'notify', 'connect']));
    });

    it('moves under the server on the multi-server form', () => {
        expect(resolveAdminPath('/admin/library-updates', 's1')).toBe('/admin/server/s1/library-updates');
        expect(resolveAdminPath('/admin/library-updates')).toBe('/admin/library-updates');
    });

    it('is the only entry for its path', () => {
        expect(ADMIN_NAV.filter(e => e.pathTemplate === '/admin/library-updates')).toHaveLength(1);
    });
});
