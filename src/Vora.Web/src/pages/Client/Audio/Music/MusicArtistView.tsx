import { type ArtistVM, type AlbumVM, type RadioSeed } from '../../../../api/Music/musicService';
import StarRating from '../../../../components/Client/Primitives/StarRating';
import RatedBadge from '../../../../components/Client/Primitives/RatedBadge';
import { type MusicNavState } from './musicNavState';

interface MusicArtistViewProps {
    isLoading: boolean;
    currentArtist: ArtistVM | null;
    albums: AlbumVM[];
    similarArtists: ArtistVM[];
    isServerAdmin: boolean;
    playArtist: (shuffle: boolean) => Promise<void>;
    startRadioFromSeed: (seed: RadioSeed) => Promise<void>;
    handleSetArtistRating: (artistId: string, next: number | null) => Promise<void>;
    onEditArtist: (artist: ArtistVM) => void;
    onEditAlbum: (album: AlbumVM) => void;
    updateNav: (next: MusicNavState) => void;
    currentArtistId: string | undefined;
}

export default function MusicArtistView({
    isLoading,
    currentArtist,
    albums,
    similarArtists,
    isServerAdmin,
    playArtist,
    startRadioFromSeed,
    handleSetArtistRating,
    onEditArtist,
    onEditAlbum,
    updateNav,
    currentArtistId,
}: MusicArtistViewProps) {
    return (
        <>
            {currentArtist && (() => {
                const heroBackdrop = currentArtist.bannerUrl || currentArtist.backgroundUrl;
                const hasArtwork = !!currentArtist.artworkUrl;
                return (
                    <div className="relative w-full rounded-lg overflow-hidden mb-6 border border-[var(--vora-border-subtle)] bg-[var(--vora-bg-sunken)]" style={{ minHeight: '13rem' }}>
                        {heroBackdrop ? (
                            <img src={heroBackdrop} alt="" className="absolute inset-0 w-full h-full object-cover" />
                        ) : (
                            <div className="absolute inset-0 bg-gradient-to-br from-gray-900 via-gray-900 to-gray-800" />
                        )}
                        <div className="absolute inset-0 bg-gradient-to-t from-gray-950 via-gray-950/55 to-gray-950/20" />
                        <div className="absolute inset-0 bg-gradient-to-r from-gray-950/40 via-transparent to-gray-950/40" />

                        <div className="relative flex flex-col sm:flex-row items-stretch sm:items-end gap-4 p-5 sm:p-6 min-h-[13rem]">
                            {hasArtwork && (
                                <div className="w-28 h-28 sm:w-36 sm:h-36 rounded-full bg-[var(--vora-bg-canvas)] border border-[var(--vora-border-subtle)]/80 overflow-hidden shrink-0 shadow-lg self-start sm:self-end">
                                    <img src={currentArtist.artworkUrl} alt={currentArtist.name} className="w-full h-full object-cover" />
                                </div>
                            )}
                            <div className="flex-1 min-w-0 flex flex-col justify-end gap-3">
                                <div>
                                    <div className="text-xs uppercase tracking-widest text-[var(--vora-text-secondary)] font-bold mb-1">Artist</div>
                                    {currentArtist.clearLogoUrl ? (
                                        <img
                                            src={currentArtist.clearLogoUrl}
                                            alt={currentArtist.name}
                                            className="max-h-16 sm:max-h-20 max-w-full object-contain drop-shadow-[0_2px_6px_rgba(0,0,0,0.8)]"
                                            style={{ objectPosition: 'left center' }}
                                        />
                                    ) : (
                                        <h2 className="text-3xl sm:text-4xl font-bold text-[var(--vora-text-primary)] drop-shadow-[0_2px_6px_rgba(0,0,0,0.8)] truncate">{currentArtist.name}</h2>
                                    )}
                                </div>
                                <div className="flex flex-wrap items-center gap-x-4 gap-y-1">
                                    <div className="flex items-center gap-2">
                                        <span className="text-[10px] font-bold uppercase tracking-widest text-[var(--vora-text-secondary)]">Your rating</span>
                                        <StarRating
                                            value={currentArtist.myRating ?? null}
                                            onChange={(next) => handleSetArtistRating(currentArtist.id, next)}
                                            showNumeric
                                        />
                                    </div>
                                    {currentArtist.serverAdminRating != null && !isServerAdmin && (
                                        <div className="flex items-center gap-2">
                                            <span className="text-[10px] font-bold uppercase tracking-widest text-[var(--vora-text-secondary)]">Server admin</span>
                                            <StarRating value={currentArtist.serverAdminRating} readOnly showNumeric color="var(--vora-accent-text)" />
                                        </div>
                                    )}
                                </div>
                                <div className="flex flex-wrap items-center gap-2">
                                    {albums.length > 0 && (
                                        <>
                                            <button
                                                type="button"
                                                onClick={() => playArtist(false)}
                                                className="text-sm px-4 py-2 bg-[var(--vora-accent-500)] hover:bg-[var(--vora-accent-500)] text-[var(--vora-text-primary)] font-bold rounded transition-colors cursor-pointer flex items-center gap-2"
                                                title="Play every track from every album"
                                            >
                                                <svg className="w-4 h-4" fill="currentColor" viewBox="0 0 24 24"><path d="M8 5v14l11-7z" /></svg>
                                                Play Artist
                                            </button>
                                            <button
                                                type="button"
                                                onClick={() => playArtist(true)}
                                                className="text-sm px-4 py-2 bg-[var(--vora-bg-sunken)] hover:bg-[var(--vora-bg-surface)] text-[var(--vora-text-primary)] hover:text-[var(--vora-text-primary)] rounded transition-colors cursor-pointer flex items-center gap-2 backdrop-blur-sm border border-[var(--vora-border-subtle)]/60"
                                                title="Shuffle every track from every album"
                                            >
                                                <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 4l5 5m0 0V5m0 4H5m11-4l5 5m0 0V5m0 4h-4m-2 7l7 7m-7-7l-7 7m14 0v-4m0 4h-4" /></svg>
                                                Shuffle Artist
                                            </button>
                                            <button
                                                type="button"
                                                onClick={() => startRadioFromSeed({ seedKind: 'Artist', seedArtistId: currentArtist.id })}
                                                className="text-sm px-4 py-2 bg-[var(--vora-bg-sunken)] hover:bg-[var(--vora-bg-surface)] text-[var(--vora-text-primary)] hover:text-[var(--vora-text-primary)] rounded transition-colors cursor-pointer flex items-center gap-2 backdrop-blur-sm border border-[var(--vora-border-subtle)]/60"
                                                title="Start an endless radio station seeded by this artist"
                                            >
                                                <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9.348 14.652a3.75 3.75 0 010-5.304m5.304 0a3.75 3.75 0 010 5.304m-7.425 2.121a6.75 6.75 0 010-9.546m9.546 0a6.75 6.75 0 010 9.546M5.106 18.894c-3.808-3.807-3.808-9.98 0-13.788m13.788 0c3.808 3.807 3.808 9.98 0 13.788M12 12h.01" /></svg>
                                                Start Radio
                                            </button>
                                        </>
                                    )}
                                    {isServerAdmin && (
                                        <button
                                            type="button"
                                            onClick={() => onEditArtist(currentArtist)}
                                            className="text-xs px-3 py-1.5 bg-[var(--vora-bg-sunken)] hover:bg-[var(--vora-bg-surface)] text-[var(--vora-text-primary)] hover:text-[var(--vora-text-primary)] rounded transition-colors cursor-pointer flex items-center gap-1 backdrop-blur-sm border border-[var(--vora-border-subtle)]/60"
                                        >
                                            <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15.232 5.232l3.536 3.536m-2.036-5.036a2.5 2.5 0 113.536 3.536L6.5 21.036H3v-3.572L16.732 3.732z" /></svg>
                                            Edit Artist
                                        </button>
                                    )}
                                </div>
                            </div>
                        </div>
                    </div>
                );
            })()}

            {isLoading ? (
                <div className="text-[var(--vora-text-muted)] py-12 text-center">Loading albums...</div>
            ) : albums.length === 0 ? (
                <div className="text-[var(--vora-text-muted)] py-12 text-center">No albums for this artist.</div>
            ) : (
                <>
                    <div className="grid grid-cols-[repeat(auto-fill,minmax(9rem,1fr))] gap-4">
                        {albums.map(album => (
                            <div
                                key={album.id}
                                onClick={() => updateNav({ view: 'album', artistId: currentArtistId, albumId: album.id })}
                                className="relative flex flex-col bg-[var(--vora-bg-sunken)] hover:bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] hover:border-[var(--vora-accent-500)] rounded-lg p-3 transition-all cursor-pointer text-left"
                            >
                                {isServerAdmin && (
                                    <button
                                        type="button"
                                        onClick={(e) => { e.stopPropagation(); onEditAlbum(album); }}
                                        className="absolute top-2 right-2 p-1.5 rounded bg-[var(--vora-bg-canvas)]/80 text-[var(--vora-text-muted)] hover:text-[var(--vora-accent-text)] transition-colors cursor-pointer z-10"
                                        title="Edit album"
                                    >
                                        <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15.232 5.232l3.536 3.536m-2.036-5.036a2.5 2.5 0 113.536 3.536L6.5 21.036H3v-3.572L16.732 3.732z" /></svg>
                                    </button>
                                )}
                                <div className="relative aspect-square rounded bg-[var(--vora-bg-canvas)] border border-[var(--vora-border-subtle)] flex items-center justify-center overflow-hidden mb-3">
                                    {album.artworkUrl
                                        ? <img src={album.artworkUrl} alt={album.title} className="max-w-full max-h-full object-cover" />
                                        : <svg className="w-10 h-10 text-[var(--vora-text-disabled)]" fill="currentColor" viewBox="0 0 24 24"><path d="M12 3v10.55c-.59-.34-1.27-.55-2-.55-2.21 0-4 1.79-4 4s1.79 4 4 4 4-1.79 4-4V7h4V3h-6z" /></svg>}
                                    {(album.myRating ?? album.serverAdminRating) != null && (
                                        <div className="absolute left-2 bottom-2">
                                            <RatedBadge
                                                value={(album.myRating ?? album.serverAdminRating)!}
                                                title={album.myRating != null ? `Your rating: ${Math.round(album.myRating)} of 10` : `Server admin rating: ${Math.round(album.serverAdminRating!)} of 10`}
                                            />
                                        </div>
                                    )}
                                </div>
                                <div className="font-bold text-sm text-[var(--vora-text-primary)] truncate" title={album.title}>{album.title}</div>
                                <div className="text-xs text-[var(--vora-text-muted)]">{album.year || ''}</div>
                            </div>
                        ))}
                    </div>
                    {similarArtists.length > 0 && (
                        <div className="mt-10">
                            <h3 className="text-lg font-bold text-[var(--vora-text-primary)] mb-3">Fans Also Listen To</h3>
                            <div className="flex gap-4 overflow-x-auto pb-2 -mx-2 px-2">
                                {similarArtists.map(a => (
                                    <button
                                        key={a.id}
                                        type="button"
                                        onClick={() => updateNav({ view: 'artist', artistId: a.id })}
                                        className="w-28 sm:w-32 shrink-0 group text-left cursor-pointer"
                                        title={a.name}
                                    >
                                        <div className="w-full aspect-square rounded-full bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] group-hover:border-[var(--vora-accent-500)] transition-all overflow-hidden mb-2">
                                            {a.artworkUrl
                                                ? <img src={a.artworkUrl} alt="" className="w-full h-full object-cover" />
                                                : <div className="w-full h-full flex items-center justify-center text-[var(--vora-text-disabled)]"><svg className="w-10 h-10" fill="currentColor" viewBox="0 0 24 24"><path d="M12 12c2.21 0 4-1.79 4-4s-1.79-4-4-4-4 1.79-4 4 1.79 4 4 4zm0 2c-2.67 0-8 1.34-8 4v2h16v-2c0-2.66-5.33-4-8-4z" /></svg></div>}
                                        </div>
                                        <div className="text-sm font-bold text-[var(--vora-text-primary)] truncate text-center" title={a.name}>{a.name}</div>
                                    </button>
                                ))}
                            </div>
                        </div>
                    )}
                </>
            )}
        </>
    );
}
