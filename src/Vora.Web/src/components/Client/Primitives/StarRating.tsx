import { useState, useCallback, useMemo } from 'react';

interface StarRatingProps {
    value: number | null | undefined;
    onChange?: (next: number | null) => void | Promise<void>;
    readOnly?: boolean;
    size?: number;
    showNumeric?: boolean;
    ariaLabel?: string;
    title?: string;
    color?: string;
}

const STAR_COUNT = 5;

export default function StarRating({
    value,
    onChange,
    readOnly = false,
    size = 18,
    showNumeric = false,
    ariaLabel,
    title,
    color,
}: StarRatingProps) {
    const [hover, setHover] = useState<number | null>(null);
    const [busy, setBusy] = useState(false);

    const displayBackendValue = hover !== null ? hover : (value ?? 0);
    const displayStars = displayBackendValue / 2;

    const interactive = !readOnly && !!onChange && !busy;

    const fillColor = color ?? 'var(--vora-accent-500)';
    const emptyColor = 'color-mix(in srgb, var(--vora-text-muted) 50%, transparent)';

    const handleClick = useCallback(async (backendValue: number) => {
        if (!interactive || !onChange) return;
        const target = (value ?? 0) === backendValue ? null : backendValue;
        try {
            setBusy(true);
            await onChange(target);
        } finally {
            setBusy(false);
        }
    }, [interactive, onChange, value]);

    const handleMouseLeave = useCallback(() => {
        if (!interactive) return;
        setHover(null);
    }, [interactive]);

    const stars = useMemo(() => {
        const result: { halfBackendValue: number; fullBackendValue: number; index: number }[] = [];
        for (let i = 0; i < STAR_COUNT; i++) {
            result.push({
                halfBackendValue: i * 2 + 1,
                fullBackendValue: i * 2 + 2,
                index: i,
            });
        }
        return result;
    }, []);

    return (
        <div
            className="inline-flex items-center gap-1"
            onMouseLeave={handleMouseLeave}
            role={interactive ? 'slider' : 'img'}
            aria-label={ariaLabel ?? (value != null ? `Rated ${value} out of 10` : 'No rating')}
            aria-valuemin={interactive ? 0 : undefined}
            aria-valuemax={interactive ? 10 : undefined}
            aria-valuenow={interactive ? (value ?? 0) : undefined}
            title={title}
            style={{ opacity: busy ? 0.6 : 1, transition: 'opacity 150ms ease' }}
        >
            {stars.map(({ halfBackendValue, fullBackendValue, index }) => {
                const halfFilled = displayStars >= index + 0.5;
                const fullFilled = displayStars >= index + 1;

                return (
                    <div
                        key={index}
                        className="relative inline-block"
                        style={{ width: size, height: size }}
                    >
                        <svg
                            width={size}
                            height={size}
                            viewBox="0 0 24 24"
                            style={{ position: 'absolute', top: 0, left: 0, color: emptyColor }}
                            fill="currentColor"
                            aria-hidden="true"
                        >
                            <path d="M12 17.27L18.18 21l-1.64-7.03L22 9.24l-7.19-.61L12 2 9.19 8.63 2 9.24l5.46 4.73L5.82 21z" />
                        </svg>
                        <svg
                            width={size}
                            height={size}
                            viewBox="0 0 24 24"
                            style={{
                                position: 'absolute',
                                top: 0,
                                left: 0,
                                color: fillColor,
                                clipPath: fullFilled
                                    ? 'inset(0 0 0 0)'
                                    : halfFilled
                                        ? 'inset(0 50% 0 0)'
                                        : 'inset(0 100% 0 0)',
                            }}
                            fill="currentColor"
                            aria-hidden="true"
                        >
                            <path d="M12 17.27L18.18 21l-1.64-7.03L22 9.24l-7.19-.61L12 2 9.19 8.63 2 9.24l5.46 4.73L5.82 21z" />
                        </svg>
                        {interactive && (
                            <>
                                <button
                                    type="button"
                                    aria-label={`Rate ${halfBackendValue} of 10`}
                                    onMouseEnter={() => setHover(halfBackendValue)}
                                    onClick={() => handleClick(halfBackendValue)}
                                    style={{
                                        position: 'absolute',
                                        top: 0,
                                        left: 0,
                                        width: size / 2,
                                        height: size,
                                        background: 'transparent',
                                        border: 'none',
                                        padding: 0,
                                        cursor: 'pointer',
                                    }}
                                />
                                <button
                                    type="button"
                                    aria-label={`Rate ${fullBackendValue} of 10`}
                                    onMouseEnter={() => setHover(fullBackendValue)}
                                    onClick={() => handleClick(fullBackendValue)}
                                    style={{
                                        position: 'absolute',
                                        top: 0,
                                        left: size / 2,
                                        width: size / 2,
                                        height: size,
                                        background: 'transparent',
                                        border: 'none',
                                        padding: 0,
                                        cursor: 'pointer',
                                    }}
                                />
                            </>
                        )}
                    </div>
                );
            })}
            {showNumeric && value != null && (
                <span
                    className="ml-1 text-xs tabular-nums"
                    style={{ color: 'var(--vora-text-muted)' }}
                >
                    {value}
                </span>
            )}
        </div>
    );
}
