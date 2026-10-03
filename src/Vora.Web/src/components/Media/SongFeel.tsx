import { songFeelParts, type SongFeelInput } from '../../utils/songFeel';

export default function SongFeel(props: SongFeelInput) {
    const parts = songFeelParts(props);
    if (parts.length === 0) return null;

    return (
        <p className="text-xs text-[var(--vora-text-muted)]" aria-label="Mood">
            {parts.join(' · ')}
        </p>
    );
}
