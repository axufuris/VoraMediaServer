import { type GenreSummaryVM } from '../../../../api/Music/musicService';
import { type MusicNavState } from './musicNavState';

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
            <div className="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 gap-4">
                {genres.map(g => (
                    <button
                        key={g.name}
                        type="button"
                        onClick={() => updateNav({ view: 'genre', genre: g.name })}
                        className="relative aspect-square rounded-lg overflow-hidden border border-[var(--vora-border-subtle)] hover:border-emerald-400 transition-all cursor-pointer group bg-gradient-to-br from-emerald-700 via-teal-900 to-indigo-900"
                        title={g.name}
                    >
                        {g.sampleArtworkUrl && (
                            <img src={g.sampleArtworkUrl} alt="" className="absolute inset-0 w-full h-full object-cover opacity-30 group-hover:opacity-40 transition-opacity" />
                        )}
                        <div className="absolute inset-0 flex flex-col items-center justify-center p-3 text-center">
                            <div className="text-xl sm:text-2xl font-bold text-[var(--vora-text-primary)] drop-shadow-md">{g.name}</div>
                            <div className="text-xs text-emerald-200/90 mt-1">{g.trackCount} tracks · {g.artistCount} {g.artistCount === 1 ? 'artist' : 'artists'}</div>
                        </div>
                    </button>
                ))}
            </div>
        </>
    );
}
