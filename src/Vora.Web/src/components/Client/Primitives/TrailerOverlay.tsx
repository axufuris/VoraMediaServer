import { createPortal } from 'react-dom';

export interface TrailerSource {
    name: string;
    // A YouTube/Vimeo key, or a full watch URL to pull the key out of.
    videoKey?: string;
    url?: string;
    site?: string;
}

// Pulls the video id out of either a bare key or a full watch URL, so callers
// can pass whichever shape their API gave them.
function resolveKey(source: TrailerSource): string | null {
    if (source.videoKey) return source.videoKey;
    if (!source.url) return null;
    const fromQuery = source.url.split('v=')[1]?.split('&')[0];
    if (fromQuery) return fromQuery;
    const last = source.url.split('/').pop();
    return last ? last.split('?')[0] : null;
}

function trailerEmbedUrl(source: TrailerSource): string | null {
    const key = resolveKey(source);
    if (!key) return null;
    return source.site === 'Vimeo'
        ? `https://player.vimeo.com/video/${key}?autoplay=1`
        : `https://www.youtube.com/embed/${key}?autoplay=1`;
}

// The full-screen player for an external trailer. Shared by the media details
// page, its Extras row, and the discovery details page — all three used to keep
// their own copy of this markup.
export default function TrailerOverlay({ trailer, onClose }: { trailer: TrailerSource | null; onClose: () => void }) {
    if (!trailer) return null;
    const src = trailerEmbedUrl(trailer);
    if (!src) return null;

    return createPortal(
        <div className="fixed inset-0 z-[99999] flex items-center justify-center bg-black">
            <button
                type="button"
                onClick={onClose}
                title="Close video"
                className="absolute left-1/2 top-6 z-10 flex -translate-x-1/2 cursor-pointer items-center gap-2 rounded-full px-6 py-2.5 text-sm font-bold tracking-wider backdrop-blur-md transition-colors"
                style={{ background: 'rgba(20, 20, 28, 0.8)', border: '1px solid rgba(255,255,255,0.2)', color: '#fafafa' }}
            >
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2.5}>
                    <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
                </svg>
                CLOSE
            </button>
            <iframe
                className="h-full w-full border-none bg-black"
                src={src}
                title={trailer.name}
                allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture; web-share"
                allowFullScreen
            />
        </div>,
        document.body
    );
}
