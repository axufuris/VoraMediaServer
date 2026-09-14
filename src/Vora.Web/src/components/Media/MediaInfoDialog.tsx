import { useEffect, useId, useRef, type ReactNode } from 'react';
import type { AudioTrack, MediaPart, SubtitleTrack, VideoTrack } from '../../api/Media/mediaService';
import {
    fileNameOf,
    formatBitrate,
    formatChannels,
    formatCodec,
    formatFileSize,
    formatInfoDuration,
    formatLanguage,
    formatResolution,
    subtitleSource,
} from '../../utils/mediaInfo';

// The file-level facts for a movie or episode: where each file lives and what's
// in it. One dialog for both the video player and the media details page, so
// the two never describe the same file differently. The player lays it over the
// video (`absolute`); the details page lays it over the whole client (`fixed`,
// above the header).
interface MediaInfoDialogProps {
    parts: MediaPart[];
    placement: 'absolute' | 'fixed';
    onClose: () => void;
}

type StatValue = string | number | null | undefined;

function Stat({ label, value }: { label: string; value: StatValue }) {
    if (value === null || value === undefined || value === '') return null;
    return (
        <div className="flex items-baseline justify-between gap-4">
            <dt className="shrink-0 text-sm" style={{ color: 'var(--vora-text-muted)' }}>{label}</dt>
            <dd className="m-0 min-w-0 break-words text-right text-sm font-medium" style={{ color: 'var(--vora-text-primary)' }}>{value}</dd>
        </div>
    );
}

function Section({ title, children }: { title: string; children: ReactNode }) {
    return (
        <section className="min-w-0">
            <h4 className="m-0 mb-3 text-base font-semibold" style={{ color: 'var(--vora-text-primary)' }}>{title}</h4>
            {children}
        </section>
    );
}

function StatList({ children, divided }: { children: ReactNode; divided?: boolean }) {
    return (
        <dl className={`m-0 space-y-2 ${divided ? 'border-t pt-4' : ''}`} style={divided ? { borderColor: 'var(--vora-border-subtle)' } : undefined}>
            {children}
        </dl>
    );
}

const yesNo = (value?: boolean) => (value ? 'Yes' : null);

function VideoStats({ track }: { track: VideoTrack }) {
    return (
        <>
            <Stat label="Codec" value={formatCodec(track.codec)} />
            <Stat label="Profile" value={track.profile} />
            <Stat label="Bit depth" value={track.bitDepth ? `${track.bitDepth}-bit` : null} />
            <Stat label="HDR" value={track.hdrType} />
            <Stat label="Bitrate" value={formatBitrate(track.bitrateKbps)} />
            <Stat label="Default" value={yesNo(track.isDefault)} />
        </>
    );
}

function AudioStats({ track }: { track: AudioTrack }) {
    return (
        <>
            <Stat label="Codec" value={formatCodec(track.codec)} />
            <Stat label="Channels" value={formatChannels(track.channels)} />
            <Stat label="Language" value={formatLanguage(track.language)} />
            <Stat label="Title" value={track.title} />
            <Stat label="Default" value={yesNo(track.isDefault)} />
        </>
    );
}

function SubtitleStats({ track }: { track: SubtitleTrack }) {
    return (
        <>
            <Stat label="Codec" value={formatCodec(track.codec)} />
            <Stat label="Language" value={formatLanguage(track.language)} />
            <Stat label="Title" value={track.title} />
            <Stat label="Source" value={subtitleSource(track)} />
            <Stat label="Forced" value={yesNo(track.isForced)} />
            <Stat label="Default" value={yesNo(track.isDefault)} />
        </>
    );
}

function PartInfo({ part, index, count }: { part: MediaPart; index: number; count: number }) {
    const videoTracks = part.videoTracks ?? [];
    const audioTracks = part.audioTracks ?? [];
    const subtitleTracks = part.subtitleTracks ?? [];

    return (
        <div className="grid grid-cols-1 gap-x-10 gap-y-8 sm:grid-cols-2 xl:grid-cols-4">
            <Section title={count > 1 ? `Part ${index + 1}` : 'Part'}>
                <StatList>
                    <Stat label="File" value={fileNameOf(part.filePath)} />
                    <Stat label="Duration" value={formatInfoDuration(part.durationSeconds)} />
                    <Stat label="Size" value={formatFileSize(part.fileSizeBytes)} />
                    <Stat label="Bitrate" value={formatBitrate(part.bitrateKbps)} />
                    <Stat label="Resolution" value={formatResolution(part.resolution)} />
                    <Stat label="Container" value={formatCodec(part.container)} />
                    <Stat label="Version" value={part.versionName} />
                    <Stat label="Edition" value={part.edition} />
                </StatList>
            </Section>

            <Section title={videoTracks.length > 1 ? `Video · ${videoTracks.length}` : 'Video'}>
                {videoTracks.length === 0 ? <Empty /> : videoTracks.map((track, i) => (
                    <StatList key={track.id} divided={i > 0}><VideoStats track={track} /></StatList>
                ))}
            </Section>

            <Section title={audioTracks.length > 1 ? `Audio · ${audioTracks.length}` : 'Audio'}>
                {audioTracks.length === 0 ? <Empty /> : (
                    <div className="space-y-4">
                        {audioTracks.map((track, i) => (
                            <StatList key={track.id} divided={i > 0}><AudioStats track={track} /></StatList>
                        ))}
                    </div>
                )}
            </Section>

            <Section title={subtitleTracks.length > 1 ? `Subtitles · ${subtitleTracks.length}` : 'Subtitles'}>
                {subtitleTracks.length === 0 ? <Empty /> : (
                    <div className="space-y-4">
                        {subtitleTracks.map((track, i) => (
                            <StatList key={track.id} divided={i > 0}><SubtitleStats track={track} /></StatList>
                        ))}
                    </div>
                )}
            </Section>
        </div>
    );
}

function Empty() {
    return <p className="m-0 text-sm" style={{ color: 'var(--vora-text-disabled)' }}>None</p>;
}

export default function MediaInfoDialog({ parts, placement, onClose }: MediaInfoDialogProps) {
    const titleId = useId();
    const closeRef = useRef<HTMLButtonElement>(null);

    useEffect(() => {
        closeRef.current?.focus();
        const onKey = (e: KeyboardEvent) => {
            if (e.key === 'Escape') {
                e.stopPropagation();
                onClose();
            }
        };
        window.addEventListener('keydown', onKey);
        return () => window.removeEventListener('keydown', onKey);
    }, [onClose]);

    return (
        <div
            className={`${placement === 'fixed' ? 'fixed z-[200] p-4' : 'absolute z-50'} inset-0 flex animate-fade-in items-center justify-center backdrop-blur-md`}
            onClick={onClose}
            style={{ background: 'color-mix(in srgb, var(--vora-bg-canvas) 78%, transparent)' }}
        >
            <div
                role="dialog"
                aria-modal="true"
                aria-labelledby={titleId}
                className="flex max-h-[85vh] w-full max-w-5xl flex-col overflow-hidden rounded-2xl p-6"
                onClick={e => e.stopPropagation()}
                style={{
                    background: 'var(--vora-bg-raised)',
                    border: '1px solid var(--vora-border-strong)',
                    boxShadow: 'var(--vora-shadow-overlay)',
                }}
            >
                <div className="mb-6 flex shrink-0 items-center justify-between">
                    <h2 id={titleId} className="m-0 flex items-center gap-2 text-xl font-semibold" style={{ color: 'var(--vora-text-primary)', letterSpacing: '-0.01em' }}>
                        <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" style={{ color: 'var(--vora-text-muted)' }} aria-hidden="true">
                            <circle cx="12" cy="12" r="10" />
                            <line x1="12" y1="16" x2="12" y2="12" />
                            <line x1="12" y1="8" x2="12.01" y2="8" />
                        </svg>
                        Media info
                    </h2>
                    <button
                        ref={closeRef}
                        type="button"
                        onClick={onClose}
                        aria-label="Close"
                        className="vora-icon-button inline-flex h-8 w-8 cursor-pointer items-center justify-center rounded-full"
                        style={{ color: 'var(--vora-text-muted)' }}
                    >
                        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true"><line x1="18" y1="6" x2="6" y2="18" /><line x1="6" y1="6" x2="18" y2="18" /></svg>
                    </button>
                </div>

                <div className="flex-1 space-y-8 overflow-y-auto pr-3">
                    <section>
                        <h3 className="m-0 mb-3 text-xs font-semibold uppercase tracking-wider" style={{ color: 'var(--vora-text-muted)' }}>
                            {parts.length > 1 ? `Files · ${parts.length}` : 'File'}
                        </h3>
                        {parts.length === 0 ? <Empty /> : (
                            <div className="space-y-2">
                                {parts.map(part => (
                                    <div
                                        key={part.id}
                                        className="select-all break-all rounded p-3 font-mono text-xs"
                                        style={{ background: 'var(--vora-bg-sunken)', border: '1px solid var(--vora-border-subtle)', color: 'var(--vora-text-secondary)' }}
                                    >
                                        {part.filePath}
                                    </div>
                                ))}
                            </div>
                        )}
                    </section>

                    {parts.map((part, index) => (
                        <div key={part.id} className={index > 0 ? 'border-t pt-8' : undefined} style={index > 0 ? { borderColor: 'var(--vora-border-subtle)' } : undefined}>
                            <PartInfo part={part} index={index} count={parts.length} />
                        </div>
                    ))}
                </div>
            </div>
        </div>
    );
}
