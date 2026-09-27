import type { AlbumVM } from '../api/Music/musicService';

export function albumCover(album: Pick<AlbumVM, 'artworkUrl' | 'artistArtworkUrl'>): string | undefined {
    if (album.artworkUrl?.trim()) return album.artworkUrl;
    if (album.artistArtworkUrl?.trim()) return album.artistArtworkUrl;
    return undefined;
}
