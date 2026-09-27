import type { IptvPlaylistVM } from '../api/Iptv/iptvAdminService';

// Splits providers into Live TV and Radio by their default channel kind, the
// same field the admin Providers screen uses. Anything not explicitly Radio
// counts as Live TV, so a provider with an unexpected kind is never hidden.
export interface ProviderGroups {
    liveTv: IptvPlaylistVM[];
    radio: IptvPlaylistVM[];
}

export function groupProvidersByKind(providers: IptvPlaylistVM[]): ProviderGroups {
    const groups: ProviderGroups = { liveTv: [], radio: [] };
    for (const provider of providers) {
        (provider.defaultChannelKind === 'Radio' ? groups.radio : groups.liveTv).push(provider);
    }
    return groups;
}
