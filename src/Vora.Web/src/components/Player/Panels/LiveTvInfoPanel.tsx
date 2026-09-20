import type { IptvProgramDto } from '../../../api/Iptv/iptvClientService';

interface LiveTvInfoPanelProps {
    activeProgram: IptvProgramDto | undefined;
    streamUrl?: string;
    formatTime: (dateStr: string) => string;
}

function Row({ label, value }: { label: string, value: React.ReactNode }) {
    return (
        <div className="flex items-start justify-between gap-3">
            <span className="text-sm" style={{ color: 'var(--vora-text-muted)' }}>{label}</span>
            <span className="max-w-[200px] truncate text-right text-sm font-medium" style={{ color: 'var(--vora-text-primary)' }}>{value}</span>
        </div>
    );
}

export default function LiveTvInfoPanel({ activeProgram, streamUrl, formatTime }: LiveTvInfoPanelProps) {
    return (
        <div
            className="pointer-events-auto absolute right-8 top-24 z-40 w-full max-w-sm rounded-2xl p-5 text-left backdrop-blur-md"
            style={{
                background: 'rgba(20, 20, 28, 0.78)',
                border: '1px solid rgba(255, 255, 255, 0.16)',
                boxShadow: 'var(--vora-shadow-overlay)',
                color: '#fafafa',
            }}
        >
            <h3 className="m-0 mb-4 pb-2 text-base font-semibold" style={{ borderBottom: '1px solid rgba(255, 255, 255, 0.14)' }}>Stream info</h3>
            <div className="space-y-3">
                <Row label="Current program" value={activeProgram?.title || 'Live TV'} />
                {activeProgram && (
                    <Row label="Time" value={`${formatTime(activeProgram.startTime)} – ${formatTime(activeProgram.endTime)}`} />
                )}
                {streamUrl && <Row label="Source" value={<span className="font-mono">{streamUrl}</span>} />}
                <Row label="Stream type" value="Timeshifted HLS" />
            </div>
        </div>
    );
}
