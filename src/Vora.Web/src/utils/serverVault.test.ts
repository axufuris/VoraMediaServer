import { describe, it, expect, beforeEach } from 'vitest';
import { serverVault, type VoraServer } from './serverVault';

function makeServer(id: string, name = id, isAdmin = false): VoraServer {
    return {
        id,
        name,
        url: `https://${id}.example.com`,
        token: `tok-${id}`,
        profileId: `prof-${id}`,
        isAdmin
    };
}

describe('serverVault', () => {
    beforeEach(() => {
        localStorage.clear();
    });

    it('getServers returns empty array when nothing stored', () => {
        expect(serverVault.getServers()).toEqual([]);
    });

    it('getServers returns empty array on malformed JSON', () => {
        localStorage.setItem('Vora_servers_vault', '{not valid json');
        expect(serverVault.getServers()).toEqual([]);
    });

    it('addOrUpdateServer adds a new server', () => {
        serverVault.addOrUpdateServer(makeServer('s1'));
        const all = serverVault.getServers();
        expect(all).toHaveLength(1);
        expect(all[0].id).toBe('s1');
    });

    it('addOrUpdateServer replaces existing entry with same id', () => {
        serverVault.addOrUpdateServer(makeServer('s1', 'Original'));
        serverVault.addOrUpdateServer(makeServer('s1', 'Updated'));

        const all = serverVault.getServers();
        expect(all).toHaveLength(1);
        expect(all[0].name).toBe('Updated');
    });

    it('getServer returns undefined when id missing', () => {
        expect(serverVault.getServer('missing')).toBeUndefined();
    });

    it('getServer returns matching entry', () => {
        serverVault.addOrUpdateServer(makeServer('s1'));
        serverVault.addOrUpdateServer(makeServer('s2'));
        expect(serverVault.getServer('s2')?.name).toBe('s2');
    });

    it('removeServer drops the server from the vault', () => {
        serverVault.addOrUpdateServer(makeServer('s1'));
        serverVault.addOrUpdateServer(makeServer('s2'));
        serverVault.removeServer('s1');

        const remaining = serverVault.getServers();
        expect(remaining).toHaveLength(1);
        expect(remaining[0].id).toBe('s2');
    });

    it('removeServer clears active server id when it matches', () => {
        serverVault.addOrUpdateServer(makeServer('s1'));
        serverVault.setActiveServerId('s1');

        serverVault.removeServer('s1');

        expect(serverVault.getActiveServerId()).toBeNull();
    });

    it('removeServer preserves active server id when it does not match', () => {
        serverVault.addOrUpdateServer(makeServer('s1'));
        serverVault.addOrUpdateServer(makeServer('s2'));
        serverVault.setActiveServerId('s2');

        serverVault.removeServer('s1');

        expect(serverVault.getActiveServerId()).toBe('s2');
    });

    it('getActiveServer returns undefined when no active id set', () => {
        expect(serverVault.getActiveServer()).toBeUndefined();
    });

    it('getActiveServer returns the matching server when active id set', () => {
        serverVault.addOrUpdateServer(makeServer('s1'));
        serverVault.setActiveServerId('s1');

        expect(serverVault.getActiveServer()?.id).toBe('s1');
    });

    it('clearVault removes both vault and active id', () => {
        serverVault.addOrUpdateServer(makeServer('s1'));
        serverVault.setActiveServerId('s1');

        serverVault.clearVault();

        expect(serverVault.getServers()).toEqual([]);
        expect(serverVault.getActiveServerId()).toBeNull();
    });
});
