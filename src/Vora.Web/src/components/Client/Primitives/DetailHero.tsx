import { type ReactNode } from 'react';
import CinematicBackdrop from './CinematicBackdrop';
import ArtImage from './ArtImage';

export type DetailHeroPosterShape = 'poster' | 'still';

interface DetailHeroProps {
    backdropSrc?: string | null;
    transitionKey?: string | number;
    posterSrc?: string | null;
    posterShape?: DetailHeroPosterShape;
    onBack?: () => void;
    backLabel?: string;
    eyebrow?: ReactNode;
    title: string;
    titleSuffix?: ReactNode;
    subtitle?: ReactNode;
    chips?: ReactNode;
    ratings?: ReactNode;
    credits?: ReactNode;
    actions?: ReactNode;
    notice?: ReactNode;
    overview?: string | null;
}

// The chip used for every hero fact — runtime, content rating, resolution,
// codec, provider state. `tone="accent"` marks the one fact worth calling out
// (the resolution on a local item, "In your library" on a discovery item).
export function HeroChip({ children, tone = 'neutral' }: { children: ReactNode; tone?: 'neutral' | 'accent' }) {
    const accent = tone === 'accent';
    return (
        <span
            className="rounded-md px-2.5 py-1 text-xs font-medium backdrop-blur-md"
            style={accent
                ? { background: 'var(--vora-accent-500)', border: '1px solid var(--vora-accent-500)', color: 'var(--vora-accent-contrast)', fontWeight: 600 }
                : { background: 'rgba(8, 8, 11, 0.6)', border: '1px solid rgba(255, 255, 255, 0.16)', color: '#fafafa' }}
        >
            {children}
        </span>
    );
}

// A square icon action in the hero's button row. Fixed light-on-dark chrome,
// not theme tokens — these sit over artwork, which isn't themed. `active` is the
// on-state (Mark as watched once watched).
export function HeroIconButton({ label, active, onClick, children }: {
    label: string;
    active?: boolean;
    onClick: () => void;
    children: ReactNode;
}) {
    return (
        <button
            type="button"
            onClick={onClick}
            title={label}
            aria-label={label}
            aria-pressed={active}
            className={`inline-flex h-12 w-12 cursor-pointer items-center justify-center rounded-md backdrop-blur-md transition-colors ${active ? '' : 'hover:bg-[rgba(20,20,28,0.85)]'}`}
            style={{
                background: active ? 'var(--vora-accent-500)' : 'rgba(20, 20, 28, 0.72)',
                color: active ? 'var(--vora-accent-contrast)' : '#fafafa',
                border: `1px solid ${active ? 'var(--vora-accent-500)' : 'rgba(255, 255, 255, 0.18)'}`,
                padding: 0,
            }}
        >
            {children}
        </button>
    );
}

// The labelled facts under the hero's rating row — director, genres, studio.
// Shared so a title reads the same whether it came from the library or from a
// discovery provider. Rows with nothing to show are dropped entirely.
//
// Names are capped: TMDB credits the whole Directing department on some films
// (assistant and second-unit directors included), which turns a one-line credit
// into a paragraph. Library items were scanned that way too, so the cap has to
// live here rather than only in the provider.
const MAX_CREDIT_NAMES = 3;

function creditLine(names: string[]): string {
    if (names.length <= MAX_CREDIT_NAMES) return names.join(', ');
    return `${names.slice(0, MAX_CREDIT_NAMES).join(', ')} +${names.length - MAX_CREDIT_NAMES} more`;
}

export function HeroCredits({ directors, genres, studios }: {
    directors?: string[];
    genres?: string[];
    studios?: string[];
}) {
    const rows: { label: string; value: string; title?: string }[] = [];
    if (directors && directors.length > 0) {
        rows.push({ label: directors.length > 1 ? 'Directors' : 'Director', value: creditLine(directors), title: directors.join(', ') });
    }
    if (genres && genres.length > 0) rows.push({ label: 'Genres', value: genres.join(', ') });
    if (studios && studios.length > 0) {
        rows.push({ label: studios.length > 1 ? 'Studios' : 'Studio', value: creditLine(studios), title: studios.join(', ') });
    }
    if (rows.length === 0) return null;

    return (
        <dl className="mt-5 grid max-w-3xl gap-x-4 gap-y-1.5" style={{ gridTemplateColumns: 'auto 1fr' }}>
            {rows.map(row => (
                <div key={row.label} className="contents">
                    <dt
                        className="font-semibold uppercase tracking-wider"
                        style={{ color: 'var(--vora-text-muted)', fontSize: 'var(--vora-card-caption-size)', lineHeight: 1.6 }}
                    >
                        {row.label}
                    </dt>
                    <dd className="m-0 text-sm" style={{ color: 'var(--vora-text-secondary)', lineHeight: 1.6 }} title={row.title}>{row.value}</dd>
                </div>
            ))}
        </dl>
    );
}

// The top block of every detail page — the local media details page and the
// discovery details page both render this, so a title looks the same whether
// it's in the library or not. Pages differ only in what they pass to `actions`:
// local items get Play/Quality/Watched, discovery items get Add to Watchlist.
//
// The backdrop is laid beside the content rather than behind it: it occupies
// the right of the header and dissolves at its left and bottom edges (see
// CinematicBackdrop's 'edge' mask) so it melts into the page instead of ending
// on a hard line.
export default function DetailHero({
    backdropSrc, transitionKey, posterSrc, posterShape = 'poster',
    onBack, backLabel = 'Back', eyebrow, title, titleSuffix, subtitle,
    chips, ratings, credits, actions, notice, overview,
}: DetailHeroProps) {
    const isStill = posterShape === 'still';

    return (
        <header className="relative" style={{ minHeight: '30rem' }}>
            <div className="pointer-events-none absolute inset-y-0 right-0 z-0 w-full md:w-[70%] lg:w-[64%]">
                <CinematicBackdrop
                    src={backdropSrc}
                    intensity="detail"
                    mask="edge"
                    parallax
                    fill
                    transitionKey={transitionKey}
                />
            </div>

            <div className="relative z-10 px-12 pb-10 pt-8">
                {onBack && (
                    <button
                        type="button"
                        onClick={onBack}
                        className="inline-flex cursor-pointer items-center gap-2 rounded-full px-3 py-1.5 text-sm font-medium backdrop-blur-md transition-colors hover:bg-[rgba(20,20,28,0.85)]"
                        style={{ background: 'rgba(20, 20, 28, 0.65)', border: '1px solid rgba(255, 255, 255, 0.14)', color: '#fafafa' }}
                    >
                        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><polyline points="15 18 9 12 15 6" /></svg>
                        {backLabel}
                    </button>
                )}

                <div className={`mt-10 grid gap-10 ${isStill ? 'md:grid-cols-[25rem_1fr]' : 'md:grid-cols-[16.25rem_1fr]'}`}>
                    <div className="shrink-0">
                        <div
                            className={`relative overflow-hidden ${isStill ? 'aspect-video' : 'aspect-[2/3]'}`}
                            style={{
                                borderRadius: 'var(--vora-radius-lg)',
                                boxShadow: 'var(--vora-shadow-lg)',
                                border: '1px solid var(--vora-border-subtle)',
                                background: 'var(--vora-bg-surface)',
                                maxWidth: isStill ? '25rem' : '16.25rem',
                            }}
                        >
                            <ArtImage
                                src={posterSrc}
                                alt={title}
                                variant={isStill ? 'still' : 'poster'}
                                imgClassName={`h-full w-full ${isStill ? 'object-contain' : 'object-cover'}`}
                            />
                        </div>
                    </div>

                    <div className="min-w-0">
                        {eyebrow && (
                            <div className="text-xs font-semibold uppercase tracking-wider" style={{ color: 'var(--vora-accent-text)' }}>
                                {eyebrow}
                            </div>
                        )}

                        <h1
                            className="m-0 mt-2 font-semibold"
                            style={{ color: 'var(--vora-text-primary)', fontSize: 'clamp(2rem, 4vw, 2.75rem)', lineHeight: 1.05, letterSpacing: '-0.02em' }}
                        >
                            {title}
                        </h1>

                        {titleSuffix && (
                            <div className="mt-2 text-lg font-semibold" style={{ color: 'var(--vora-accent-text)' }}>
                                {titleSuffix}
                            </div>
                        )}

                        {subtitle && (
                            <div className="mt-1 text-xl" style={{ color: 'var(--vora-text-secondary)' }}>
                                {subtitle}
                            </div>
                        )}

                        {chips && <div className="mt-4 flex flex-wrap items-center gap-2">{chips}</div>}

                        {ratings && <div className="mt-3 flex flex-wrap items-center gap-x-5 gap-y-2">{ratings}</div>}

                        {credits}

                        {actions && <div className="mt-6 flex flex-wrap items-center gap-3">{actions}</div>}

                        {notice}

                        <p className="mt-7 max-w-3xl text-[0.9375rem] leading-relaxed" style={{ color: 'var(--vora-text-secondary)' }}>
                            {overview || 'No overview available.'}
                        </p>
                    </div>
                </div>
            </div>
        </header>
    );
}
