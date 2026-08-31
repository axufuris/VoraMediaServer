import { type ReactNode } from 'react';
import CinematicBackdrop from './CinematicBackdrop';
import MediaCard from './MediaCard';
import MediaGrid from './MediaGrid';
import EmptyState from './EmptyState';

// One tile in either of the actor's two credit sections.
export interface ActorCredit {
    key: string;
    title: string;
    posterUrl?: string | null;
    // The caption under the poster: character name, year, or both.
    caption?: string | null;
    onOpen: () => void;
}

interface ActorProfileProps {
    name: string;
    role?: string | null;
    profileImageUrl?: string | null;
    biography?: string | null;
    birthday?: string | null;
    deathday?: string | null;
    placeOfBirth?: string | null;
    onServer: ActorCredit[];
    knownFor: ActorCredit[];
    onBack: () => void;
    notice?: ReactNode;
}

function ageFrom(birthday?: string | null, deathday?: string | null): number | null {
    if (!birthday) return null;
    const birth = new Date(birthday);
    if (Number.isNaN(birth.getTime())) return null;
    const end = deathday ? new Date(deathday) : new Date();
    let age = end.getFullYear() - birth.getFullYear();
    const months = end.getMonth() - birth.getMonth();
    if (months < 0 || (months === 0 && end.getDate() < birth.getDate())) age--;
    return age;
}

function yearOf(value?: string | null): number | null {
    if (!value) return null;
    const parsed = new Date(value);
    return Number.isNaN(parsed.getTime()) ? null : parsed.getFullYear();
}

function CreditSection({ title, subtitle, credits }: { title: string; subtitle: string; credits: ActorCredit[] }) {
    if (credits.length === 0) return null;
    return (
        <div className="mt-16 px-12">
            <MediaGrid title={title} subtitle={subtitle}>
                {credits.map(credit => (
                    <MediaCard
                        key={credit.key}
                        title={credit.title}
                        captionLines={credit.caption ? [credit.caption] : []}
                        imageUrl={credit.posterUrl}
                        onClick={credit.onOpen}
                        fill
                    />
                ))}
            </MediaGrid>
        </div>
    );
}

// The actor page, shared by the library actor route and the discovery actor
// route so a person looks the same however you arrived.
//
// Credits are split by whether the title is on this server, not by whether a
// cast link happens to exist locally. A show can be in the library while the
// actor has no link to it — a recurring player often isn't in the show-level
// cast the scan stored — and splitting on the link put those titles under
// "Known for" with an "In library" badge, which is where they least belong.
export default function ActorProfile({
    name, role, profileImageUrl, biography, birthday, deathday, placeOfBirth,
    onServer, knownFor, onBack, notice,
}: ActorProfileProps) {
    const age = ageFrom(birthday, deathday);
    const birthYear = yearOf(birthday);
    const deathYear = yearOf(deathday);
    const hasLifeDates = birthYear != null || !!placeOfBirth;

    return (
        <div className="relative min-h-full pb-20">
            <div className="absolute inset-x-0 top-0 z-0">
                <CinematicBackdrop src={profileImageUrl} intensity="detail" parallax transitionKey={name} />
            </div>

            <div className="relative z-10 pt-8">
                <div className="px-12">
                    <button
                        type="button"
                        onClick={onBack}
                        className="inline-flex cursor-pointer items-center gap-2 rounded-full px-3 py-1.5 text-sm font-medium backdrop-blur-md transition-colors hover:bg-[rgba(20,20,28,0.85)]"
                        style={{ background: 'rgba(20, 20, 28, 0.65)', border: '1px solid rgba(255, 255, 255, 0.14)', color: '#fafafa' }}
                    >
                        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><polyline points="15 18 9 12 15 6" /></svg>
                        Back
                    </button>
                </div>

                <div className="mt-12 grid gap-10 px-12 md:grid-cols-[16.25rem_1fr]">
                    <div className="shrink-0">
                        <div
                            className="relative aspect-[2/3] overflow-hidden"
                            style={{
                                borderRadius: 'var(--vora-radius-lg)',
                                boxShadow: 'var(--vora-shadow-lg)',
                                border: '1px solid var(--vora-border-subtle)',
                                background: 'var(--vora-bg-surface)',
                                maxWidth: '16.25rem',
                            }}
                        >
                            {profileImageUrl ? (
                                <img src={profileImageUrl} alt={name} className="h-full w-full object-cover" />
                            ) : (
                                <div className="flex h-full w-full flex-col items-center justify-center" style={{ color: 'var(--vora-text-muted)' }}>
                                    <svg width="56" height="56" viewBox="0 0 24 24" fill="currentColor" className="mb-2">
                                        <path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm0 3c1.66 0 3 1.34 3 3s-1.34 3-3 3-3-1.34-3-3 1.34-3 3-3zm0 14.2c-2.5 0-4.71-1.28-6-3.22.03-1.99 4-3.08 6-3.08 1.99 0 5.97 1.09 6 3.08-1.29 1.94-3.5 3.22-6 3.22z" />
                                    </svg>
                                    <span className="text-sm">No image</span>
                                </div>
                            )}
                        </div>
                    </div>

                    <div className="min-w-0">
                        {role && (
                            <div className="text-xs font-semibold uppercase tracking-wider" style={{ color: 'var(--vora-accent-text)' }}>
                                {role}
                            </div>
                        )}
                        <h1 className="m-0 mt-2 font-semibold" style={{ color: 'var(--vora-text-primary)', fontSize: 'clamp(2rem, 4vw, 3rem)', lineHeight: 1.05, letterSpacing: '-0.02em' }}>
                            {name}
                        </h1>

                        {hasLifeDates && (
                            <div className="mt-3 flex flex-wrap items-center gap-3 text-sm" style={{ color: 'var(--vora-text-secondary)' }}>
                                {birthYear != null && (
                                    <span>
                                        {birthYear}{deathYear != null ? ` – ${deathYear}` : ''}
                                        {age != null && <span style={{ color: 'var(--vora-text-muted)' }}> ({age} years old)</span>}
                                    </span>
                                )}
                                {placeOfBirth && <span style={{ color: 'var(--vora-text-muted)' }}>· Born in {placeOfBirth}</span>}
                            </div>
                        )}

                        <p
                            className="mt-6 line-clamp-[10] max-w-3xl text-base leading-relaxed transition-all hover:line-clamp-none"
                            style={{ color: 'var(--vora-text-secondary)' }}
                        >
                            {biography || 'No biography available.'}
                        </p>

                        {notice}
                    </div>
                </div>

                <CreditSection
                    title="On Server"
                    subtitle={`${onServer.length} title${onServer.length === 1 ? '' : 's'} you can watch now`}
                    credits={onServer}
                />
                <CreditSection
                    title="Known For"
                    subtitle="Other credits, not in your library"
                    credits={knownFor}
                />

                {onServer.length === 0 && knownFor.length === 0 && (
                    <div className="mt-16">
                        <EmptyState
                            title="No credits to show"
                            description="Nothing by this person is in your library, and no wider credit list is available."
                        />
                    </div>
                )}
            </div>
        </div>
    );
}
