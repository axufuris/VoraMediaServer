import { useEffect, useState } from 'react';

export const SHOWN_TICK_MS = 250;

interface Shown {
    key: string;
    elapsed: number;
}

export function useMsSinceShown(key: string | null, untilMs: number): number {
    const [shown, setShown] = useState<Shown | null>(null);

    useEffect(() => {
        if (key === null) return;
        const at = Date.now();
        const timer = window.setInterval(() => {
            const elapsed = Date.now() - at;
            setShown({ key, elapsed });
            if (elapsed >= untilMs) window.clearInterval(timer);
        }, SHOWN_TICK_MS);
        return () => {
            window.clearInterval(timer);
            setShown(null);
        };
    }, [key, untilMs]);

    return key !== null && shown?.key === key ? shown.elapsed : 0;
}
