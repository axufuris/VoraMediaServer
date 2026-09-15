export type MusicView = 'root' | 'artist' | 'album' | 'likes' | 'top' | 'mix' | 'recap' | 'genres' | 'genre';

export interface MusicNavState {
    view: MusicView;
    artistId?: string;
    albumId?: string;
    mixId?: string;
    year?: number;
    genre?: string;
}

const MUSIC_VIEWS: readonly MusicView[] = ['root', 'artist', 'album', 'likes', 'top', 'mix', 'recap', 'genres', 'genre'];

// Stored state can predate a rename (the hub used to be the 'artists' view), so
// anything unrecognised lands back on the sub-tab root instead of a blank page.
export function parseMusicNavState(raw: string): MusicNavState {
    try {
        const parsed = JSON.parse(raw) as Partial<MusicNavState>;
        if (parsed && typeof parsed.view === 'string' && MUSIC_VIEWS.includes(parsed.view)) {
            return parsed as MusicNavState;
        }
    } catch {
        /* fall through to the root view */
    }
    return { view: 'root' };
}
