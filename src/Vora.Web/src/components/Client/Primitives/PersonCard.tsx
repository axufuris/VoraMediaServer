import { useState } from 'react';
import MediaPlaceholder from './MediaPlaceholder';

export interface PersonCardProps {
    name: string;
    role?: string | null;
    characterName?: string | null;
    imageUrl?: string | null;
    onClick?: () => void;
}

// The single cast/crew tile — used by the media details page, the discovery
// details page and anywhere else a person appears in a row. Portrait artwork,
// name, credited role, then the character.
export default function PersonCard({ name, role, characterName, imageUrl, onClick }: PersonCardProps) {
    const [failedUrl, setFailedUrl] = useState<string | null>(null);
    const showImage = !!imageUrl && failedUrl !== imageUrl;

    const handleKeyDown = onClick
        ? (e: React.KeyboardEvent<HTMLDivElement>) => {
            if (e.key === 'Enter' || e.key === ' ') {
                e.preventDefault();
                onClick();
            }
        }
        : undefined;

    return (
        <div
            role={onClick ? 'button' : undefined}
            tabIndex={onClick ? 0 : undefined}
            onClick={onClick}
            onKeyDown={handleKeyDown}
            className={`group flex flex-col items-center text-center ${onClick ? 'cursor-pointer' : ''}`}
            style={{ width: 'var(--vora-person-w)' }}
        >
            <div
                className="relative w-full overflow-hidden border border-[var(--vora-border-subtle)] transition-colors group-hover:border-[var(--vora-accent-500)]"
                style={{
                    aspectRatio: '4 / 5',
                    borderRadius: 'var(--vora-radius-md)',
                    boxShadow: 'var(--vora-shadow-md)',
                    background: 'var(--vora-bg-surface)',
                }}
            >
                {showImage ? (
                    <img
                        src={imageUrl!}
                        alt={name}
                        loading="lazy"
                        decoding="async"
                        onError={() => setFailedUrl(imageUrl ?? null)}
                        className="h-full w-full object-cover"
                    />
                ) : (
                    <MediaPlaceholder title={name} variant="actor" />
                )}
            </div>
            <div
                className="mt-2.5 w-full truncate font-semibold transition-colors group-hover:text-[var(--vora-accent-text)]"
                style={{ color: 'var(--vora-text-primary)', fontSize: 'var(--vora-card-title-size)' }}
                title={name}
            >
                {name}
            </div>
            {role && (
                <div
                    className="mt-1 w-full truncate font-bold uppercase tracking-wider"
                    style={{ color: 'var(--vora-accent-text)', fontSize: 'var(--vora-card-caption-size)' }}
                >
                    {role}
                </div>
            )}
            {characterName && (
                <div
                    className="mt-1 line-clamp-2 w-full leading-tight"
                    style={{ color: 'var(--vora-text-muted)', fontSize: 'var(--vora-card-caption-size)' }}
                >
                    {characterName}
                </div>
            )}
        </div>
    );
}
