import type { ReactNode } from 'react';

const URL_PATTERN = /https?:\/\/[^\s)]+[^\s).,;:!?]/g;

export function linkify(text: string, linkClassName = 'underline hover:text-[var(--vora-accent-500)]'): ReactNode[] {
    const parts: ReactNode[] = [];
    let lastIndex = 0;
    let match: RegExpExecArray | null;
    URL_PATTERN.lastIndex = 0;
    while ((match = URL_PATTERN.exec(text)) !== null) {
        if (match.index > lastIndex) parts.push(text.substring(lastIndex, match.index));
        parts.push(
            <a key={`link-${match.index}`} href={match[0]} target="_blank" rel="noreferrer" className={linkClassName}>
                {match[0]}
            </a>
        );
        lastIndex = match.index + match[0].length;
    }
    if (lastIndex < text.length) parts.push(text.substring(lastIndex));
    return parts;
}
