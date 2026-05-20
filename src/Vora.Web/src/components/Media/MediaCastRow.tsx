import { useNavigate } from 'react-router-dom';
import type { CastMember } from '../../api/Media/mediaService';
import MediaRow from '../Common/MediaRow';

interface Props {
    cast: CastMember[];
    serverId?: string;
}

export default function MediaCastRow({ cast, serverId }: Props) {
    const navigate = useNavigate();

    if (!cast || cast.length === 0) return null;

    return (
        <MediaRow title="Cast & Crew" variant="detail" gap="5">
            {cast.map(actor => (
                <div key={actor.actorId} onClick={() => navigate(serverId ? `/server/${serverId}/actor/${actor.actorId}` : `/actor/${actor.actorId}`)} className="w-32 shrink-0 flex flex-col items-center text-center group cursor-pointer">
                    <div className="w-32 h-40 rounded-lg overflow-hidden bg-gray-800 mb-3 border border-gray-700 shadow-lg relative group-hover:border-orange-500 transition-colors">
                        {actor.profileImageUrl ? (
                            <img src={actor.profileImageUrl} alt={actor.name} className="w-full h-full object-cover" />
                        ) : (
                            <div className="w-full h-full flex items-center justify-center bg-gray-800">
                                <svg className="w-16 h-16 text-gray-600" fill="currentColor" viewBox="0 0 24 24"><path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm0 3c1.66 0 3 1.34 3 3s-1.34 3-3 3-3-1.34-3-3 1.34-3 3-3zm0 14.2c-2.5 0-4.71-1.28-6-3.22.03-1.99 4-3.08 6-3.08 1.99 0 5.97 1.09 6 3.08-1.29 1.94-3.5 3.22-6 3.22z" /></svg>
                            </div>
                        )}
                        <div className="absolute inset-0 bg-black/40 opacity-0 group-hover:opacity-100 transition-opacity flex items-center justify-center"></div>
                    </div>
                    <h3 className="font-bold text-gray-200 text-sm leading-tight max-w-full truncate group-hover:text-white transition-colors">
                        {actor.name}
                    </h3>
                    <p className="text-xs text-orange-400 font-bold uppercase tracking-wider mt-1 line-clamp-1">
                        {actor.role}
                    </p>
                    <p className="text-xs text-gray-500 font-medium leading-tight mt-1 line-clamp-2 max-w-full">
                        {actor.characterName || '---'}
                    </p>
                </div>
            ))}
        </MediaRow>
    );
}
