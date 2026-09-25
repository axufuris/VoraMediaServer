import { thumbUrl } from '../../utils/thumbnails';

// A playlist's cover: the owner's upload when there is one, otherwise a mosaic
// of the first four different images from its items, the way music apps build
// one. Square for music, like an album; the poster shape for film and TV.
interface PlaylistCoverProps {
    imageUrl?: string | null;
    posterUrls: string[];
    shape: 'square' | 'poster';
    className?: string;
}

export default function PlaylistCover({ imageUrl, posterUrls, shape, className = '' }: PlaylistCoverProps) {
    const aspect = shape === 'square' ? 'aspect-square' : 'aspect-[2/3]';
    const tiles = [...new Set(posterUrls)].slice(0, 4);

    return (
        <div
            className={`${aspect} w-full overflow-hidden rounded-lg border shadow-2xl ${className}`}
            style={{ borderColor: 'var(--vora-border-subtle)', background: 'var(--vora-bg-sunken)' }}
        >
            {imageUrl ? (
                <img src={imageUrl} alt="" className="h-full w-full object-cover" />
            ) : tiles.length >= 4 ? (
                <div className="grid h-full w-full grid-cols-2 grid-rows-2">
                    {tiles.map(url => <img key={url} src={thumbUrl(url, 320)} alt="" className="h-full w-full object-cover" />)}
                </div>
            ) : tiles.length > 0 ? (
                <img src={thumbUrl(tiles[0], 640)} alt="" className="h-full w-full object-cover" />
            ) : (
                <div className="flex h-full w-full items-center justify-center" style={{ color: 'var(--vora-text-muted)' }}>
                    <svg className="h-16 w-16" fill="currentColor" viewBox="0 0 24 24" aria-hidden="true">
                        <path d="M3 5h12v2H3V5zm0 4h12v2H3V9zm0 4h8v2H3v-2zm14-8h4v2h-2v8.5a2.5 2.5 0 11-2-2.45V5z" />
                    </svg>
                </div>
            )}
        </div>
    );
}
