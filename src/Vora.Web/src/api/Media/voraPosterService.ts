import { apiClient } from '../client';

export interface VoraPosterCategory {
    id: string;
    name: string;
}

export interface VoraPoster {
    id: string;
    category: string;
    title: string;
    aliases: string[];
    url: string;
}

export interface VoraPosterCatalog {
    categories: VoraPosterCategory[];
    posters: VoraPoster[];
}

const catalogs = new Map<string, Promise<VoraPosterCatalog>>();

export function getVoraPosters(serverId?: string): Promise<VoraPosterCatalog> {
    const key = serverId ?? '';
    const known = catalogs.get(key);
    if (known) return known;

    const request = apiClient.get<VoraPosterCatalog>('/api/artwork/vora', { serverId }).then(response => response.data);
    catalogs.set(key, request);
    request.catch(() => catalogs.delete(key));
    return request;
}
