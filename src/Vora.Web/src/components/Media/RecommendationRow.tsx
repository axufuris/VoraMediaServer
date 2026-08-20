import { useNavigate } from 'react-router-dom';
import MediaCard from './MediaCard';
import MediaRow from '../Common/MediaRow';
import { posterCaption } from '../../utils/posterCaption';
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
            {list.items.map(item => {
                const cap = posterCaption(item);
                return (
                <div key={item.id} className="flex-none w-40 sm:w-48 snap-start">
                    <MediaCard
                        id={item.id}
                        title={cap.title}
                        captionLines={cap.lines}
                        imageUrl={item.posterUrl}
                        type={item.type}
                        aspectRatio={item.type === 'Episode' ? 'video' : 'poster'}
                        isPlayed={item.isPlayed}
                        unplayedCount={item.unplayedItemCount}
                        onClick={() => navigate(serverId ? `/server/${serverId}/media/${item.id}` : `/media/${item.id}`)}
                    />
                </div>
                );
            })}
        </MediaRow>
    );
}
