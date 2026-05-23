import type { ReactNode } from 'react';

/**
 * Icons used by the client-side MainLayout sidebar AND anywhere else that
 * needs to render the same iconography (e.g. the library media-type picker
 * on CreateLibrary).
 *
 * Lives in its own module so Fast Refresh stays happy — files that export
 * React components can't also export non-component values without breaking
 * the `react-refresh/only-export-components` rule. Moving these here also
 * makes the icon set easy to find and extend.
 */

export const NAV_ICON_PATHS: Record<string, ReactNode> = {
    playlists: <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 6h13M4 12h13M4 18h9M19 14v6m-3-3h6" />,
    collections: <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 11H5m14 0a2 2 0 012 2v6a2 2 0 01-2 2H5a2 2 0 01-2-2v-6a2 2 0 012-2m14 0V9a2 2 0 00-2-2M5 11V9a2 2 0 012-2m0 0V5a2 2 0 012-2h6a2 2 0 012 2v2M7 7h10" />,
    discovery: <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M21 12a9 9 0 01-9 9m9-9a9 9 0 00-9-9m9 9H3m9 9a9 9 0 01-9-9m9 9c1.657 0 3-4.03 3-9s-1.343-9-3-9m0 18c-1.657 0-3-4.03-3-9s1.343-9 3-9m-9 9a9 9 0 019-9" />,
    recommendations: <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M5 3v4M3 5h4M6 17v4m-2-2h4m5-16l2.286 6.857L21 12l-5.714 2.143L13 21l-2.286-6.857L5 12l5.714-2.143L13 3z" />,
    watchlist: <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M5 5a2 2 0 012-2h10a2 2 0 012 2v16l-7-3.5L5 21V5z" />,
    calendar: <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M8 7V3m8 4V3m-9 8h10M5 21h14a2 2 0 002-2V7a2 2 0 00-2-2H5a2 2 0 00-2 2v12a2 2 0 002 2z" />,
    livetv: <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 21h6m-3-3v3M4 4h16a1 1 0 011 1v12a1 1 0 01-1 1H4a1 1 0 01-1-1V5a1 1 0 011-1z" />,
    dvr: <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15 10l4.553-2.276A1 1 0 0121 8.618v6.764a1 1 0 01-1.447.894L15 14M5 18h8a2 2 0 002-2V8a2 2 0 00-2-2H5a2 2 0 00-2 2v8a2 2 0 002 2z" />,
    audio: <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 19V6l12-3v13M9 19c0 1.657-1.79 3-4 3s-4-1.343-4-3 1.79-3 4-3 4 1.343 4 3zm12-3c0 1.657-1.79 3-4 3s-4-1.343-4-3 1.79-3 4-3 4 1.343 4 3z" />,
    youtube: <><rect x="2.5" y="6" width="19" height="12" rx="3" strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} /><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M10 9.5l5 2.5-5 2.5z" /></>,
    Movie: <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M7 4v16M17 4v16M3 8h4m10 0h4M3 12h18M3 16h4m10 0h4M4 4h16c.55 0 1 .45 1 1v14c0 .55-.45 1-1 1H4c-.55 0-1-.45-1-1V5c0-.55.45-1 1-1z" />,
    TvShow: <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M3 7h18v10H3z M8 21h8 M12 17v4" />,
    Music: <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 19V6l12-3v13M9 19c0 1.657-1.79 3-4 3s-4-1.343-4-3 1.79-3 4-3 4 1.343 4 3z" />,
    HomeVideo: <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M3 6a2 2 0 012-2h10a2 2 0 012 2v12a2 2 0 01-2 2H5a2 2 0 01-2-2V6z M17 10l4-2v8l-4-2z M7 8h6 M7 12h4" />,
};

export const renderNavIcon = (key: string | undefined, sizeClass: string): ReactNode => {
    if (!key) return null;
    const paths = NAV_ICON_PATHS[key];
    if (!paths) return null;
    return (
        <svg className={`${sizeClass} shrink-0`} fill="none" stroke="currentColor" viewBox="0 0 24 24">{paths}</svg>
    );
};
