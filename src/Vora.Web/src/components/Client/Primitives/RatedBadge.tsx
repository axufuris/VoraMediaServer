import { VoraMark } from './RatingBadge';

interface RatedBadgeProps {
    value: number;
    title?: string;
    color?: string;
    background?: string;
}

export default function RatedBadge({ value, title, color, background }: RatedBadgeProps) {
    const score = Math.round(value);
    return (
        <span
            className="inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[10px] font-bold backdrop-blur-md"
            style={{
                background: background ?? 'color-mix(in srgb, var(--vora-bg-canvas) 72%, transparent)',
                color: color ?? 'var(--vora-accent-text)',
                border: '1px solid var(--vora-border-strong)',
                lineHeight: 1,
            }}
            title={title ?? `Rated ${score} of 10`}
        >
            <VoraMark size={10} />
            <span className="tabular-nums">{score}</span>
        </span>
    );
}
