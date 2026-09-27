import LastFmMark from './LastFmMark';
import { formatCompactCount } from '../../utils/compactCount';

interface TrackListenersProps {
    listeners?: number | null;
}

export default function TrackListeners({ listeners }: TrackListenersProps) {
    return (
        <div
            className="hidden sm:flex w-20 items-center justify-end gap-1 text-xs text-[var(--vora-text-disabled)] shrink-0 tabular-nums"
            title={listeners != null ? `${listeners.toLocaleString()} listeners on Last.fm` : undefined}
        >
            {listeners != null && (
                <>
                    <LastFmMark className="h-3 w-3" />
                    <span>{formatCompactCount(listeners)}</span>
                    <span className="sr-only">listeners</span>
                </>
            )}
        </div>
    );
}
