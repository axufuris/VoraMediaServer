import { useNavigate } from 'react-router-dom';
import MediaCard from './MediaCard';
import MediaRow from '../Common/MediaRow';
import type { RecommendationListVM } from '../../api/Discovery/recommendationService';

interface RecommendationRowProps {
    list: RecommendationListVM;
    serverId?: string;
}

export default function RecommendationRow({ list, serverId }: RecommendationRowProps) {
    const navigate = useNavigate();

    if (!list.items || list.items.length === 0) return null;

    return (
        <MediaRow title={list.title} subtitle={list.description} variant="home">
            {list.items.map(item => (
                <div key={item.id} className="flex-none w-40 sm:w-48 snap-start">
                    <MediaCard
                        id={item.id}
                        title={item.title}
                        subtitle={item.releaseDate ? new Date(item.releaseDate).getFullYear().toString() : item.type}
                        imageUrl={item.posterUrl}
                        type={item.type}
                        aspectRatio={item.type === 'Episode' ? 'video' : 'poster'}
                        isPlayed={item.isPlayed}
                        unplayedCount={item.unplayedItemCount}
                        onClick={() => navigate(serverId ? `/server/${serverId}/media/${item.id}` : `/media/${item.id}`)}
                    />
                </div>
            ))}
        </MediaRow>
    );
}
