import type { UpNextItemVM, UpNextResultVM } from '../../../api/Media/mediaService';
import MediaCard from '../../Client/Primitives/MediaCard';
import MediaRow from '../../Client/Primitives/MediaRow';
import { useUpNextAutoPlay } from '../useUpNextAutoPlay';

interface CurrentMediaSummary {
    title: string;
    subtitle?: string;
}

interface UpNextOverlayProps {
    currentMedia: CurrentMediaSummary;
    upNextData: UpNextResultVM | null;
    onPlayNext: (item: UpNextItemVM) => Promise<void>;
    onClose: () => void;
    // The profile plays the next episode by itself, and the viewer has not
    // cancelled it for this one.
    autoPlay?: boolean;
    // The item has finished; with autoPlay on, the next one starts now.
    finished?: boolean;
    secondsLeft?: number;
    onCancelAutoPlay?: () => void;
}

export default function UpNextOverlay({ currentMedia, upNextData, onPlayNext, onClose, autoPlay = false, finished = false, secondsLeft = 0, onCancelAutoPlay }: UpNextOverlayProps) {
    const nextItem = upNextData?.nextItem;
    const countingDown = autoPlay && !!nextItem;
    useUpNextAutoPlay({ countingDown, finished, nextItem, onPlayNext });

    return (
        <div
            className="absolute inset-0 z-10 flex animate-fade-in overflow-hidden px-20 py-0 backdrop-blur-md max-md:px-4"
            style={{ background: 'linear-gradient(135deg, color-mix(in srgb, var(--vora-bg-canvas) 92%, transparent), color-mix(in srgb, var(--vora-bg-sunken) 95%, transparent))' }}
        >
            <div className="absolute left-20 top-20 w-96 max-md:hidden">
                <h2 className="m-0 mb-4 pb-2 text-2xl font-semibold tracking-wide" style={{ color: 'var(--vora-text-primary)', borderBottom: '1px solid var(--vora-border-subtle)', letterSpacing: '-0.01em' }}>
                    Currently playing
                </h2>
            </div>

            <div className="absolute left-20 top-[23rem] w-96 max-md:left-48 max-md:right-16 max-md:top-6 max-md:w-auto">
                <h3 className="m-0 truncate text-xl font-semibold max-md:text-base" style={{ color: 'var(--vora-text-primary)' }}>{currentMedia.title}</h3>
                {currentMedia.subtitle && <p className="m-0 mt-1 truncate text-sm" style={{ color: 'var(--vora-text-muted)' }}>{currentMedia.subtitle}</p>}
            </div>

            <div className="ml-auto flex h-full w-[60%] flex-col overflow-y-auto pr-4 pt-20 max-md:ml-0 max-md:w-full max-md:pr-0 max-md:pt-32">
                {upNextData?.nextItem && (
                    <div className="mb-12 max-md:mb-8">
                        <div className="mb-4 flex flex-wrap items-end justify-between gap-3 pb-2" style={{ borderBottom: '1px solid var(--vora-border-subtle)' }}>
                            <h2 className="m-0 text-2xl font-semibold tracking-wide max-md:text-xl" style={{ color: 'var(--vora-text-primary)', letterSpacing: '-0.01em' }}>
                                Up next
                            </h2>
                            {countingDown && (
                                <div className="flex items-center gap-3">
                                    <span role="status" className="text-sm font-medium tabular-nums" style={{ color: 'var(--vora-text-secondary)' }}>
                                        {finished ? 'Starting…' : `Playing in ${Math.ceil(secondsLeft)}s`}
                                    </span>
                                    {!finished && onCancelAutoPlay && (
                                        <button type="button" onClick={onCancelAutoPlay} className="vora-button-secondary cursor-pointer">
                                            Cancel
                                        </button>
                                    )}
                                </div>
                            )}
                        </div>
                        <div
                            className="group flex cursor-pointer overflow-hidden rounded-2xl transition-all max-md:flex-col"
                            onClick={() => upNextData.nextItem && onPlayNext(upNextData.nextItem)}
                            style={{
                                background: 'var(--vora-bg-surface)',
                                border: '1px solid var(--vora-border-subtle)',
                                boxShadow: 'var(--vora-shadow-lg)',
                            }}
                            onMouseEnter={(e) => { e.currentTarget.style.borderColor = 'var(--vora-accent-500)'; }}
                            onMouseLeave={(e) => { e.currentTarget.style.borderColor = 'var(--vora-border-subtle)'; }}
                        >
                            <div className={`relative shrink-0 ${upNextData.nextItem.type === 'Episode' ? 'aspect-video w-72 max-md:w-full' : 'aspect-[2/3] w-48 max-md:w-32'}`}>
                                {upNextData.nextItem.posterUrl ? (
                                    <img src={upNextData.nextItem.posterUrl} className="h-full w-full object-cover" alt="" />
                                ) : (
                                    <div className="flex h-full w-full items-center justify-center" style={{ background: 'var(--vora-bg-sunken)', color: 'var(--vora-text-disabled)' }}>No image</div>
                                )}
                                <div className="absolute inset-0 flex items-center justify-center bg-[color-mix(in_srgb,var(--vora-bg-canvas)_40%,transparent)] opacity-0 transition-opacity group-hover:opacity-100">
                                    <div
                                        className="flex h-16 w-16 items-center justify-center rounded-full shadow-[var(--vora-shadow-overlay)]"
                                        style={{ background: 'var(--vora-accent-500)', color: 'var(--vora-accent-contrast)', paddingLeft: 4 }}
                                    >
                                        <svg width="32" height="32" viewBox="0 0 24 24" fill="currentColor"><polygon points="5 3 19 12 5 21 5 3" /></svg>
                                    </div>
                                </div>
                            </div>
                            <div className="flex flex-1 flex-col justify-center p-8 max-md:p-4">
                                {upNextData.nextItem.type === 'Episode' && (
                                    <p className="m-0 mb-2 text-sm font-bold uppercase tracking-widest" style={{ color: 'var(--vora-accent-text)' }}>
                                        {upNextData.nextItem.tvShowTitle} · S{upNextData.nextItem.seasonNumber} E{upNextData.nextItem.episodeNumber}
                                    </p>
                                )}
                                <h3 className="m-0 mb-3 line-clamp-2 text-3xl font-semibold max-md:text-xl" style={{ color: 'var(--vora-text-primary)', letterSpacing: '-0.01em' }}>{upNextData.nextItem.title}</h3>
                                <p className="m-0 line-clamp-3 leading-relaxed" style={{ color: 'var(--vora-text-secondary)' }}>{upNextData.nextItem.overview}</p>
                            </div>
                        </div>
                    </div>
                )}

                {upNextData?.relatedLists.map(list => (
                    <MediaRow key={list.title} title={list.title} variant="section" className="mb-6">
                        {list.items.map(item => (
                            <MediaCard
                                key={item.id}
                                item={item}
                                imageUrl={item.posterUrl}
                                shape={item.type === 'Episode' ? 'still' : 'poster'}
                                onClick={() => { void onPlayNext(item); }}
                            />
                        ))}
                    </MediaRow>
                ))}
            </div>

            <button
                type="button"
                onClick={onClose}
                aria-label="Close up-next overlay"
                className="vora-icon-button absolute right-12 top-8 z-50 inline-flex h-12 w-12 cursor-pointer items-center justify-center rounded-full bg-[color-mix(in_srgb,var(--vora-bg-canvas)_55%,transparent)] backdrop-blur-md transition-colors max-md:right-4 max-md:top-4 max-md:h-10 max-md:w-10"
                style={{ border: '1px solid color-mix(in srgb, var(--vora-text-primary) 16%, transparent)', color: 'var(--vora-text-primary)' }}
            >
                <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><line x1="18" y1="6" x2="6" y2="18" /><line x1="6" y1="6" x2="18" y2="18" /></svg>
            </button>
        </div>
    );
}
