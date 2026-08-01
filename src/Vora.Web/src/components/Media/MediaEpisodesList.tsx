import { useCallback, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { mediaService, type Episode } from '../../api/Media/mediaService';
import StarRating from '../Client/Primitives/StarRating';
import ArtImage from '../Client/Primitives/ArtImage';
import { StorageKeys } from '../../utils/storageKeys';

interface Props {
    episodes: Episode[];
    serverId?: string;
}

export default function MediaEpisodesList({ episodes, serverId }: Props) {
    const navigate = useNavigate();
    const [localRatings, setLocalRatings] = useState<Record<string, number | null>>({});

    const isAdmin = typeof window !== 'undefined' && window.localStorage.getItem(StorageKeys.isServerAdmin) === 'true';

    const handleRate = useCallback(async (episodeId: string, next: number | null) => {
        setLocalRatings(prev => ({ ...prev, [episodeId]: next }));
        try {
            await mediaService.setRating(episodeId, next, serverId);
        } catch {
            setLocalRatings(prev => {
                const copy = { ...prev };
                delete copy[episodeId];
                return copy;
            });
        }
    }, [serverId]);

    if (!episodes || episodes.length === 0) return null;

    return (
        <div className="mt-16">
            <h2 className="text-2xl font-bold mb-6 text-[var(--vora-text-primary)] border-b border-[var(--vora-border-subtle)] pb-2">Episodes</h2>
            <div className="space-y-4">
                {episodes.map(ep => {
                    const ratingValue = ep.id in localRatings ? localRatings[ep.id] : (ep.myRating ?? null);

                    return (
                        <div key={ep.id} className="flex flex-col sm:flex-row gap-6 p-4 bg-[var(--vora-bg-sunken)]/50 hover:bg-[var(--vora-bg-sunken)] rounded-lg border border-transparent hover:border-[var(--vora-border-subtle)] transition-colors group">
                            <div
                                onClick={() => navigate(serverId ? `/server/${serverId}/media/${ep.id}` : `/media/${ep.id}`)}
                                className="w-full sm:w-48 shrink-0 relative aspect-video bg-[var(--vora-bg-canvas)] rounded-md overflow-hidden shadow-md cursor-pointer"
                            >
                                <ArtImage src={ep.posterUrl} alt={ep.title} variant="still" imgClassName="w-full h-full object-contain bg-black" />
                                <div className="absolute inset-0 bg-black/40 opacity-0 group-hover:opacity-100 transition-opacity"></div>

                                {ep.isPlayed && (
                                    <div className="absolute top-2 right-2 bg-black/60 backdrop-blur-sm rounded-full p-1 shadow-lg border border-white/10 z-10">
                                        <svg className="w-4 h-4 text-[var(--vora-text-primary)]" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={3} d="M5 13l4 4L19 7" /></svg>
                                    </div>
                                )}

                                {ep.resumePositionSeconds && ep.resumePositionSeconds > 0 && !ep.isPlayed && ep.durationMinutes && (
                                    <div className="absolute bottom-0 left-0 right-0 h-1 bg-[var(--vora-bg-sunken)] z-10">
                                        <div className="h-full" style={{ width: `${(ep.resumePositionSeconds / (ep.durationMinutes * 60)) * 100}%`, background: 'var(--vora-accent-500)' }}></div>
                                    </div>
                                )}
                            </div>
                            <div className="flex-1 flex flex-col justify-center">
                                <h4
                                    onClick={() => navigate(serverId ? `/server/${serverId}/media/${ep.id}` : `/media/${ep.id}`)}
                                    className="text-lg font-bold text-[var(--vora-text-primary)] cursor-pointer transition-colors"
                                    style={{ color: 'var(--vora-text-primary)' }}
                                >
                                    {ep.endEpisodeNumber && ep.endEpisodeNumber > ep.episodeNumber ? `${ep.episodeNumber}-${ep.endEpisodeNumber}` : ep.episodeNumber}. {ep.title}
                                </h4>
                                <div className="flex flex-wrap items-center gap-x-3 gap-y-1 text-xs font-semibold mt-1 mb-2" style={{ color: 'var(--vora-text-muted)' }}>
                                    {ep.releaseDate && <span>{new Date(ep.releaseDate).toLocaleDateString()}</span>}
                                    {ep.durationMinutes && <span>{Math.round(ep.durationMinutes)} min</span>}
                                    <div className="flex items-center gap-1.5" onClick={(e) => e.stopPropagation()}>
                                        <StarRating
                                            value={ratingValue}
                                            onChange={(next) => handleRate(ep.id, next)}
                                            size={14}
                                            ariaLabel={`Rate ${ep.title}`}
                                            title={ratingValue != null ? 'Click the same star to clear' : 'Rate this episode'}
                                        />
                                    </div>
                                    {ep.serverAdminRating != null && !isAdmin && (
                                        <span style={{ color: 'var(--vora-accent-text)' }} title={`Server admin: ${(ep.serverAdminRating / 2).toFixed(1)} of 5 stars`}>
                                            ★ Admin {(ep.serverAdminRating / 2).toFixed(1)}
                                        </span>
                                    )}
                                </div>
                                <p
                                    onClick={() => navigate(serverId ? `/server/${serverId}/media/${ep.id}` : `/media/${ep.id}`)}
                                    className="text-sm line-clamp-3 leading-relaxed cursor-pointer"
                                    style={{ color: 'var(--vora-text-secondary)' }}
                                >
                                    {ep.overview || "No overview available for this episode."}
                                </p>
                            </div>
                        </div>
                    );
                })}
            </div>
        </div>
    );
}