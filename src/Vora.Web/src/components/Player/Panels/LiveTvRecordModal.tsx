import type { IptvChannelVM } from '../../../api/Iptv/iptvAdminService';
import type { IptvProgramDto } from '../../../api/Iptv/iptvClientService';

interface LiveTvRecordModalProps {
    channel: IptvChannelVM;
    program: IptvProgramDto;
    formatTime: (dateStr: string) => string;
    recordRetention: number;
    onChangeRetention: (n: number) => void;
    onRecordEpisode: () => void;
    onRecordSeries: () => void;
    onCancel: () => void;
}

export default function LiveTvRecordModal({
    channel,
    program,
    formatTime,
    recordRetention,
    onChangeRetention,
    onRecordEpisode,
    onRecordSeries,
    onCancel,
}: LiveTvRecordModalProps) {
    return (
        <div
            className="pointer-events-auto fixed inset-0 z-[999999] flex items-center justify-center p-4 backdrop-blur-md"
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
                    {channel.name} · {formatTime(program.startTime)} – {formatTime(program.endTime)}
                </p>

                <div className="flex flex-col gap-3">
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
                        className="mt-2 rounded-md p-3"
                        style={{ background: 'var(--vora-bg-sunken)', border: '1px solid var(--vora-border-subtle)' }}
                    >
                        <label className="mb-2 block text-xs font-semibold uppercase tracking-wider" style={{ color: 'var(--vora-text-muted)' }}>Series retention</label>
                        <div className="flex items-center justify-between gap-2">
                            <span className="text-sm" style={{ color: 'var(--vora-text-primary)' }}>Keep maximum episodes:</span>
                            <select
                                value={recordRetention}
                                onChange={e => onChangeRetention(parseInt(e.target.value, 10))}
                                className="cursor-pointer rounded-md px-2 py-1 text-sm outline-none"
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
                        onClick={onRecordSeries}
                        className="vora-button-primary w-full cursor-pointer"
                    >
                        Record series
                    </button>

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
