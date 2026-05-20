import { useNavigate } from 'react-router-dom';
import type { Episode } from '../../api/Media/mediaService';
interface Props {
    episodes: Episode[];
    serverId?: string;
}

export default function MediaEpisodesList({ episodes, serverId }: Props) {
    const navigate = useNavigate();

    if (!episodes || episodes.length === 0) return null;

    return (
        <div className="mt-16">
            <h2 className="text-2xl font-bold mb-6 text-gray-100 border-b border-gray-800 pb-2">Episodes</h2>
            <div className="space-y-4">
                {episodes.map(ep => (
                    <div key={ep.id} onClick={() => navigate(serverId ? `/server/${serverId}/media/${ep.id}` : `/media/${ep.id}`)} className="flex flex-col sm:flex-row gap-6 p-4 bg-gray-800/50 hover:bg-gray-800 rounded-lg border border-transparent hover:border-gray-700 transition-colors group cursor-pointer">
                        <div className="w-full sm:w-48 shrink-0 relative aspect-video bg-gray-950 rounded-md overflow-hidden shadow-md">
                            {ep.posterUrl ? (
                                <img src={ep.posterUrl} alt={ep.title} className="w-full h-full object-contain bg-black" />
                            ) : (
                                <div className="w-full h-full flex items-center justify-center text-xs text-gray-600">No Image</div>
                            )}
                            <div className="absolute inset-0 bg-black/40 opacity-0 group-hover:opacity-100 transition-opacity"></div>

                            {ep.isPlayed && (
                                <div className="absolute top-2 right-2 bg-black/60 backdrop-blur-sm rounded-full p-1 shadow-lg border border-white/10 z-10">
                                    <svg className="w-4 h-4 text-white" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={3} d="M5 13l4 4L19 7" /></svg>
                                </div>
                            )}

                            {ep.resumePositionSeconds && ep.resumePositionSeconds > 0 && !ep.isPlayed && ep.durationMinutes && (
                                <div className="absolute bottom-0 left-0 right-0 h-1 bg-gray-800 z-10">
                                    <div className="h-full bg-orange-500" style={{ width: `${(ep.resumePositionSeconds / (ep.durationMinutes * 60)) * 100}%` }}></div>
                                </div>
                            )}
                        </div>
                        <div className="flex-1 flex flex-col justify-center">
                            <h4 className="text-lg font-bold text-gray-100 group-hover:text-orange-400 transition-colors">
                                {ep.episodeNumber}. {ep.title}
                            </h4>
                            <div className="flex items-center gap-3 text-xs font-semibold text-gray-500 mt-1 mb-2">
                                {ep.releaseDate && <span>{new Date(ep.releaseDate).toLocaleDateString()}</span>}
                                {ep.durationMinutes && <span>{Math.round(ep.durationMinutes)} min</span>}
                            </div>
                            <p className="text-sm text-gray-400 line-clamp-3 leading-relaxed">
                                {ep.overview || "No overview available for this episode."}
                            </p>
                        </div>
                    </div>
                ))}
            </div>
        </div>
    );
}