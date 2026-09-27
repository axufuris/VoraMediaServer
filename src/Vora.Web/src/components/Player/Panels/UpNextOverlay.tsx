import type { UpNextItemVM, UpNextResultVM } from '../../../api/Media/mediaService';

interface CurrentMediaSummary {
    title: string;
    subtitle?: string;
}

interface UpNextOverlayProps {
    currentMedia: CurrentMediaSummary;
    upNextData: UpNextResultVM | null;
    onPlayNext: (item: UpNextItemVM) => Promise<void>;
    onClose: () => void;
}

export default function UpNextOverlay({ currentMedia, upNextData, onPlayNext, onClose }: UpNextOverlayProps) {
    return (
        <div
            className="absolute inset-0 z-10 flex animate-fade-in overflow-hidden px-20 py-0 backdrop-blur-md"
            style={{ background: 'linear-gradient(135deg, color-mix(in srgb, var(--vora-bg-canvas) 92%, transparent), color-mix(in srgb, var(--vora-bg-sunken) 95%, transparent))' }}
        >
            <div className="absolute left-20 top-20 w-96">
                <h2 className="m-0 mb-4 pb-2 text-2xl font-semibold tracking-wide" style={{ color: 'var(--vora-text-primary)', borderBottom: '1px solid var(--vora-border-subtle)', letterSpacing: '-0.01em' }}>
                    Currently playing
                </h2>
            </div>

            <div className="absolute left-20 top-[23rem] w-96">
                <h3 className="m-0 truncate text-xl font-semibold" style={{ color: 'var(--vora-text-primary)' }}>{currentMedia.title}</h3>
                {currentMedia.subtitle && <p className="m-0 mt-1 truncate text-sm" style={{ color: 'var(--vora-text-muted)' }}>{currentMedia.subtitle}</p>}
            </div>

            <div className="ml-auto flex h-full w-[60%] flex-col overflow-y-auto pr-4 pt-20">
                {upNextData?.nextItem && (
                    <div className="mb-12">
                        <h2 className="m-0 mb-4 pb-2 text-2xl font-semibold tracking-wide" style={{ color: 'var(--vora-text-primary)', borderBottom: '1px solid var(--vora-border-subtle)', letterSpacing: '-0.01em' }}>
                            Up next
                        </h2>
                        <div
                            className="group flex cursor-pointer overflow-hidden rounded-2xl transition-all"
                            onClick={() => upNextData.nextItem && onPlayNext(upNextData.nextItem)}
                            style={{
                                background: 'var(--vora-bg-surface)',
                                border: '1px solid var(--vora-border-subtle)',
                                boxShadow: 'var(--vora-shadow-lg)',
                            }}
                            onMouseEnter={(e) => { e.currentTarget.style.borderColor = 'var(--vora-accent-500)'; }}
                            onMouseLeave={(e) => { e.currentTarget.style.borderColor = 'var(--vora-border-subtle)'; }}
                        >
                            <div className={`relative shrink-0 ${upNextData.nextItem.type === 'Episode' ? 'aspect-video w-72' : 'aspect-[2/3] w-48'}`}>
                                {upNextData.nextItem.posterUrl ? (
                                    <img src={upNextData.nextItem.posterUrl} className="h-full w-full object-cover" alt="" />
                                ) : (
                                    <div className="flex h-full w-full items-center justify-center" style={{ background: 'var(--vora-bg-sunken)', color: 'var(--vora-text-disabled)' }}>No image</div>
                                )}
                                <div className="absolute inset-0 flex items-center justify-center bg-black/40 opacity-0 transition-opacity group-hover:opacity-100">
                                    <div
                                        className="flex h-16 w-16 items-center justify-center rounded-full shadow-xl"
                                        style={{ background: 'var(--vora-accent-500)', color: 'var(--vora-accent-contrast)', paddingLeft: 4 }}
                                    >
                                        <svg width="32" height="32" viewBox="0 0 24 24" fill="currentColor"><polygon points="5 3 19 12 5 21 5 3" /></svg>
                                    </div>
                                </div>
                            </div>
                            <div className="flex flex-1 flex-col justify-center p-8">
                                {upNextData.nextItem.type === 'Episode' && (
                                    <p className="m-0 mb-2 text-sm font-bold uppercase tracking-widest" style={{ color: 'var(--vora-accent-text)' }}>
                                        {upNextData.nextItem.tvShowTitle} · S{upNextData.nextItem.seasonNumber} E{upNextData.nextItem.episodeNumber}
                                    </p>
                                )}
                                <h3 className="m-0 mb-3 line-clamp-2 text-3xl font-semibold" style={{ color: 'var(--vora-text-primary)', letterSpacing: '-0.01em' }}>{upNextData.nextItem.title}</h3>
                                <p className="m-0 line-clamp-3 leading-relaxed" style={{ color: 'var(--vora-text-secondary)' }}>{upNextData.nextItem.overview}</p>
                            </div>
                        </div>
                    </div>
                )}

                {upNextData?.relatedLists.map(list => (
                    <div key={list.title} className="mb-10">
                        <h3 className="m-0 mb-4 text-xl font-semibold" style={{ color: 'var(--vora-text-primary)' }}>{list.title}</h3>
                        <div className="flex gap-4 overflow-x-auto pb-4">
                            {list.items.map(item => (
                                <div
                                    key={item.id}
                                    onClick={() => onPlayNext(item)}
                                    className={`group shrink-0 cursor-pointer ${item.type === 'Episode' ? 'w-56' : 'w-36'}`}
                                >
                                    <div
                                        className={`${item.type === 'Episode' ? 'aspect-video' : 'aspect-[2/3]'} relative mb-2 overflow-hidden transition-all`}
                                        style={{
                                            background: 'var(--vora-bg-surface)',
                                            border: '1px solid var(--vora-border-subtle)',
                                            borderRadius: 'var(--vora-radius-md)',
                                            boxShadow: 'var(--vora-shadow-md)',
                                        }}
                                    >
                                        {item.posterUrl ? <img src={item.posterUrl} className="h-full w-full object-cover" /> : null}
                                        <div className="absolute inset-0 flex items-center justify-center bg-black/50 opacity-0 transition-opacity group-hover:opacity-100">
                                            <svg width="32" height="32" viewBox="0 0 24 24" fill="currentColor" style={{ color: 'var(--vora-accent-500)' }}><polygon points="5 3 19 12 5 21 5 3" /></svg>
                                        </div>
                                    </div>
                                    <h4 className="m-0 truncate text-sm font-medium" style={{ color: 'var(--vora-text-primary)' }}>{item.title}</h4>
                                </div>
                            ))}
                        </div>
                    </div>
                ))}
            </div>

            <button
                type="button"
                onClick={onClose}
                aria-label="Close up-next overlay"
                className="absolute right-12 top-8 z-50 inline-flex h-12 w-12 cursor-pointer items-center justify-center rounded-full backdrop-blur-md transition-colors hover:bg-white/10"
                style={{ background: 'rgba(20, 20, 28, 0.55)', border: '1px solid rgba(255, 255, 255, 0.16)', color: '#fafafa' }}
            >
                <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><line x1="18" y1="6" x2="6" y2="18" /><line x1="6" y1="6" x2="18" y2="18" /></svg>
            </button>
        </div>
    );
}
