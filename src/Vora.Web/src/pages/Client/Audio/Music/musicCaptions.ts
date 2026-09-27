import type { AlbumVM, ArtistTrackVM } from '../../../../api/Music/musicService';
import type { PosterCaptionItem } from '../../../../utils/posterCaption';

export const albumCaption = (album: AlbumVM): PosterCaptionItem => ({
    type: 'Album',
    title: album.title,
    artistName: album.artistName,
    albumTitle: album.title,
    releaseDate: album.year ? `${album.year}-01-01` : null,
});

export const trackCaption = (track: ArtistTrackVM): PosterCaptionItem => ({
    type: 'Track',
    title: track.title,
    artistName: track.artist,
    albumTitle: track.albumTitle,
});
