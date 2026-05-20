import { useEffect, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { streamingAdminService } from '../../../api/Streaming/streamingAdminService';

export default function ActivityPill() {
    const { serverId } = useParams<{ serverId?: string }>();
    const navigate = useNavigate();
    const [streamCount, setStreamCount] = useState<number | null>(null);
    const [transcodeCount, setTranscodeCount] = useState<number>(0);

    useEffect(() => {
        let cancelled = false;
        const refresh = async () => {
            try {
                const sessions = await streamingAdminService.getNowPlaying(serverId);
                if (cancelled) return;
                setStreamCount(sessions.length);
                setTranscodeCount(sessions.filter(s =>
                    s.videoStrategy?.toLowerCase().includes('transcode') ||
                    s.audioStrategy?.toLowerCase().includes('transcode')
                ).length);
            } catch {
                if (!cancelled) setStreamCount(0);
            }
        };
        refresh();
        const handle = setInterval(refresh, 5000);
        return () => { cancelled = true; clearInterval(handle); };
    }, [serverId]);

    if (streamCount === null) return null;
    if (streamCount === 0) return null;

    return (
        <button
            type="button"
            onClick={() => navigate(serverId ? `/server/${serverId}/admin` : '/admin')}
            className="flex items-center gap-2 px-2.5 py-1 rounded-full border border-[var(--vora-border-subtle)] hover:border-[var(--vora-border-strong)] bg-[var(--vora-bg-surface)] transition-colors cursor-pointer"
            title="View now playing"
        >
            <span className="relative flex h-2 w-2">
                <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-[var(--vora-accent-500)] opacity-75"></span>
                <span className="relative inline-flex rounded-full h-2 w-2 bg-[var(--vora-accent-500)]"></span>
            </span>
            <span className="text-xs font-semibold text-[var(--vora-text-primary)] tabular-nums">{streamCount}</span>
            <span className="text-xs text-[var(--vora-text-muted)]">streaming</span>
            {transcodeCount > 0 && (
                <span className="text-xs text-[var(--vora-text-muted)] border-l border-[var(--vora-border-subtle)] pl-2">
                    <span className="font-semibold text-[var(--vora-text-primary)] tabular-nums">{transcodeCount}</span> transcoding
                </span>
            )}
        </button>
    );
}
