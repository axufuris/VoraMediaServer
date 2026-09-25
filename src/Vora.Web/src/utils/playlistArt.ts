import type { PlaylistSummaryVM } from '../api/Collections/playlistService';

// What a playlist tile shows: the owner's cover when there is one, otherwise a
// mosaic of the items' art. Backdrops first for the cinematic look on film and
// TV playlists; songs carry no backdrop, so music falls back to album covers.
export function playlistTileArt(p: PlaylistSummaryVM): { imageUrl?: string; mosaicUrls?: string[] } {
    if (p.imageUrl) return { imageUrl: p.imageUrl };
    const mosaic = p.backdropUrls.length > 0 ? p.backdropUrls : p.posterUrls;
    return { mosaicUrls: mosaic, imageUrl: mosaic[0] };
}
