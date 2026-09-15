import { describe, it, expect } from 'vitest';
import { groupProvidersByKind } from './providerGroups';
import type { IptvPlaylistVM } from '../api/Iptv/iptvAdminService';

const provider = (name: string, kind: string): IptvPlaylistVM => ({
    id: name,
    name,
    isActive: true,
    supportsWebPlayback: true,
    maxConcurrentStreams: 0,
    defaultChannelKind: kind as IptvPlaylistVM['defaultChannelKind'],
});

describe('grouping providers by kind', () => {
    it('puts radio providers under Radio and the rest under Live TV', () => {
        const groups = groupProvidersByKind([
            provider('US — IPTV Org', 'Tv'),
            provider('Radio — US Top 100 (Radio Browser)', 'Radio'),
            provider('Roku Channel', 'Tv'),
        ]);

        expect(groups.liveTv.map(p => p.name)).toEqual(['US — IPTV Org', 'Roku Channel']);
        expect(groups.radio.map(p => p.name)).toEqual(['Radio — US Top 100 (Radio Browser)']);
    });

    // An unexpected kind must still be listed somewhere, or an admin could
    // never grant access to it.
    it.each(['', 'Unknown', 'radio'])('keeps a provider with kind %j visible under Live TV', kind => {
        expect(groupProvidersByKind([provider('Mystery', kind)]).liveTv.map(p => p.name)).toEqual(['Mystery']);
    });

    it('keeps each group in the order given', () => {
        const groups = groupProvidersByKind([provider('B', 'Tv'), provider('A', 'Tv')]);

        expect(groups.liveTv.map(p => p.name)).toEqual(['B', 'A']);
    });

    it('returns empty groups for no providers', () => {
        expect(groupProvidersByKind([])).toEqual({ liveTv: [], radio: [] });
    });
});
