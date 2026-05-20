import { useMemo } from 'react';

interface LetterRailProps {
    available: ReadonlyArray<string>;
    onJump: (letter: string) => void;
    activeLetter?: string;
    className?: string;
}

const ALL_LETTERS = ['#', 'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J', 'K', 'L', 'M', 'N', 'O', 'P', 'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z'];

export default function LetterRail({ available, onJump, activeLetter, className }: LetterRailProps) {
    const availableSet = useMemo(() => new Set(available.map(l => l.toUpperCase())), [available]);

    return (
        <nav
            className={`flex flex-col items-center gap-0.5 select-none ${className ?? ''}`}
            aria-label="Jump to letter"
        >
            {ALL_LETTERS.map(letter => {
                const enabled = availableSet.has(letter);
                const isActive = activeLetter?.toUpperCase() === letter;
                return (
                    <button
                        key={letter}
                        type="button"
                        disabled={!enabled}
                        onClick={() => enabled && onJump(letter)}
                        className={`h-5 w-5 rounded text-xs font-semibold leading-none transition-colors ${enabled ? 'cursor-pointer' : 'cursor-default opacity-30'}`}
                        style={{
                            color: isActive ? 'var(--vora-accent-contrast)' : enabled ? 'var(--vora-text-secondary)' : 'var(--vora-text-disabled)',
                            background: isActive ? 'var(--vora-accent-500)' : 'transparent',
                        }}
                    >
                        {letter}
                    </button>
                );
            })}
        </nav>
    );
}
