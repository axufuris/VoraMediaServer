import type { MediaItem, MediaPart } from '../../../api/Media/mediaService';

type VideoTrackType = NonNullable<MediaPart['videoTracks']>[number];
type AudioTrackType = NonNullable<MediaPart['audioTracks']>[number];
type SubtitleTrackType = NonNullable<MediaPart['subtitleTracks']>[number];

interface DeviceCapabilities {
    videoCodecs: string[];
    audioCodecs: string[];
    maxAudioChannels: number;
}

interface PlayerSettingsPanelProps {
    mediaDetails: MediaItem | null;
    selVideo: string;
    selAudio: string;
    selSub: string;
    setSelVideo: (id: string) => void;
    setSelAudio: (id: string) => void;
    setSelSub: (id: string) => void;
    caps: DeviceCapabilities;
    onCancel: () => void;
    onApply: () => void;
}

export default function PlayerSettingsPanel({
    mediaDetails,
    selVideo,
    selAudio,
    selSub,
    setSelVideo,
    setSelAudio,
    setSelSub,
    caps,
    onCancel,
    onApply
}: PlayerSettingsPanelProps) {
    const activeSettingsPart = mediaDetails?.mediaParts?.find((p: MediaPart) =>
        p.videoTracks?.some((vt: VideoTrackType) => vt.id === selVideo)
    ) || mediaDetails?.mediaParts?.[0];

    const sortedAudioTracks = activeSettingsPart?.audioTracks
        ? [...activeSettingsPart.audioTracks].sort((a: AudioTrackType, b: AudioTrackType) => (b.channels || 0) - (a.channels || 0))
        : [];

    const selectStyle: React.CSSProperties = {
        background: 'var(--vora-bg-sunken)',
        border: '1px solid var(--vora-border-subtle)',
        color: 'var(--vora-text-primary)',
    };
    const labelStyle: React.CSSProperties = { color: 'var(--vora-text-muted)' };

    return (
        <div
            className="absolute inset-0 z-50 flex animate-fade-in items-center justify-center backdrop-blur-md"
            onClick={onCancel}
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
                    <h2 className="m-0 text-xl font-semibold" style={{ color: 'var(--vora-text-primary)', letterSpacing: '-0.01em' }}>Playback settings</h2>
                    <button
                        type="button"
                        onClick={onCancel}
                        aria-label="Close"
                        className="inline-flex h-8 w-8 cursor-pointer items-center justify-center rounded-full transition-colors hover:bg-white/5"
                        style={{ color: 'var(--vora-text-muted)' }}
                    >
                        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><line x1="18" y1="6" x2="6" y2="18" /><line x1="6" y1="6" x2="18" y2="18" /></svg>
                    </button>
                </div>

                {mediaDetails?.mediaParts && mediaDetails.mediaParts.length > 0 ? (
                    <div className="space-y-5">
                        <div>
                            <label className="mb-1.5 block text-xs font-semibold uppercase tracking-wider" style={labelStyle}>Video track</label>
                            <select
                                value={selVideo}
                                onChange={e => {
                                    const newVid = e.target.value;
                                    setSelVideo(newVid);
                                    const newPart = mediaDetails?.mediaParts?.find((p: MediaPart) => p.videoTracks?.some((vt: VideoTrackType) => vt.id === newVid));
                                    if (newPart) {
                                        if (newPart.audioTracks && newPart.audioTracks.length > 0) {
                                            const sorted = [...newPart.audioTracks].sort((a: AudioTrackType, b: AudioTrackType) => {
                                                const aTranscode = !caps.audioCodecs.includes(a.codec?.toLowerCase() || '') || ((a.channels || 2) > caps.maxAudioChannels);
                                                const bTranscode = !caps.audioCodecs.includes(b.codec?.toLowerCase() || '') || ((b.channels || 2) > caps.maxAudioChannels);
                                                if (aTranscode !== bTranscode) return aTranscode ? 1 : -1;
                                                return (b.channels || 0) - (a.channels || 0);
                                            });
                                            setSelAudio(sorted[0].id);
                                        } else {
                                            setSelAudio('');
                                        }
                                        setSelSub('none');
                                    }
                                }}
                                className="w-full cursor-pointer rounded-md p-2.5 text-sm outline-none"
                                style={selectStyle}
                            >
                                {mediaDetails.mediaParts.flatMap((part: MediaPart) =>
                                    part.videoTracks?.map((vt: VideoTrackType) => {
                                        const willTranscode = !caps.videoCodecs.includes(vt.codec?.toLowerCase() || '');
                                        const displayRes = part.resolution === '2160p' ? '4K' : (part.resolution || 'Unknown');
                                        const bitrateStr = part.bitrateKbps ? `${(part.bitrateKbps / 1000).toFixed(1)} Mbps` : 'Unknown Mbps';
                                        return (
                                            <option key={vt.id} value={vt.id}>
                                                {displayRes} · {vt.codec?.toUpperCase()} ({bitrateStr}){vt.hdrType ? ` · ${vt.hdrType}` : ''}{willTranscode ? ' · transcode' : ' · direct'}
                                            </option>
                                        );
                                    })
                                )}
                            </select>
                        </div>
                        <div>
                            <label className="mb-1.5 block text-xs font-semibold uppercase tracking-wider" style={labelStyle}>Audio track</label>
                            <select
                                value={selAudio}
                                onChange={e => setSelAudio(e.target.value)}
                                className="w-full cursor-pointer rounded-md p-2.5 text-sm outline-none"
                                style={selectStyle}
                            >
                                {sortedAudioTracks.map((at: AudioTrackType) => {
                                    const willTranscode = !caps.audioCodecs.includes(at.codec?.toLowerCase() || '') || ((at.channels || 2) > caps.maxAudioChannels);
                                    const name = at.title || at.language || 'Unknown';
                                    return (
                                        <option key={at.id} value={at.id}>
                                            {name} · {at.codec?.toUpperCase()} ({at.channels}ch){at.isDefault ? ' · default' : ''}{willTranscode ? ' · transcode' : ' · direct'}
                                        </option>
                                    );
                                })}
                            </select>
                        </div>
                        <div>
                            <label className="mb-1.5 block text-xs font-semibold uppercase tracking-wider" style={labelStyle}>Subtitles</label>
                            <select
                                value={selSub}
                                onChange={e => setSelSub(e.target.value)}
                                className="w-full cursor-pointer rounded-md p-2.5 text-sm outline-none"
                                style={selectStyle}
                            >
                                <option value="none">Off</option>
                                {activeSettingsPart?.subtitleTracks?.map((st: SubtitleTrackType) => (
                                    <option key={st.id} value={st.id}>
                                        {st.title || st.language || 'Unknown'} · {st.codec?.toUpperCase()}{st.isDefault ? ' · default' : ''}{st.isForced ? ' · forced' : ''}
                                    </option>
                                ))}
                            </select>
                        </div>
                        <div className="mt-7 flex justify-end gap-2 border-t pt-5" style={{ borderColor: 'var(--vora-border-subtle)' }}>
                            <button type="button" onClick={onCancel} className="vora-button-secondary cursor-pointer">Cancel</button>
                            <button type="button" onClick={onApply} className="vora-button-primary cursor-pointer">Apply</button>
                        </div>
                    </div>
                ) : (
                    <div className="py-10 text-center text-sm" style={{ color: 'var(--vora-text-muted)' }}>Loading track information…</div>
                )}
            </div>
        </div>
    );
}
