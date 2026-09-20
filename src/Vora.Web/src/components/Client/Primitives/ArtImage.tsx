import { useState } from 'react';
import MediaPlaceholder, { type PlaceholderVariant } from './MediaPlaceholder';

// Drop-in <img> replacement that falls back to the branded MediaPlaceholder when
// the source is missing OR fails to load (404 etc.) — so nothing ever shows the
// browser's broken-image icon. Use for one-off posters/stills that don't go
// through MediaCard.
export default function ArtImage({ src, alt, variant = 'poster', imgClassName }: {
    src?: string | null;
    alt: string;
    variant?: PlaceholderVariant;
    imgClassName?: string;
}) {
    const [failedSrc, setFailedSrc] = useState<string | null>(null);
    const failed = !!src && failedSrc === src;

    if (!src || failed) return <MediaPlaceholder title={alt} variant={variant} />;

    return (
        <img
            src={src}
            alt={alt}
            loading="lazy"
            decoding="async"
            onError={() => setFailedSrc(src)}
            className={imgClassName ?? 'h-full w-full object-cover'}
        />
    );
}
