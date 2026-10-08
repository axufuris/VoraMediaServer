import type { AudioQualityVM } from '../../api/Music/musicService';

interface AudioQualityChipProps {
    quality?: AudioQualityVM | null;
    convertedFrom?: AudioQualityVM | null;
}

export default function AudioQualityChip({ quality, convertedFrom }: AudioQualityChipProps) {
    if (!quality?.label) return null;

    const kind = quality.hiRes ? 'High-resolution lossless audio' : quality.lossless ? 'Lossless audio' : 'Compressed audio';

    return (
        <span
            className="inline-flex items-center rounded border px-1.5 py-0.5 text-[11px] font-semibold leading-none"
            style={{
                borderColor: quality.hiRes ? 'var(--vora-accent-500)' : 'var(--vora-border-subtle)',
                color: quality.hiRes ? 'var(--vora-accent-text)' : 'var(--vora-text-secondary)',
            }}
            title={convertedFrom?.label ? `${kind}, converted from ${convertedFrom.label}` : kind}
        >
            {quality.label}
        </span>
    );
}
