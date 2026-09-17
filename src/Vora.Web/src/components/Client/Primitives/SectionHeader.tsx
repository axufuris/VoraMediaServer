import { type ReactNode } from 'react';

interface SectionHeaderProps {
    title?: ReactNode;
    subtitle?: ReactNode;
    actions?: ReactNode;
    underline?: boolean;
}

// The heading above a MediaRow or a MediaGrid. Shared so a "Cast & Crew" rail
// and a "Movies" search grid carry the same title treatment.
export default function SectionHeader({ title, subtitle, actions, underline }: SectionHeaderProps) {
    if (title === undefined && subtitle === undefined && actions === undefined) return null;

    return (
        <div
            className="mb-4 flex items-end justify-between gap-3"
            style={underline ? { borderBottom: '1px solid var(--vora-border-subtle)', paddingBottom: '0.5rem' } : undefined}
        >
            <div className="min-w-0">
                {title !== undefined && (
                    <h2
                        className="m-0 truncate font-semibold"
                        style={{ color: 'var(--vora-text-primary)', fontSize: 'var(--vora-row-title-size)', letterSpacing: '-0.01em' }}
                    >
                        {title}
                    </h2>
                )}
                {subtitle !== undefined && (
                    <p className="m-0 mt-1 text-sm" style={{ color: 'var(--vora-text-muted)' }}>{subtitle}</p>
                )}
            </div>
            {actions && <div className="flex shrink-0 items-center gap-2">{actions}</div>}
        </div>
    );
}
