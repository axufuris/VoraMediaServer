import { type ReactNode } from 'react';
import type { FeatureFlagsVM } from '../../../api/System/featureFlagsService';

export type ClientFeatureGate = keyof FeatureFlagsVM;

export type ClientIconName =
    | 'home' | 'discovery' | 'music' | 'live-tv' | 'dvr' | 'podcast'
    | 'library' | 'collections' | 'playlists' | 'watchlist' | 'calendar'
    | 'recommendations' | 'history' | 'settings' | 'search';

export const ClientIcons: Record<ClientIconName, ReactNode> = {
    home: <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M3 12l9-9 9 9M5 10v10h4v-6h6v6h4V10" />,
    discovery: <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M21 12a9 9 0 11-18 0 9 9 0 0118 0zm-6-3l-2 6-6 2 2-6 6-2z" />,
    music: <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M9 18V5l12-2v13M9 18a3 3 0 11-6 0 3 3 0 016 0zm12-2a3 3 0 11-6 0 3 3 0 016 0z" />,
    'live-tv': <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M2 6h20v13H2zM8 22l4-3 4 3" />,
    dvr: <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M21 12a9 9 0 11-18 0 9 9 0 0118 0zm-9 4a4 4 0 100-8 4 4 0 000 8z" />,
    podcast: <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M12 1a3 3 0 00-3 3v8a3 3 0 006 0V4a3 3 0 00-3-3zM5 10v2a7 7 0 0014 0v-2M12 19v4m-4 0h8" />,
    library: <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M3 3v18h18M7 17l4-4 4 4 6-6" />,
    collections: <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M12 4l9 5-9 5-9-5 9-5zM3 15l9 5 9-5M3 19l9 5 9-5" />,
    playlists: <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M4 6h16M4 12h16M4 18h10M16 16l5 3-5 3v-6z" />,
    watchlist: <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="m19 21-7-5-7 5V5a2 2 0 012-2h10a2 2 0 012 2v16z" />,
    calendar: <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M8 7V3m8 4V3M3 11h18M5 5h14a2 2 0 012 2v12a2 2 0 01-2 2H5a2 2 0 01-2-2V7a2 2 0 012-2z" />,
    recommendations: <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M11.05 3.72a1 1 0 011.9 0l2 5.27 5.62.4a1 1 0 01.57 1.76l-4.31 3.65 1.34 5.48a1 1 0 01-1.5 1.1L12 18.62l-4.67 2.76a1 1 0 01-1.5-1.1l1.34-5.48-4.31-3.65a1 1 0 01.57-1.76l5.62-.4 2-5.27z" />,
    history: <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M3 12a9 9 0 109-9M3 12l3-3m-3 3l3 3M12 7v5l3 2" />,
    settings: <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M12 15a3 3 0 100-6 3 3 0 000 6zm7-3a7 7 0 00-.1-1.2l2-1.5-2-3.4-2.4.8a7 7 0 00-2-1.2l-.4-2.5h-4l-.4 2.5a7 7 0 00-2 1.2l-2.4-.8-2 3.4 2 1.5A7 7 0 005 12c0 .4 0 .8.1 1.2l-2 1.5 2 3.4 2.4-.8a7 7 0 002 1.2l.4 2.5h4l.4-2.5a7 7 0 002-1.2l2.4.8 2-3.4-2-1.5a7 7 0 00.1-1.2z" />,
    search: <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M21 21l-4.3-4.3M11 19a8 8 0 110-16 8 8 0 010 16z" />,
};

export type ClientNavSection = 'Browse' | 'Live' | 'Music' | 'Personal' | 'Library';

export interface ClientNavEntry {
    label: string;
    pathTemplate: string;
    icon: ClientIconName;
    section: ClientNavSection;
    end?: boolean;
    keywords?: string[];
    requires?: ClientFeatureGate;
}

export const CLIENT_NAV: ClientNavEntry[] = [
    { label: 'Home',             pathTemplate: '/',              section: 'Browse',   icon: 'home',            end: true,  keywords: ['start', 'overview'] },
    { label: 'Discovery',        pathTemplate: '/discovery',     section: 'Browse',   icon: 'discovery',                   keywords: ['explore', 'find', 'tmdb'],           requires: 'discover' },
    { label: 'Recommendations',  pathTemplate: '/recommendations', section: 'Browse', icon: 'recommendations',             keywords: ['for you', 'suggested'],              requires: 'forYou' },
    { label: 'Watchlist',        pathTemplate: '/watchlist',     section: 'Personal', icon: 'watchlist',                   keywords: ['saved', 'later'] },
    { label: 'Calendar',         pathTemplate: '/calendar',      section: 'Personal', icon: 'calendar',                    keywords: ['releases', 'upcoming'],              requires: 'releaseCalendar' },
    { label: 'Live TV',          pathTemplate: '/live-tv',       section: 'Live',     icon: 'live-tv',                     keywords: ['iptv', 'channels'],                  requires: 'liveTv' },
    { label: 'DVR',              pathTemplate: '/live-tv/dvr',   section: 'Live',     icon: 'dvr',                         keywords: ['recordings', 'recorded'],            requires: 'dvr' },
    { label: 'Music',            pathTemplate: '/audio',         section: 'Music',    icon: 'music',                       keywords: ['songs', 'albums', 'artists', 'radio', 'podcasts'] },
    { label: 'Playlists',        pathTemplate: '/playlists',     section: 'Personal', icon: 'playlists',                   keywords: ['mix', 'smart playlist'] },
    { label: 'Collections',      pathTemplate: '/collections',   section: 'Personal', icon: 'collections',                 keywords: ['sets', 'curated'] },
    { label: 'Search',           pathTemplate: '/search',        section: 'Browse',   icon: 'search',                      keywords: ['find'] },
    { label: 'History',          pathTemplate: '/profile/history', section: 'Personal', icon: 'history',                   keywords: ['watched', 'recently played'] },
    { label: 'Settings',         pathTemplate: '/settings',      section: 'Personal', icon: 'settings',                    keywords: ['preferences', 'templates', 'playback'] },
];

export function resolveClientPath(template: string, serverId?: string): string {
    if (!serverId) return template;
    if (template === '/') return `/server/${serverId}`;
    return `/server/${serverId}${template}`;
}
