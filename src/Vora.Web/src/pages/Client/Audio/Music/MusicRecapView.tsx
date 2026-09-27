import { type YearRecapVM } from '../../../../api/Music/musicService';
import { type MusicNavState } from './musicNavState';

interface MusicRecapViewProps {
    isLoading: boolean;
    currentRecap: YearRecapVM | null;
    availableYears: number[];
    updateNav: (next: MusicNavState) => void;
}

export default function MusicRecapView({ isLoading, currentRecap, availableYears, updateNav }: MusicRecapViewProps) {
    if (isLoading) {
        return <div className="text-[var(--vora-text-muted)] py-12 text-center">Loading your year...</div>;
    }
    if (!currentRecap) {
        return <div className="text-[var(--vora-text-muted)] py-12 text-center">Couldn't load recap.</div>;
    }

    return (
        <>
            <div className="flex flex-col sm:flex-row items-center sm:items-end gap-4 sm:gap-6 mb-8 pb-6 border-b border-[var(--vora-border-subtle)] text-center sm:text-left">
                <div className="w-32 h-32 sm:w-40 sm:h-40 rounded bg-gradient-to-br from-pink-600 via-amber-500 to-purple-700 border border-pink-400/30 flex items-center justify-center shrink-0 shadow-lg">
                    <div className="text-5xl sm:text-6xl font-bold text-[var(--vora-text-primary)] drop-shadow-lg">{currentRecap.year}</div>
                </div>
                <div className="flex-1 min-w-0">
                    <div className="text-xs uppercase tracking-widest text-[var(--vora-text-secondary)] font-bold mb-1">Year in Music</div>
                    <h2 className="text-3xl sm:text-4xl font-bold text-[var(--vora-text-primary)]">{currentRecap.year} in music</h2>
                    <p className="text-sm text-[var(--vora-text-secondary)] mt-2">
                        {currentRecap.totalPlays.toLocaleString()} plays · {formatHours(currentRecap.totalListeningSeconds)} of listening
                    </p>
                    {availableYears.length > 1 && (
                        <div className="mt-3 inline-flex gap-1 flex-wrap">
                            {availableYears.map(y => (
                                <button
                                    key={y}
                                    type="button"
                                    onClick={() => updateNav({ view: 'recap', year: y })}
                                    className={`text-xs px-3 py-1 rounded font-bold transition-colors cursor-pointer ${currentRecap.year === y ? 'bg-pink-600 text-[var(--vora-text-primary)]' : 'bg-[var(--vora-bg-surface)] text-[var(--vora-text-secondary)] hover:text-[var(--vora-text-primary)] hover:bg-[var(--vora-bg-raised)]'}`}
                                >
                                    {y}
                                </button>
                            ))}
                        </div>
                    )}
                </div>
            </div>

            {currentRecap.totalPlays === 0 ? (
                <div className="text-[var(--vora-text-muted)] py-12 text-center bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] rounded-lg">
                    <p className="mb-2">No plays in {currentRecap.year} yet.</p>
                    <p className="text-xs">Listen to some music and check back.</p>
                </div>
            ) : (
                <>
                    <div className="grid grid-cols-2 sm:grid-cols-4 gap-4 mb-8">
                        <StatBlock label="Tracks played" value={currentRecap.distinctTrackCount.toLocaleString()} />
                        <StatBlock label="Artists" value={currentRecap.distinctArtistCount.toLocaleString()} />
                        <StatBlock label="Albums" value={currentRecap.distinctAlbumCount.toLocaleString()} />
                        <StatBlock label="Peak day" value={currentRecap.peakDayOfWeekLabel ?? '—'} />
                    </div>

                    {currentRecap.topArtists.length > 0 && (
                        <div className="mb-8">
                            <h3 className="text-lg font-bold text-[var(--vora-text-primary)] mb-3">Top Artists</h3>
                            <div className="flex gap-4 overflow-x-auto pb-2 -mx-2 px-2">
                                {currentRecap.topArtists.map((a, idx) => (
                                    <button
                                        key={a.id}
                                        type="button"
                                        onClick={() => updateNav({ view: 'artist', artistId: a.id })}
                                        className="w-28 sm:w-32 shrink-0 group text-left cursor-pointer"
                                    >
                                        <div className="w-full aspect-square rounded-full bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] group-hover:border-pink-400 transition-all overflow-hidden mb-2 relative">
                                            {a.artworkUrl
                                                ? <img src={a.artworkUrl} alt="" className="w-full h-full object-cover" />
                                                : <div className="w-full h-full flex items-center justify-center text-[var(--vora-text-disabled)]"><svg className="w-10 h-10" fill="currentColor" viewBox="0 0 24 24"><path d="M12 12c2.21 0 4-1.79 4-4s-1.79-4-4-4-4 1.79-4 4 1.79 4 4 4zm0 2c-2.67 0-8 1.34-8 4v2h16v-2c0-2.66-5.33-4-8-4z" /></svg></div>}
                                            <div className="absolute top-1 left-1 bg-pink-600/90 text-[var(--vora-text-primary)] text-xs font-bold rounded px-1.5 py-0.5">#{idx + 1}</div>
                                        </div>
                                        <div className="text-sm font-bold text-[var(--vora-text-primary)] truncate text-center">{a.name}</div>
                                        <div className="text-xs text-[var(--vora-text-muted)] text-center">{a.playCount} plays</div>
                                    </button>
                                ))}
                            </div>
                        </div>
                    )}

                    {currentRecap.topTracks.length > 0 && (
                        <div className="mb-8">
                            <h3 className="text-lg font-bold text-[var(--vora-text-primary)] mb-3">Top Tracks</h3>
                            <div className="space-y-1">
                                {currentRecap.topTracks.map((t, idx) => (
                                    <div key={t.id} className="flex items-center gap-3 p-2 bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] rounded">
                                        <div className="w-8 text-right text-sm text-[var(--vora-text-muted)] tabular-nums shrink-0 font-bold">{idx + 1}</div>
                                        <div className="w-10 h-10 rounded bg-[var(--vora-bg-canvas)] border border-[var(--vora-border-subtle)] overflow-hidden shrink-0">
                                            {t.albumArtworkUrl
                                                ? <img src={t.albumArtworkUrl} alt="" className="w-full h-full object-cover" />
                                                : <div className="w-full h-full flex items-center justify-center text-[var(--vora-text-disabled)]"><svg className="w-5 h-5" fill="currentColor" viewBox="0 0 24 24"><path d="M12 3v10.55c-.59-.34-1.27-.55-2-.55-2.21 0-4 1.79-4 4s1.79 4 4 4 4-1.79 4-4V7h4V3h-6z" /></svg></div>}
                                        </div>
                                        <div className="flex-1 min-w-0">
                                            <div className="text-sm text-[var(--vora-text-primary)] truncate">{t.title}</div>
                                            <div className="text-xs text-[var(--vora-text-muted)] truncate">{t.artist ?? t.albumTitle ?? ''}</div>
                                        </div>
                                        <div className="text-xs text-pink-400 font-bold shrink-0">{t.playCount} plays</div>
                                    </div>
                                ))}
                            </div>
                        </div>
                    )}

                    {currentRecap.topGenres.length > 0 && (
                        <div className="mb-8">
                            <h3 className="text-lg font-bold text-[var(--vora-text-primary)] mb-3">Top Genres</h3>
                            <div className="space-y-2 bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] rounded-lg p-4">
                                {currentRecap.topGenres.map(g => (
                                    <div key={g.name}>
                                        <div className="flex justify-between text-sm mb-1">
                                            <span className="text-[var(--vora-text-primary)]">{g.name}</span>
                                            <span className="text-[var(--vora-text-muted)]">{g.percent}% · {g.playCount} plays</span>
                                        </div>
                                        <div className="h-2 bg-[var(--vora-bg-surface)] rounded-full overflow-hidden">
                                            <div className="h-full bg-gradient-to-r from-pink-500 to-amber-500" style={{ width: `${g.percent}%` }} />
                                        </div>
                                    </div>
                                ))}
                            </div>
                        </div>
                    )}

                    {currentRecap.newDiscoveries.length > 0 && (
                        <div className="mb-8">
                            <h3 className="text-lg font-bold text-[var(--vora-text-primary)] mb-3">New Discoveries</h3>
                            <p className="text-xs text-[var(--vora-text-muted)] mb-3">Artists you played for the first time in {currentRecap.year}</p>
                            <div className="flex gap-4 overflow-x-auto pb-2 -mx-2 px-2">
                                {currentRecap.newDiscoveries.map(a => (
                                    <button
                                        key={a.id}
                                        type="button"
                                        onClick={() => updateNav({ view: 'artist', artistId: a.id })}
                                        className="w-24 sm:w-28 shrink-0 group text-left cursor-pointer"
                                    >
                                        <div className="w-full aspect-square rounded-full bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] group-hover:border-orange-400 transition-all overflow-hidden mb-2">
                                            {a.artworkUrl
                                                ? <img src={a.artworkUrl} alt="" className="w-full h-full object-cover" />
                                                : <div className="w-full h-full flex items-center justify-center text-[var(--vora-text-disabled)]"><svg className="w-8 h-8" fill="currentColor" viewBox="0 0 24 24"><path d="M12 12c2.21 0 4-1.79 4-4s-1.79-4-4-4-4 1.79-4 4 1.79 4 4 4zm0 2c-2.67 0-8 1.34-8 4v2h16v-2c0-2.66-5.33-4-8-4z" /></svg></div>}
                                        </div>
                                        <div className="text-xs font-bold text-[var(--vora-text-primary)] truncate text-center">{a.name}</div>
                                    </button>
                                ))}
                            </div>
                        </div>
                    )}

                    <div className="grid grid-cols-1 lg:grid-cols-2 gap-4 mb-8">
                        <DistributionBlock title="Plays by day of week" labels={['Sun','Mon','Tue','Wed','Thu','Fri','Sat']} values={currentRecap.playsByDayOfWeek} />
                        <DistributionBlock title="Plays by hour" labels={Array.from({length:24}, (_,i) => i.toString())} values={currentRecap.playsByHour} compact />
                    </div>
                    {currentRecap.peakHourLabel && (
                        <div className="text-sm text-[var(--vora-text-muted)] mb-4">Most active around <span className="text-pink-400 font-bold">{currentRecap.peakHourLabel}</span></div>
                    )}
                </>
            )}
        </>
    );
}

function formatHours(totalSeconds: number): string {
    if (totalSeconds <= 0) return '0 minutes';
    const hours = Math.floor(totalSeconds / 3600);
    const minutes = Math.floor((totalSeconds % 3600) / 60);
    if (hours === 0) return `${minutes} ${minutes === 1 ? 'minute' : 'minutes'}`;
    if (hours < 100) return `${hours}h ${minutes}m`;
    return `${hours.toLocaleString()} hours`;
}

function StatBlock({ label, value }: { label: string; value: string | number }) {
    return (
        <div className="bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] rounded-lg p-4 text-center">
            <div className="text-2xl sm:text-3xl font-bold text-[var(--vora-text-primary)]">{value}</div>
            <div className="text-xs uppercase tracking-widest text-[var(--vora-text-muted)] mt-1">{label}</div>
        </div>
    );
}

function DistributionBlock({ title, labels, values, compact }: { title: string; labels: string[]; values: number[]; compact?: boolean }) {
    const max = Math.max(...values, 1);
    return (
        <div className="bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] rounded-lg p-4">
            <h4 className="text-sm font-bold text-[var(--vora-text-primary)] mb-3">{title}</h4>
            <div className="flex items-end gap-1 h-24">
                {values.map((v, i) => (
                    <div key={i} className="flex-1 flex flex-col items-center justify-end h-full">
                        <div
                            className="w-full bg-gradient-to-t from-pink-600 to-amber-500 rounded-sm transition-all"
                            style={{ height: `${(v / max) * 100}%` }}
                            title={`${labels[i]}: ${v} plays`}
                        />
                    </div>
                ))}
            </div>
            <div className="flex gap-1 mt-2">
                {labels.map((l, i) => (
                    <div key={i} className={`flex-1 text-center text-[var(--vora-text-muted)] ${compact ? 'text-[9px]' : 'text-xs'}`}>{compact && i % 3 !== 0 ? '' : l}</div>
                ))}
            </div>
        </div>
    );
}
