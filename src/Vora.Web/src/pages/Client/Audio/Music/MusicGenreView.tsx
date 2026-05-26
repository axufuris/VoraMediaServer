import { type GenreContentVM } from '../../../../api/Music/musicService';
import { type MusicNavState } from './musicNavState';

interface MusicGenreViewProps {
    isLoading: boolean;
    currentGenre: GenreContentVM | null;
    updateNav: (next: MusicNavState) => void;
}

export default function MusicGenreView({ isLoading, currentGenre, updateNav }: MusicGenreViewProps) {
    if (isLoading) {
        return <div className="text-[var(--vora-text-muted)] py-12 text-center">Loading genre...</div>;
    }
    if (!currentGenre) {
        return <div className="text-[var(--vora-text-muted)] py-12 text-center">Genre not found.</div>;
    }

    return (
        <>
            <div className="flex flex-col sm:flex-row items-center sm:items-end gap-4 sm:gap-6 mb-8 pb-6 border-b border-[var(--vora-border-subtle)] text-center sm:text-left">
                <div className="w-32 h-32 sm:w-40 sm:h-40 rounded bg-gradient-to-br from-emerald-600 via-teal-700 to-indigo-800 border border-emerald-400/30 flex items-center justify-center shrink-0 shadow-lg">
                    <svg className="w-16 h-16 sm:w-20 sm:h-20 text-[var(--vora-text-primary)]" fill="none" stroke="currentColor" strokeWidth={1.5} viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" d="M3.75 6A2.25 2.25 0 016 3.75h2.25A2.25 2.25 0 0110.5 6v2.25a2.25 2.25 0 01-2.25 2.25H6a2.25 2.25 0 01-2.25-2.25V6zM3.75 15.75A2.25 2.25 0 016 13.5h2.25a2.25 2.25 0 012.25 2.25V18a2.25 2.25 0 01-2.25 2.25H6A2.25 2.25 0 013.75 18v-2.25zM13.5 6a2.25 2.25 0 012.25-2.25H18A2.25 2.25 0 0120.25 6v2.25A2.25 2.25 0 0118 10.5h-2.25a2.25 2.25 0 01-2.25-2.25V6zM13.5 15.75a2.25 2.25 0 012.25-2.25H18a2.25 2.25 0 012.25 2.25V18A2.25 2.25 0 0118 20.25h-2.25A2.25 2.25 0 0113.5 18v-2.25z" /></svg>
                </div>
                <div className="flex-1 min-w-0">
                    <div className="text-xs uppercase tracking-widest text-[var(--vora-text-secondary)] font-bold mb-1">Genre</div>
                    <h2 className="text-3xl sm:text-4xl font-bold text-[var(--vora-text-primary)]">{currentGenre.name}</h2>
                    <p className="text-sm text-[var(--vora-text-secondary)] mt-2">{currentGenre.artists.length} {currentGenre.artists.length === 1 ? 'artist' : 'artists'} · {currentGenre.albums.length} {currentGenre.albums.length === 1 ? 'album' : 'albums'}</p>
                </div>
            </div>

            {currentGenre.artists.length > 0 && (
                <div className="mb-8">
                    <h3 className="text-lg font-bold text-[var(--vora-text-primary)] mb-3">Artists</h3>
                    <div className="flex gap-4 overflow-x-auto pb-2 -mx-2 px-2">
                        {currentGenre.artists.map(a => (
                            <button
                                key={a.id}
                                type="button"
                                onClick={() => updateNav({ view: 'artist', artistId: a.id })}
                                className="w-28 sm:w-32 shrink-0 group text-left cursor-pointer"
                            >
                                <div className="w-full aspect-square rounded-full bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] group-hover:border-emerald-400 transition-all overflow-hidden mb-2">
                                    {a.artworkUrl
                                        ? <img src={a.artworkUrl} alt="" className="w-full h-full object-cover" />
                                        : <div className="w-full h-full flex items-center justify-center text-[var(--vora-text-disabled)]"><svg className="w-10 h-10" fill="currentColor" viewBox="0 0 24 24"><path d="M12 12c2.21 0 4-1.79 4-4s-1.79-4-4-4-4 1.79-4 4 1.79 4 4 4zm0 2c-2.67 0-8 1.34-8 4v2h16v-2c0-2.66-5.33-4-8-4z" /></svg></div>}
                                </div>
                                <div className="text-sm font-bold text-[var(--vora-text-primary)] truncate text-center">{a.name}</div>
                            </button>
                        ))}
                    </div>
                </div>
            )}

            {currentGenre.albums.length > 0 && (
                <div className="mb-8">
                    <h3 className="text-lg font-bold text-[var(--vora-text-primary)] mb-3">Albums</h3>
                    <div className="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 gap-4">
                        {currentGenre.albums.map(album => (
                            <div
                                key={album.id}
                                onClick={() => updateNav({ view: 'album', artistId: album.artistId, albumId: album.id })}
                                className="relative flex flex-col bg-[var(--vora-bg-sunken)] hover:bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] hover:border-emerald-400 rounded-lg p-3 transition-all cursor-pointer text-left"
                            >
                                <div className="aspect-square rounded bg-[var(--vora-bg-canvas)] border border-[var(--vora-border-subtle)] flex items-center justify-center overflow-hidden mb-3">
                                    {album.artworkUrl
                                        ? <img src={album.artworkUrl} alt={album.title} className="max-w-full max-h-full object-cover" />
                                        : <svg className="w-10 h-10 text-[var(--vora-text-disabled)]" fill="currentColor" viewBox="0 0 24 24"><path d="M12 3v10.55c-.59-.34-1.27-.55-2-.55-2.21 0-4 1.79-4 4s1.79 4 4 4 4-1.79 4-4V7h4V3h-6z" /></svg>}
                                </div>
                                <div className="font-bold text-sm text-[var(--vora-text-primary)] truncate" title={album.title}>{album.title}</div>
                                <div className="text-xs text-[var(--vora-text-muted)] truncate">{album.artistName}{album.year ? ` · ${album.year}` : ''}</div>
                            </div>
                        ))}
                    </div>
                </div>
            )}
        </>
    );
}
