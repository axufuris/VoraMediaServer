import { useState } from 'react';

interface ArtistBiographyProps {
    text: string;
}

export default function ArtistBiography({ text }: ArtistBiographyProps) {
    const [expanded, setExpanded] = useState(false);
    const long = text.length > 400;

    return (
        <section className="mt-10 max-w-3xl" aria-labelledby="artist-about">
            <h3 id="artist-about" className="mb-3 text-lg font-bold text-[var(--vora-text-primary)]">About</h3>
            <p className={`whitespace-pre-line text-sm leading-relaxed text-[var(--vora-text-secondary)] ${long && !expanded ? 'line-clamp-4' : ''}`}>
                {text}
            </p>
            <div className="mt-2 flex items-center gap-3 text-xs">
                {long && (
                    <button
                        type="button"
                        onClick={() => setExpanded(e => !e)}
                        aria-expanded={expanded}
                        className="cursor-pointer font-semibold text-[var(--vora-accent-text)] hover:underline"
                    >
                        {expanded ? 'Show less' : 'Read more'}
                    </button>
                )}
                <span className="text-[var(--vora-text-muted)]">From Last.fm</span>
            </div>
        </section>
    );
}
