import LastFmCount from './LastFmCount';

interface TrackListenersProps {
    listeners?: number | null;
}

export default function TrackListeners({ listeners }: TrackListenersProps) {
    return (
        <div className="hidden sm:flex w-20 items-center justify-end text-xs text-[var(--vora-text-disabled)] shrink-0">
            <LastFmCount value={listeners} unit="listeners" />
        </div>
    );
}
