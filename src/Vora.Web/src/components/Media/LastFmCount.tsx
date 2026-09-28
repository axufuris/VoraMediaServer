import LastFmMark from './LastFmMark';
import { formatCompactCount } from '../../utils/compactCount';

interface LastFmCountProps {
    value?: number | null;
    // What the number counts, for the tooltip and screen readers.
    unit: 'listeners' | 'plays';
    className?: string;
}

// A Last.fm figure: the Last.fm mark and a compact number, with the full number
// on hover. Nothing at all when Last.fm has no figure for the item.
export default function LastFmCount({ value, unit, className }: LastFmCountProps) {
    if (value == null) return null;
    return (
        <span
            className={`inline-flex items-center gap-1 tabular-nums ${className ?? ''}`}
            title={`${value.toLocaleString()} ${unit} on Last.fm`}
        >
            <LastFmMark className="h-3 w-3" />
            <span>{formatCompactCount(value)}</span>
            <span className="sr-only">{unit}</span>
        </span>
    );
}
