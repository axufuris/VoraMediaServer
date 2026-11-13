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

function AiMark() {
    return (
        <svg
            role="img"
            aria-label="Made by AI"
            viewBox="0 0 24 24"
            fill="currentColor"
            className="shrink-0"
            style={{ width: '0.8em', height: '0.8em', color: 'var(--vora-text-muted)' }}
        >
            <title>Made by AI</title>
            <path d="M10 4q1.2 7.8 8 9q-6.8 1.2-8 9q-1.2-7.8-8-9q6.8-1.2 8-9z" />
            <path d="M19 1q.5 3 3 3.5q-2.5.5-3 3.5q-.5-3-3-3.5q2.5-.5 3-3.5z" />
        </svg>
    );
}

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

    const title = !list.isAi ? list.title : (
        <span className="flex min-w-0 items-center gap-2">
            <span className="truncate">{list.title}</span>
            <AiMark />
        </span>
    );

    return (
        <MediaRow title={title} subtitle={list.description}>
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
