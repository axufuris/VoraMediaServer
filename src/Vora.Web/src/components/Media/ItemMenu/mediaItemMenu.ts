import { createContext } from 'react';
import type { MenuPoint } from '../../Client/Primitives/menuEntries';

// What a poster hands over when it is right-clicked or long-pressed: enough to
// load the item and to tell the page that drew it when the item changed.
export interface MediaMenuTarget {
    id: string;
    serverId?: string;
    onChanged?: () => void;
    onDeleted?: () => void;
}

export interface MediaItemMenuApi {
    open: (target: MediaMenuTarget, at: MenuPoint) => void;
}

export const MediaItemMenuContext = createContext<MediaItemMenuApi | null>(null);

const TYPES_WITH_A_MENU = new Set(['Movie', 'TvShow', 'Season', 'Episode']);

interface MenuCallbacks {
    onChanged?: () => void;
    onDeleted?: () => void;
}

// Only library titles have this menu. A track, an album, a channel or a
// discovery result that is not in the library gets none, so those tiles keep
// the browser's own menu.
export function mediaMenuTarget(
    item: { id: string; type?: string | null } | null | undefined,
    serverId?: string | null,
    callbacks: MenuCallbacks = {},
): MediaMenuTarget | undefined {
    if (!item?.id || !item.type || !TYPES_WITH_A_MENU.has(item.type)) return undefined;
    return { id: item.id, serverId: serverId ?? undefined, ...callbacks };
}
