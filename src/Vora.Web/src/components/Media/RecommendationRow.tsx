import { useNavigate } from 'react-router-dom';
import MediaRow, { MediaRowItem } from '../Client/Primitives/MediaRow';
import MediaCard from '../Client/Primitives/MediaCard';
import type { RecommendationListVM } from '../../api/Discovery/recommendationService';

interface RecommendationRowProps {
    list: RecommendationListVM;
    serverId?: string;
}

export default function RecommendationRow({ list, serverId }: RecommendationRowProps) {
    const navigate = useNavigate();

    if (!list.items || list.items.length === 0) return null;

    return (
        <MediaRow title={list.title} subtitle={list.description}>
            {list.items.map(item => (
                <MediaRowItem key={item.id}>
                    <MediaCard
                        item={item}
                        imageUrl={item.posterUrl}
                        isPlayed={item.isPlayed}
                        unplayedCount={item.unplayedItemCount}
                        onClick={() => navigate(serverId ? `/server/${serverId}/media/${item.id}` : `/media/${item.id}`)}
                    />
                </MediaRowItem>
            ))}
        </MediaRow>
    );
}
