import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import type { MediaItem } from '../../api/Media/mediaService';
import type { LibraryItem } from '../../api/Media/libraryService';
import MediaRow, { MediaRowItem } from '../Client/Primitives/MediaRow';
import MediaCard from '../Client/Primitives/MediaCard';
import type { RecommendationListVM } from '../../api/Discovery/recommendationService';
import { mediaMenuTarget } from './ItemMenu/mediaItemMenu';

interface RecommendationRowProps {
    list: RecommendationListVM;
    serverId?: string;
}

type WatchedState = Pick<LibraryItem, 'isPlayed' | 'unplayedItemCount'>;

export default function RecommendationRow({ list, serverId }: RecommendationRowProps) {
    const navigate = useNavigate();
    // Asking the providers for the row again can cost a model call, so a title
    // marked watched from its menu is patched here instead.
    const [changed, setChanged] = useState<Record<string, WatchedState>>({});

    const noteChange = (media?: MediaItem) => {
        if (!media) return;
        setChanged(current => ({
            ...current,
            [media.id]: { isPlayed: media.isPlayed, unplayedItemCount: media.unplayedItemCount },
        }));
    };

    if (!list.items || list.items.length === 0) return null;

    return (
        <MediaRow title={list.title} subtitle={list.description}>
            {list.items.map(item => {
                const watched = changed[item.id] ?? item;
                return (
                    <MediaRowItem key={item.id}>
                        <MediaCard
                            item={item}
                            imageUrl={item.posterUrl}
                            isPlayed={watched.isPlayed}
                            unplayedCount={watched.unplayedItemCount}
                            onClick={() => navigate(serverId ? `/server/${serverId}/media/${item.id}` : `/media/${item.id}`)}
                            menu={mediaMenuTarget(item, serverId, { onChanged: noteChange })}
                        />
                    </MediaRowItem>
                );
            })}
        </MediaRow>
    );
}
