import { type ArtistVM } from '../../../../api/Music/musicService';
import MediaCard from '../../../../components/Client/Primitives/MediaCard';
import MediaGrid from '../../../../components/Client/Primitives/MediaGrid';
import EmptyState from '../../../../components/Client/Primitives/EmptyState';

interface MusicArtistsGridProps {
    isLoading: boolean;
    artists: ArtistVM[];
    isServerAdmin: boolean;
    onOpenArtist: (artist: ArtistVM) => void;
    onEditArtist: (artist: ArtistVM) => void;
}

export default function MusicArtistsGrid({ isLoading, artists, isServerAdmin, onOpenArtist, onEditArtist }: MusicArtistsGridProps) {
    if (isLoading && artists.length === 0) {
        return (
            <MediaGrid className="px-8" size="xs">
                {Array.from({ length: 12 }, (_, i) => <div key={i} className="vora-skeleton aspect-square rounded-full" aria-hidden="true" />)}
            </MediaGrid>
        );
    }

    if (artists.length === 0) {
        return (
            <EmptyState
                title="No artists yet"
                description="Create a Music library in Server Settings, point it at a folder of audio files, then trigger a scan."
            />
        );
    }

    return (
        <MediaGrid
            className="px-8"
            size="xs"
            title="All Artists"
            subtitle={`${artists.length} ${artists.length === 1 ? 'artist' : 'artists'}`}
        >
            {artists.map(artist => (
                <div key={artist.id} className="group/artist relative">
                    <MediaCard
                        item={{ type: 'Artist', title: artist.name }}
                        imageUrl={artist.artworkUrl}
                        shape="circle"
                        size="xs"
                        fill
                        onClick={() => onOpenArtist(artist)}
                    />
                    {isServerAdmin && (
                        <button
                            type="button"
                            onClick={() => onEditArtist(artist)}
                            title="Edit artist"
                            aria-label={`Edit ${artist.name}`}
                            className="vora-icon-button absolute right-0 top-0 inline-flex h-7 w-7 cursor-pointer items-center justify-center rounded-full opacity-0 transition-opacity focus-visible:opacity-100 group-hover/artist:opacity-100"
                            style={{ background: 'var(--vora-bg-raised)', color: 'var(--vora-text-secondary)', boxShadow: 'var(--vora-shadow-md)' }}
                        >
                            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
                                <path d="M15.232 5.232l3.536 3.536m-2.036-5.036a2.5 2.5 0 113.536 3.536L6.5 21.036H3v-3.572L16.732 3.732z" />
                            </svg>
                        </button>
                    )}
                </div>
            ))}
        </MediaGrid>
    );
}
