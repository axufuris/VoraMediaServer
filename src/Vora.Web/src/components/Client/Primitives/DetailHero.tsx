import { useRef, useState, type ReactNode } from 'react';
import CinematicBackdrop from './CinematicBackdrop';
import ActionMenu from './ActionMenu';
import type { MenuEntry, MenuPoint } from './menuEntries';
import ArtImage from './ArtImage';
import { thumbUrl } from '../../../utils/thumbnails';

export type DetailHeroPosterShape = 'poster' | 'still';

interface DetailHeroProps {
    backdropSrc?: string | null;
    transitionKey?: string | number;
    posterSrc?: string | null;
    posterShape?: DetailHeroPosterShape;
    eyebrow?: ReactNode;
    title: string;
    // The title treatment: the title drawn as artwork. Shown in place of the
    // text when the item has one.
    logoUrl?: string | null;
    titleSuffix?: ReactNode;
    subtitle?: ReactNode;
    chips?: ReactNode;
    ratings?: ReactNode;
    credits?: ReactNode;
    actions?: ReactNode;
    notice?: ReactNode;
    overview?: string | null;
    // Clamps the overview to this many lines and holds that much room, so a
    // page that swaps what the hero describes (a season's selected episode)
    // does not move everything below it.
    overviewLines?: number;
    // Above the title: the season pills on a season page.
    topContent?: ReactNode;
    // The hero swaps what it describes (a season page's selected episode), and
    // its file chips and ratings arrive a moment after the rest. Hold their room
    // so the hero, which centres its content, does not jump as they come and go.
    steady?: boolean;
    // Resolution / audio / subtitle chips, in the hero's bottom-right corner.
    techChips?: ReactNode;
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
                : { background: 'color-mix(in srgb, var(--vora-bg-canvas) 60%, transparent)', border: '1px solid var(--vora-border-strong)', color: 'var(--vora-text-primary)' }}
        >
            {children}
        </span>
    );
}

// A hero chip that opens a menu of choices, like Plex's version, audio and
// subtitle pickers: the chip shows what is in use and the menu changes it. With
// nothing else to choose it stays a plain chip.
export function HeroChoiceChip({ label, title, entries, tone = 'neutral' }: {
    label: ReactNode;
    title: string;
    entries: MenuEntry[];
    tone?: 'neutral' | 'accent';
}) {
    const button = useRef<HTMLButtonElement>(null);
    const [openAt, setOpenAt] = useState<MenuPoint | null>(null);

    if (entries.filter(entry => entry.kind === 'choice').length < 2) return <HeroChip tone={tone}>{label}</HeroChip>;

    const open = () => {
        const box = button.current?.getBoundingClientRect();
        if (box) setOpenAt({ x: box.left, y: box.bottom + 6 });
    };

    return (
        <>
            <button
                ref={button}
                type="button"
                aria-haspopup="menu"
                aria-expanded={openAt !== null}
                aria-label={title}
                data-tone={tone}
                data-open={openAt !== null}
                onClick={open}
                className="vora-hero-chip-button inline-flex cursor-pointer items-center gap-1 rounded-md px-2.5 py-1 text-xs font-medium backdrop-blur-md"
            >
                {label}
                <svg aria-hidden="true" width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
                    <path d="m6 9 6 6 6-6" />
                </svg>
            </button>
            {openAt && <ActionMenu at={openAt} title={title} entries={entries} onClose={() => setOpenAt(null)} />}
        </>
    );
}

// A square icon action in the hero's button row. `active` is the
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
            className={`inline-flex h-12 w-12 cursor-pointer items-center justify-center rounded-md backdrop-blur-md transition-colors ${active ? 'bg-[var(--vora-accent-500)]' : 'bg-[color-mix(in_srgb,var(--vora-bg-raised)_72%,transparent)] hover:bg-[linear-gradient(var(--vora-accent-soft),var(--vora-accent-soft))]'}`}
            style={{
                color: active ? 'var(--vora-accent-contrast)' : 'var(--vora-text-primary)',
                border: `1px solid ${active ? 'var(--vora-accent-500)' : 'var(--vora-border-strong)'}`,
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
    eyebrow, title, logoUrl, titleSuffix, subtitle,
    chips, ratings, credits, actions, notice, overview, overviewLines, topContent, steady, techChips,
}: DetailHeroProps) {
    const isStill = posterShape === 'still';
    const [failedLogo, setFailedLogo] = useState<string | null>(null);
    const logo = logoUrl && logoUrl !== failedLogo ? logoUrl : null;

    return (
        <header className="relative grid" style={{ minHeight: '22rem' }}>
            <div
                data-testid="hero-backdrop"
                className="pointer-events-none absolute right-0 top-0 z-0 aspect-video w-full lg:w-[min(64%,calc(60vh*16/9),calc(40rem*16/9))]"
            >
                <CinematicBackdrop
                    src={backdropSrc}
                    intensity="detail"
                    mask="edge"
                    parallax
                    fill
                    transitionKey={transitionKey}
                />
            </div>

            <div
                data-testid="hero-backdrop-spacer"
                aria-hidden="true"
                className="col-start-1 row-start-1 lg:pt-[min(36%,60vh,40rem)]"
            />

            <div className={`relative z-10 col-start-1 row-start-1 flex flex-col justify-center px-12 max-md:px-4 pb-6 pt-6 ${techChips || steady ? 'lg:pb-16' : ''}`}>
                <div className={`grid gap-8 max-md:gap-5 lg:items-center ${isStill ? 'lg:grid-cols-[16rem_1fr] xl:grid-cols-[20rem_1fr]' : 'lg:grid-cols-[12.5rem_1fr]'}`}>
                    <div className={`shrink-0 ${isStill ? 'max-md:w-60' : 'max-md:w-28'}`}>
                        <div
                            className={`relative overflow-hidden ${isStill ? 'aspect-video' : 'aspect-[2/3]'}`}
                            style={{
                                borderRadius: 'var(--vora-radius-lg)',
                                boxShadow: 'var(--vora-shadow-lg)',
                                border: '1px solid var(--vora-border-subtle)',
                                background: 'var(--vora-bg-surface)',
                                maxWidth: isStill ? '20rem' : '12.5rem',
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
                        {topContent && <div className="mb-4">{topContent}</div>}

                        {eyebrow && (
                            <div className="text-xs font-semibold uppercase tracking-wider" style={{ color: 'var(--vora-accent-text)' }}>
                                {eyebrow}
                            </div>
                        )}

                        {logo ? (
                            <h1 className="m-0 mt-2">
                                <img
                                    src={thumbUrl(logo, 780, 'logo')}
                                    alt={title}
                                    onError={() => setFailedLogo(logo)}
                                    className="block w-auto max-w-full object-contain object-left"
                                    style={{ maxHeight: 'clamp(3.5rem, 7vw, 5.5rem)' }}
                                />
                            </h1>
                        ) : (
                            <h1
                                className="m-0 mt-2 font-semibold"
                                style={{ color: 'var(--vora-text-primary)', fontSize: 'clamp(1.75rem, 3vw, 2.25rem)', lineHeight: 1.05, letterSpacing: '-0.02em' }}
                            >
                                {title}
                            </h1>
                        )}

                        {subtitle && (
                            <div className="mt-2 text-lg" style={{ color: 'var(--vora-text-secondary)' }}>
                                {subtitle}
                            </div>
                        )}

                        {titleSuffix && (
                            <div className="mt-1 text-sm font-semibold" style={{ color: 'var(--vora-accent-text)' }}>
                                {titleSuffix}
                            </div>
                        )}

                        {chips && <div className="mt-4 flex flex-wrap items-center gap-2">{chips}</div>}

                        {ratings && <div data-testid="hero-ratings" className={`mt-3 flex flex-wrap items-center gap-x-5 gap-y-2 ${steady ? 'min-h-7' : ''}`}>{ratings}</div>}

                        {credits}

                        {actions && <div className="mt-5 flex flex-wrap items-center gap-3">{actions}</div>}

                        {notice}

                        <p
                            data-testid="hero-overview"
                            className="mt-5 max-w-3xl text-[0.9375rem] leading-relaxed"
                            style={{
                                color: 'var(--vora-text-secondary)',
                                ...(overviewLines ? {
                                    display: '-webkit-box', WebkitLineClamp: overviewLines, WebkitBoxOrient: 'vertical', overflow: 'hidden',
                                    minHeight: `${overviewLines * 1.625}em`,
                                } : undefined),
                            }}
                            title={overviewLines && overview ? overview : undefined}
                        >
                            {overview || 'No overview available.'}
                        </p>

                        {(techChips || steady) && (
                            <div data-testid="hero-tech-chips" className={`mt-4 flex flex-wrap items-center gap-2 lg:absolute lg:bottom-6 lg:right-12 lg:mt-0 lg:justify-end ${steady ? 'min-h-8 lg:min-h-0' : ''}`}>
                                {techChips}
                            </div>
                        )}
                    </div>
                </div>
            </div>
        </header>
    );
}
