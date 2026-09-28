import { type GenreSummaryVM } from '../../../../api/Music/musicService';
import { type MusicNavState } from './musicNavState';
import MediaGrid from '../../../../components/Client/Primitives/MediaGrid';
import GenreTile from './GenreTile';

interface MusicGenresViewProps {
    isLoading: boolean;
    genres: GenreSummaryVM[];
    updateNav: (next: MusicNavState) => void;
}

export default function MusicGenresView({ isLoading, genres, updateNav }: MusicGenresViewProps) {
    if (isLoading) {
        return <div className="text-[var(--vora-text-muted)] py-12 text-center">Loading genres...</div>;
    }
    if (genres.length === 0) {
        return (
            <div className="text-[var(--vora-text-muted)] py-12 text-center bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] rounded-lg">
                <p className="mb-2">No genres in your library yet.</p>
                <p className="text-xs">Genres come from album metadata — try a library scan if you've added new music.</p>
            </div>
        );
    }

    return (
        <>
            <h2 className="text-lg font-bold text-[var(--vora-text-primary)] mb-4">Browse by Genre</h2>
            <MediaGrid size="xs">
                {genres.map(g => (
                    <GenreTile
                        key={g.name}
                        name={g.name}
                        artworkUrl={g.sampleArtworkUrl}
                        detail={`${g.trackCount} tracks · ${g.artistCount} ${g.artistCount === 1 ? 'artist' : 'artists'}`}
                        onClick={() => updateNav({ view: 'genre', genre: g.name })}
                    />
                ))}
            </MediaGrid>
        </>
    );
}
