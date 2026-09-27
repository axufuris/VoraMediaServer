import { type IptvChannelVM } from '../../api/Iptv/iptvAdminService';
import { type IptvProgramDto } from '../../api/Iptv/iptvClientService';

interface GuideProgramModalProps {
    channel: IptvChannelVM;
    program: IptvProgramDto;
    canRecord: boolean;
    formatTimeRange: (program: IptvProgramDto) => string;
    onPlay: () => void;
    onRecordEpisode: () => void;
    onRecordSeries: (retention: number) => void;
    onCancel: () => void;
}

export default function GuideProgramModal({
    channel,
    program,
    canRecord,
    formatTimeRange,
    onPlay,
    onRecordEpisode,
    onRecordSeries,
    onCancel,
}: GuideProgramModalProps) {
    return (
        <div
            className="fixed inset-0 z-[99999] flex items-center justify-center p-4 backdrop-blur-md"
            style={{ background: 'rgba(0, 0, 0, 0.78)' }}
        >
            <div
                className="w-full max-w-md overflow-hidden rounded-2xl p-6"
                style={{
                    background: 'var(--vora-bg-raised)',
                    border: '1px solid var(--vora-border-strong)',
                    boxShadow: 'var(--vora-shadow-overlay)',
                }}
            >
                <h3 className="m-0 text-xl font-semibold" style={{ color: 'var(--vora-text-primary)', letterSpacing: '-0.01em' }}>{program.title}</h3>
                <p className="m-0 mb-6 mt-1 text-sm" style={{ color: 'var(--vora-text-muted)' }}>
                    {channel.name} · {formatTimeRange(program)}
                </p>

                <div className="flex flex-col gap-3">
                    <button
                        type="button"
                        onClick={onPlay}
                        className="vora-button-primary flex w-full cursor-pointer items-center justify-center gap-2"
                    >
                        <svg width="18" height="18" viewBox="0 0 24 24" fill="currentColor"><polygon points="5 3 19 12 5 21 5 3" /></svg>
                        Play channel
                    </button>

                    {canRecord && (
                        <>
                            <button
                                type="button"
                                onClick={onRecordEpisode}
                                className="flex w-full cursor-pointer items-center justify-center gap-2 rounded-md py-3 text-sm font-semibold transition-colors"
                                style={{ background: 'var(--vora-danger-500)', color: '#ffffff' }}
                            >
                                <span className="h-2.5 w-2.5 rounded-full bg-white" />
                                Record this episode
                            </button>

                            <div
                                className="mt-1 rounded-md p-3"
                                style={{ background: 'var(--vora-bg-sunken)', border: '1px solid var(--vora-border-subtle)' }}
                            >
                                <label className="mb-2 block text-xs font-semibold uppercase tracking-wider" style={{ color: 'var(--vora-text-muted)' }}>Series retention</label>
                                <div className="flex items-center justify-between gap-2">
                                    <span className="text-sm" style={{ color: 'var(--vora-text-primary)' }}>Keep maximum episodes:</span>
                                    <select
                                        id="retention-select"
                                        className="cursor-pointer rounded-md px-2 py-1 text-sm outline-none"
                                        defaultValue="0"
                                        style={{ background: 'var(--vora-bg-raised)', border: '1px solid var(--vora-border-subtle)', color: 'var(--vora-text-primary)' }}
                                    >
                                        <option value="0">All</option>
                                        <option value="5">Latest 5</option>
                                        <option value="10">Latest 10</option>
                                        <option value="20">Latest 20</option>
                                    </select>
                                </div>
                            </div>

                            <button
                                type="button"
                                onClick={() => {
                                    const retention = parseInt((document.getElementById('retention-select') as HTMLSelectElement).value, 10);
                                    onRecordSeries(retention);
                                }}
                                className="vora-button-secondary flex w-full cursor-pointer items-center justify-center gap-2"
                                style={{ color: 'var(--vora-accent-text)', borderColor: 'var(--vora-accent-soft-hover)' }}
                            >
                                Record series
                            </button>
                        </>
                    )}

                    <button
                        type="button"
                        onClick={onCancel}
                        className="vora-button-secondary mt-1 w-full cursor-pointer"
                    >
                        Cancel
                    </button>
                </div>
            </div>
        </div>
    );
}
