import { type ReactNode } from 'react';

/**
 * Single source of truth for admin navigation. Consumed by both:
 *   - SidebarV2 (renders the persistent left-rail nav)
 *   - SearchPalette (Cmd-K jump-to-page)
 *
 * Add a new admin page here once and it appears in both places. No more drift.
 */

export type IconName =
    | 'dashboard' | 'settings' | 'tasks' | 'plugins' | 'users' | 'devices'
    | 'history' | 'music-note' | 'chart' | 'folder' | 'layers' | 'image'
    | 'copy' | 'list' | 'inbox' | 'compass' | 'star' | 'calendar'
    | 'tv' | 'record' | 'radio' | 'mic' | 'palette' | 'logs' | 'backup';

export const Icons: Record<IconName, ReactNode> = {
    dashboard: <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.75} d="M3 12l9-9 9 9M5 10v10a1 1 0 001 1h3v-7h6v7h3a1 1 0 001-1V10" />,
    settings: <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.75} d="M10.325 4.317a1 1 0 011.35 0l.928.928a1 1 0 001.06.23l1.213-.466a1 1 0 011.314.585l.466 1.213a1 1 0 00.23 1.06l.928.928a1 1 0 010 1.35l-.928.928a1 1 0 00-.23 1.06l.466 1.213a1 1 0 01-.585 1.314l-1.213.466a1 1 0 00-1.06.23l-.928.928a1 1 0 01-1.35 0l-.928-.928a1 1 0 00-1.06-.23l-1.213.466a1 1 0 01-1.314-.585l-.466-1.213a1 1 0 00-.23-1.06l-.928-.928a1 1 0 010-1.35l.928-.928a1 1 0 00.23-1.06l-.466-1.213a1 1 0 01.585-1.314l1.213-.466a1 1 0 001.06-.23l.928-.928zM12 15a3 3 0 100-6 3 3 0 000 6z" />,
    tasks: <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.75} d="M12 8v4l3 2m6-2a9 9 0 11-18 0 9 9 0 0118 0z" />,
    plugins: <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.75} d="M14 4l-4 4 2 2-3 3a2 2 0 002.83 2.83l3-3 2 2 4-4M7 17l-2 2" />,
    users: <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.75} d="M17 20h5v-2a4 4 0 00-3-3.87M9 20H4v-2a4 4 0 013-3.87m6 5.87a4 4 0 11-8 0 4 4 0 018 0zM17 11a3 3 0 100-6 3 3 0 000 6z" />,
    devices: <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.75} d="M9 17v-1.5a1.5 1.5 0 011.5-1.5h3a1.5 1.5 0 011.5 1.5V17M3 7a2 2 0 012-2h14a2 2 0 012 2v8a2 2 0 01-2 2H5a2 2 0 01-2-2V7z" />,
    history: <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.75} d="M3 12a9 9 0 109-9M3 12l3-3m-3 3l3 3M12 7v5l3 2" />,
    'music-note': <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.75} d="M9 17V5l12-2v12M9 17a3 3 0 11-6 0 3 3 0 016 0zm12-2a3 3 0 11-6 0 3 3 0 016 0z" />,
    chart: <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.75} d="M3 21h18M5 21V10m4 11V5m4 16v-8m4 8v-5m4 5V8" />,
    folder: <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.75} d="M3 7a2 2 0 012-2h4l2 2h8a2 2 0 012 2v8a2 2 0 01-2 2H5a2 2 0 01-2-2V7z" />,
    layers: <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.75} d="M12 4l9 5-9 5-9-5 9-5zM3 15l9 5 9-5M3 19l9 5 9-5" />,
    image: <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.75} d="M4 16l4-4 3 3 5-5 4 4M4 6h16v14H4V6zm12 4a2 2 0 11-4 0 2 2 0 014 0z" />,
    copy: <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.75} d="M9 5H5a2 2 0 00-2 2v10a2 2 0 002 2h10a2 2 0 002-2v-4M9 5a2 2 0 002 2h6a2 2 0 012 2v6a2 2 0 01-2 2H9a2 2 0 01-2-2V7a2 2 0 012-2z" />,
    list: <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.75} d="M4 6h16M4 12h16M4 18h10" />,
    inbox: <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.75} d="M20 13V6a2 2 0 00-2-2H6a2 2 0 00-2 2v7m16 0l-3 7H7l-3-7m16 0H4m4 0a4 4 0 008 0" />,
    compass: <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.75} d="M12 21a9 9 0 100-18 9 9 0 000 18zm3-12l-2 5-5 2 2-5 5-2z" />,
    star: <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.75} d="M11.05 3.72a1 1 0 011.9 0l2 5.27 5.62.4a1 1 0 01.57 1.76l-4.31 3.65 1.34 5.48a1 1 0 01-1.5 1.1L12 18.62l-4.67 2.76a1 1 0 01-1.5-1.1l1.34-5.48-4.31-3.65a1 1 0 01.57-1.76l5.62-.4 2-5.27z" />,
    calendar: <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.75} d="M8 7V3m8 4V3M3 11h18M5 5h14a2 2 0 012 2v12a2 2 0 01-2 2H5a2 2 0 01-2-2V7a2 2 0 012-2z" />,
    tv: <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.75} d="M5 8h14a2 2 0 012 2v8a2 2 0 01-2 2H5a2 2 0 01-2-2v-8a2 2 0 012-2zm3 12h8M8 4l4 4 4-4" />,
    record: <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.75} d="M21 12a9 9 0 11-18 0 9 9 0 0118 0zm-9 4a4 4 0 100-8 4 4 0 000 8z" />,
    radio: <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.75} d="M3.5 12a8.5 8.5 0 0117 0m-3.5 0a5 5 0 00-10 0m6 0a1 1 0 11-2 0 1 1 0 012 0z" />,
    mic: <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.75} d="M12 1a3 3 0 00-3 3v8a3 3 0 006 0V4a3 3 0 00-3-3zM5 10v2a7 7 0 0014 0v-2M12 19v4m-4 0h8" />,
    palette: <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.75} d="M12 21a9 9 0 110-18 9 9 0 015.66 16.06A2.4 2.4 0 0116 21h-2a2 2 0 01-2-2v0a2 2 0 00-2-2h-1.5a2.5 2.5 0 010-5H10a2 2 0 002-2v0M9 8h.01M15.5 9h.01M17.5 13h.01" />,
    logs: <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.75} d="M4 4h12l4 4v12a2 2 0 01-2 2H4a2 2 0 01-2-2V6a2 2 0 012-2zm10 0v6h6M7 14h10M7 18h7M7 10h4" />,
    backup: <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.75} d="M21 12a9 9 0 11-9-9 9 9 0 019 9zM12 7v5l3 2M5 5l-2-2M21 5l2-2" />,
};

export type NavSection = 'Server' | 'Library' | 'Features';

/**
 * Optional gate that hides an entry until a runtime condition is true.
 * `ai` — only show when the server has an AI plugin installed and enabled.
 */
export type NavRequires = 'ai';

export interface AdminNavEntry {
    /** Display label in the sidebar AND the palette. */
    label: string;
    /** Path template starting with `/admin/...`. The path resolver inserts a
     *  `/server/<id>` prefix when the active route is server-scoped. */
    pathTemplate: string;
    icon: IconName;
    section: NavSection;
    /** Used by the NavLink `end` prop for parent-route matching. */
    end?: boolean;
    /** Search-only synonyms — never displayed; only used to broaden the
     *  fuzzy match in the Cmd-K palette. */
    keywords?: string[];
    /** Hide the entry unless this runtime condition holds. */
    requires?: NavRequires;
}

export const ADMIN_NAV: AdminNavEntry[] = [
    // === Server ===
    { label: 'Dashboard',            pathTemplate: '/admin',                  section: 'Server',  icon: 'dashboard',  end: true,  keywords: ['home'] },
    { label: 'System Settings',      pathTemplate: '/admin/settings',         section: 'Server',  icon: 'settings',   keywords: ['core', 'transcoder', 'remote', 'request servers'] },
    { label: 'Background Tasks',     pathTemplate: '/admin/tasks',            section: 'Server',  icon: 'tasks',      keywords: ['jobs', 'queue', 'worker'] },
    { label: 'Plugins',              pathTemplate: '/admin/plugins',          section: 'Server',  icon: 'plugins',    keywords: ['extensions'] },
    { label: 'Users & Access',       pathTemplate: '/admin/users',            section: 'Server',  icon: 'users',      keywords: ['accounts', 'profiles', 'invite'] },
    { label: 'Email Invitations',    pathTemplate: '/admin/invitations',      section: 'Server',  icon: 'inbox',      keywords: ['invite', 'invites', 'invitation', 'email', 'register'] },
    { label: 'Authorized Devices',   pathTemplate: '/admin/devices',          section: 'Server',  icon: 'devices',    keywords: ['clients', 'block'] },
    { label: 'Watch History',        pathTemplate: '/admin/history',          section: 'Server',  icon: 'history',    keywords: ['playback', 'sessions'] },
    { label: 'Music History',        pathTemplate: '/admin/music-history',    section: 'Server',  icon: 'music-note', keywords: ['listening', 'plays'] },
    { label: 'Appearance',           pathTemplate: '/admin/appearance',       section: 'Server',  icon: 'palette',    keywords: ['theme', 'colors', 'look', 'admin appearance'] },
    { label: 'Client Templates',     pathTemplate: '/admin/client-templates', section: 'Server',  icon: 'image',      keywords: ['client', 'template', 'schedule', 'thanksgiving', 'holiday', 'seasonal'] },
    { label: 'AI Usage & Stats',     pathTemplate: '/admin/ai-stats',         section: 'Server',  icon: 'chart',      keywords: ['tokens', 'openai', 'cost'], requires: 'ai' },
    { label: 'Server Logs',          pathTemplate: '/admin/logs',             section: 'Server',  icon: 'logs',       keywords: ['logs', 'errors', 'warnings', 'tail', 'trace', 'debug'] },
    { label: 'Backup & Restore',     pathTemplate: '/admin/backups',          section: 'Server',  icon: 'backup',     keywords: ['backup', 'restore', 'export', 'import', 'snapshot', 'schedule'] },
    { label: 'Library Migration',    pathTemplate: '/admin/library-migration', section: 'Server', icon: 'copy',       keywords: ['plex', 'migrate', 'import', 'sync', 'watch state', 'ratings'] },

    // === Library ===
    { label: 'Libraries',            pathTemplate: '/admin/libraries',        section: 'Library', icon: 'folder',     keywords: ['media', 'sources', 'folders'] },
    { label: 'Collections',          pathTemplate: '/admin/collections',      section: 'Library', icon: 'layers',     keywords: ['sync', 'trakt', 'lists'] },
    { label: 'Music',                pathTemplate: '/admin/music',            section: 'Library', icon: 'music-note', keywords: ['lyrics', 'lastfm', 'listening data'] },
    { label: 'Poster Overlays',      pathTemplate: '/admin/overlays',         section: 'Library', icon: 'image',      keywords: ['badges', 'resolution', 'ratings', 'editor'] },
    { label: 'Media Deduplication',  pathTemplate: '/admin/dedupe',           section: 'Library', icon: 'copy',       keywords: ['duplicates', 'duplicate', 'cleanup'] },
    { label: 'Smart Lists',          pathTemplate: '/admin/smart-lists',      section: 'Library', icon: 'list',       keywords: ['home screen', 'rows'] },
    { label: 'Request Queue',        pathTemplate: '/admin/requests',         section: 'Library', icon: 'inbox',      keywords: ['radarr', 'sonarr', 'approve'] },

    // === Features ===
    { label: 'Discover',             pathTemplate: '/admin/discovery',        section: 'Features', icon: 'compass',    keywords: ['discovery', 'tmdb'] },
    { label: 'For You',              pathTemplate: '/admin/for-you',          section: 'Features', icon: 'star',       keywords: ['recommendations', 'mixes', 'daily mix'] },
    { label: 'Release Calendar',     pathTemplate: '/admin/release-calendar', section: 'Features', icon: 'calendar',   keywords: ['upcoming', 'calendar'] },
    { label: 'Live TV',              pathTemplate: '/admin/live-tv',          section: 'Features', icon: 'tv',         keywords: ['iptv', 'playlists', 'epg'] },
    { label: 'DVR',                  pathTemplate: '/admin/dvr-settings',     section: 'Features', icon: 'record',     keywords: ['recording', 'tuners'] },
    { label: 'Internet Radio',       pathTemplate: '/admin/internet-radio',   section: 'Features', icon: 'radio',      keywords: ['radio', 'streams'] },
    { label: 'Podcasts',             pathTemplate: '/admin/podcasts',         section: 'Features', icon: 'mic',        keywords: ['rss', 'feeds', 'catalog'] },
];

/**
 * Resolve a `/admin/...` path template against the active server context.
 * When `serverId` is provided the path becomes `/server/<id>/admin/...`,
 * matching how the router scopes admin routes per server.
 */
export function resolveAdminPath(pathTemplate: string, serverId?: string): string {
    if (!serverId) return pathTemplate;
    return pathTemplate.replace('/admin', `/server/${serverId}/admin`);
}
