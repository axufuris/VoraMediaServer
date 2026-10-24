import type { SkipPrompt } from './usePlaybackController';

export const SKIP_PROMPT_SHOWN_MS = 10_000;
export const UP_NEXT_MINIMUM_MS = 10_000;

export function skipPromptKey(prompt: SkipPrompt | null, commercialId: string | null): string | null {
    if (commercialId) return `commercial:${commercialId}`;
    if (!prompt) return null;
    return prompt.kind === 'credits-to-end' ? prompt.kind : `${prompt.kind}:${prompt.target.toFixed(2)}`;
}

export function skipPromptVisible(msSinceShown: number, controlsVisible: boolean): boolean {
    return controlsVisible || msSinceShown < SKIP_PROMPT_SHOWN_MS;
}

export function upNextCountdownSeconds(secondsLeft: number, msSinceShown: number): number {
    return Math.max(secondsLeft, (UP_NEXT_MINIMUM_MS - msSinceShown) / 1000, 0);
}

export function upNextStartsNow(finished: boolean, msSinceShown: number): boolean {
    return finished && msSinceShown >= UP_NEXT_MINIMUM_MS;
}
