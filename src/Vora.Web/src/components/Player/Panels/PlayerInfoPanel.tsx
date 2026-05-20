import type { MediaItem, MediaPart } from '../../../api/Media/mediaService';

type VideoTrackType = NonNullable<MediaPart['videoTracks']>[number];
type AudioTrackType = NonNullable<MediaPart['audioTracks']>[number];

interface PlayerInfoPanelProps {
    mediaDetails: MediaItem | null;
    onClose: () => void;
}

const labelStyle: React.CSSProperties = { color: 'var(--vora-text-muted)' };
const valueStyle: React.CSSProperties = { color: 'var(--vora-text-primary)' };

function Stat({ label, value }: { label: string, value: React.ReactNode }) {
    return (
        <div className="flex items-center justify-between">
            <span className="text-sm" style={labelStyle}>{label}</span>
            <span className="text-sm font-medium" style={valueStyle}>{value}</span>
        </div>
    );
}

export default function PlayerInfoPanel({ mediaDetails, onClose }: PlayerInfoPanelProps) {
    return (
        <div
            className="absolute inset-0 z-50 flex animate-fade-in items-center justify-center backdrop-blur-md"
            onClick={onClose}
            style={{ background: 'rgba(0, 0, 0, 0.78)' }}
        >
            <div
                className="flex max-h-[85vh] w-full max-w-4xl flex-col overflow-hidden rounded-2xl p-6"
                onClick={e => e.stopPropagation()}
                style={{
                    background: 'var(--vora-bg-raised)',
                    border: '1px solid var(--vora-border-strong)',
                    boxShadow: 'var(--vora-shadow-overlay)',
                }}
            >
                <div className="mb-6 flex shrink-0 items-center justify-between">
                    <h2 className="m-0 flex items-center gap-2 text-xl font-semibold" style={{ color: 'var(--vora-text-primary)', letterSpacing: '-0.01em' }}>
                        <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" style={{ color: 'var(--vora-text-muted)' }}>
                            <circle cx="12" cy="12" r="10" />
                            <line x1="12" y1="16" x2="12" y2="12" />
                            <line x1="12" y1="8" x2="12.01" y2="8" />
                        </svg>
                        Media info
                    </h2>
                    <button
                        type="button"
                        onClick={onClose}
                        aria-label="Close"
                        className="inline-flex h-8 w-8 cursor-pointer items-center justify-center rounded-full transition-colors hover:bg-white/5"
                        style={{ color: 'var(--vora-text-muted)' }}
                    >
                        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><line x1="18" y1="6" x2="6" y2="18" /><line x1="6" y1="6" x2="18" y2="18" /></svg>
                    </button>
                </div>

                <div className="flex-1 space-y-8 overflow-y-auto pr-3">
                    <div>
                        <h3 className="m-0 mb-3 text-xs font-semibold uppercase tracking-wider" style={labelStyle}>Files</h3>
                        <div className="space-y-2">
                            {mediaDetails?.mediaParts?.map((part: MediaPart) => (
                                <div
                                    key={part.id}
                                    className="break-all rounded p-3 font-mono text-xs"
                                    style={{ background: 'var(--vora-bg-sunken)', border: '1px solid var(--vora-border-subtle)', color: 'var(--vora-text-secondary)' }}
                                >
                                    {part.filePath}
                                </div>
                            ))}
                        </div>
                    </div>

                    <div className="space-y-6">
                        {mediaDetails?.mediaParts?.map((part: MediaPart, idx: number) => (
                            <div key={part.id} className="grid grid-cols-1 gap-x-12 gap-y-6 md:grid-cols-2">
                                <div>
                                    <h4 className="m-0 mb-3 text-base font-semibold" style={{ color: 'var(--vora-text-primary)' }}>Media</h4>
                                    <div className="space-y-2">
                                        {part.resolution && <Stat label="Resolution" value={part.resolution === '2160p' ? '4K' : part.resolution} />}
                                        {part.bitrateKbps && <Stat label="Bitrate" value={`${part.bitrateKbps} kbps`} />}
                                        {part.fileSizeBytes && <Stat label="Size" value={`${(part.fileSizeBytes / (1024 * 1024 * 1024)).toFixed(2)} GB`} />}
                                    </div>
                                </div>

                                <div>
                                    <h4 className="m-0 mb-3 text-base font-semibold" style={{ color: 'var(--vora-text-primary)' }}>Part {idx + 1}</h4>
                                    <div className="space-y-6">
                                        {part.videoTracks?.map((vt: VideoTrackType) => (
                                            <div key={vt.id} className="space-y-2">
                                                <Stat label="Video codec" value={<span className="uppercase">{vt.codec}</span>} />
                                                {vt.profile && <Stat label="Profile" value={vt.profile} />}
                                                {vt.hdrType && <Stat label="HDR" value={vt.hdrType} />}
                                            </div>
                                        ))}

                                        {part.audioTracks?.map((at: AudioTrackType) => (
                                            <div key={at.id} className="space-y-2 border-t pt-4" style={{ borderColor: 'var(--vora-border-subtle)' }}>
                                                <Stat label="Audio codec" value={<span className="uppercase">{at.codec}</span>} />
                                                <Stat label="Channels" value={at.channels} />
                                                {at.language && <Stat label="Language" value={<span className="uppercase">{at.language}</span>} />}
                                            </div>
                                        ))}
                                    </div>
                                </div>
                            </div>
                        ))}
                    </div>
                </div>
            </div>
        </div>
    );
}
