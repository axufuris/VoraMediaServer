import { useEffect, useState } from 'react';
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
    const [failed, setFailed] = useState(false);
    useEffect(() => { setFailed(false); }, [src]);

    if (!src || failed) return <MediaPlaceholder title={alt} variant={variant} />;

    return (
        <img
            src={src}
            alt={alt}
            loading="lazy"
            decoding="async"
            onError={() => setFailed(true)}
            className={imgClassName ?? 'h-full w-full object-cover'}
        />
    );
}
