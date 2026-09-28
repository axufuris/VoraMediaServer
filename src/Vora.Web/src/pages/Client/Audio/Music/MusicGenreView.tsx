import { type AlbumVM, type GenreContentVM } from '../../../../api/Music/musicService';
import { type MusicNavState } from './musicNavState';
import MediaCard from '../../../../components/Client/Primitives/MediaCard';
import MediaRow, { MediaRowItem } from '../../../../components/Client/Primitives/MediaRow';
import { AlbumBrowser } from './MusicAlbumsView';

interface MusicGenreViewProps {
    isLoading: boolean;
    currentGenre: GenreContentVM | null;
    serverId?: string;
    refreshKey: number;
    updateNav: (next: MusicNavState) => void;
}

const plural = (count: number, word: string) => `${count} ${word}${count === 1 ? '' : 's'}`;

export default function MusicGenreView({ isLoading, currentGenre, serverId, refreshKey, updateNav }: MusicGenreViewProps) {
    if (isLoading) {
        return <div className="text-[var(--vora-text-muted)] py-12 text-center">Loading genre...</div>;
    }
    if (!currentGenre) {
        return <div className="text-[var(--vora-text-muted)] py-12 text-center">Genre not found.</div>;
    }

    const openAlbum = (album: AlbumVM) => updateNav({ view: 'album', artistId: album.artistId, albumId: album.id });

    return (
        <>
            <div className="mb-8 flex items-center gap-4 border-b border-[var(--vora-border-subtle)] pb-6">
                <span
                    className="inline-flex h-16 w-16 shrink-0 items-center justify-center rounded-[var(--vora-radius-md)]"
                    style={{ background: 'var(--vora-accent-soft)', color: 'var(--vora-accent-text)' }}
                    aria-hidden="true"
                >
                    <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.75}><rect x="3.5" y="3.5" width="7" height="7" rx="1.5" /><rect x="13.5" y="3.5" width="7" height="7" rx="1.5" /><rect x="3.5" y="13.5" width="7" height="7" rx="1.5" /><rect x="13.5" y="13.5" width="7" height="7" rx="1.5" /></svg>
                </span>
                <div className="min-w-0">
                    <div className="text-xs font-bold uppercase tracking-widest text-[var(--vora-text-secondary)]">Genre</div>
                    <h2 className="truncate text-3xl font-bold text-[var(--vora-text-primary)]">{currentGenre.name}</h2>
                    <p className="mt-1 text-sm text-[var(--vora-text-secondary)]">
                        {plural(currentGenre.artists.length, 'artist')} · {plural(currentGenre.albumCount, 'album')}
                    </p>
                </div>
            </div>

            {currentGenre.artists.length > 0 && (
                <div className="mb-8">
                    <MediaRow title="Artists" variant="section">
                        {currentGenre.artists.map(artist => (
                            <MediaRowItem key={artist.id}>
                                <MediaCard
                                    title={artist.name}
                                    imageUrl={artist.artworkUrl}
                                    shape="circle"
                                    size="xs"
                                    onClick={() => updateNav({ view: 'artist', artistId: artist.id })}
                                />
                            </MediaRowItem>
                        ))}
                    </MediaRow>
                </div>
            )}

            <AlbumBrowser
                title="Albums"
                genre={currentGenre.name}
                serverId={serverId}
                refreshKey={refreshKey}
                padded={false}
                onOpenAlbum={openAlbum}
            />
        </>
    );
}
