import { useState } from 'react';
import type { MediaItem, MediaPart } from '../../../api/Media/mediaService';
import { isImageSubtitleCodec, isNoSubtitle, NoSubtitle } from '../../../utils/subtitleKind';
import FindSubtitlesPanel from './FindSubtitlesPanel';

type SubtitleTrackType = NonNullable<MediaPart['subtitleTracks']>[number];

interface SubtitleTracksPanelProps {
    mediaDetails: MediaItem | null;
    activePart: MediaPart | undefined;
    selectedSubtitleId: string;
    mediaItemId: string;
    serverId?: string;
    canFindSubtitles: boolean;
    // Applies immediately. Text subtitles are attached to the playing <video> as
    // a sideloaded track, so there is nothing to confirm — an Apply step here
    // would only add a click to something already reversible.
    onSelect: (subtitleTrackId: string) => void | Promise<void>;
    onSubtitleDownloaded: (subtitleTrackId: string) => void | Promise<void>;
    onClose: () => void;
}

function trackLabel(track: SubtitleTrackType): string {
    return track.title || track.language || 'Unknown';
}

const mutedStyle: React.CSSProperties = { color: 'var(--vora-text-muted)' };

function SubtitleRow({ id, label, hint, isSelected, onSelect }: {
    id: string;
    label: string;
    hint?: string;
    isSelected: boolean;
    onSelect: (id: string) => void | Promise<void>;
}) {
    return (
        <button
            type="button"
            onClick={() => onSelect(id)}
            className="flex w-full cursor-pointer items-center justify-between rounded-md p-3 text-left text-sm transition-colors"
            style={{
                background: isSelected ? 'var(--vora-accent-soft)' : 'var(--vora-bg-sunken)',
                border: `1px solid ${isSelected ? 'var(--vora-accent-500)' : 'var(--vora-border-subtle)'}`,
                color: 'var(--vora-text-primary)',
            }}
        >
            <span className="min-w-0 truncate">
                {label}
                {hint && <span className="ml-2 text-xs" style={mutedStyle}>{hint}</span>}
            </span>
            {isSelected && (
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" className="shrink-0">
                    <polyline points="20 6 9 17 4 12" />
                </svg>
            )}
        </button>
    );
}

export default function SubtitleTracksPanel({
    mediaDetails,
    activePart,
    selectedSubtitleId,
    mediaItemId,
    serverId,
    canFindSubtitles,
    onSelect,
    onSubtitleDownloaded,
    onClose,
}: SubtitleTracksPanelProps) {
    const [isFindingSubtitles, setIsFindingSubtitles] = useState(false);

    const tracks = activePart?.subtitleTracks ?? [];
    const labelStyle = mutedStyle;

    return (
        <div
            className="absolute inset-0 z-50 flex animate-fade-in items-center justify-center backdrop-blur-md"
            onClick={onClose}
            style={{ background: 'rgba(0, 0, 0, 0.78)' }}
        >
            <div
                className="w-full max-w-lg overflow-hidden rounded-2xl p-7"
                onClick={e => e.stopPropagation()}
                style={{
                    background: 'var(--vora-bg-raised)',
                    border: '1px solid var(--vora-border-strong)',
                    boxShadow: 'var(--vora-shadow-overlay)',
                }}
            >
                <div className="mb-6 flex items-center justify-between">
                    <h2 className="m-0 text-xl font-semibold" style={{ color: 'var(--vora-text-primary)', letterSpacing: '-0.01em' }}>
                        {isFindingSubtitles ? 'Find subtitles' : 'Subtitles'}
                    </h2>
                    <button
                        type="button"
                        onClick={onClose}
                        aria-label="Close"
                        className="inline-flex h-8 w-8 cursor-pointer items-center justify-center rounded-full transition-colors hover:bg-white/5"
                        style={labelStyle}
                    >
                        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><line x1="18" y1="6" x2="6" y2="18" /><line x1="6" y1="6" x2="18" y2="18" /></svg>
                    </button>
                </div>

                {isFindingSubtitles ? (
                    <FindSubtitlesPanel
                        mediaItemId={mediaItemId}
                        serverId={serverId}
                        onBack={() => setIsFindingSubtitles(false)}
                        onDownloaded={async trackId => {
                            await onSubtitleDownloaded(trackId);
                            setIsFindingSubtitles(false);
                        }}
                    />
                ) : mediaDetails ? (
                    <div className="space-y-4">
                        <div className="max-h-80 space-y-2 overflow-y-auto pr-1">
                            <SubtitleRow id={NoSubtitle} label="Off" isSelected={isNoSubtitle(selectedSubtitleId)} onSelect={onSelect} />
                            {tracks.map((track: SubtitleTrackType) => (
                                <SubtitleRow
                                    key={track.id}
                                    id={track.id}
                                    label={trackLabel(track)}
                                    onSelect={onSelect}
                                    // Image subtitles have to be burned in by the
                                    // server, so picking one restarts the stream —
                                    // worth saying before the video reloads.
                                    hint={[
                                        track.codec?.toUpperCase(),
                                        track.isForced ? 'forced' : null,
                                        isImageSubtitleCodec(track.codec) ? 'restarts playback' : null,
                                    ].filter(Boolean).join(' · ')}
                                    isSelected={selectedSubtitleId === track.id}
                                />
                            ))}
                        </div>

                        {canFindSubtitles && (
                            <button
                                type="button"
                                onClick={() => setIsFindingSubtitles(true)}
                                className="inline-flex cursor-pointer items-center gap-1.5 text-xs font-semibold transition-colors"
                                style={{ color: 'var(--vora-accent-text)' }}
                            >
                                <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                                    <circle cx="11" cy="11" r="7" /><line x1="21" y1="21" x2="16.65" y2="16.65" />
                                </svg>
                                Find subtitles online
                            </button>
                        )}
                    </div>
                ) : (
                    <div className="py-10 text-center text-sm" style={labelStyle}>Loading subtitle tracks…</div>
                )}
            </div>
        </div>
    );
}
