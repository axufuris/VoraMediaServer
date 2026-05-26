export type MusicView = 'artists' | 'artist' | 'album' | 'likes' | 'top' | 'mix' | 'recap' | 'genres' | 'genre';

export interface MusicNavState {
    view: MusicView;
    artistId?: string;
    albumId?: string;
    mixId?: string;
    year?: number;
    genre?: string;
}
