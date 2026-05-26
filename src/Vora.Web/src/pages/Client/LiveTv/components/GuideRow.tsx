import { type MouseEvent } from 'react';
import { type IptvChannelVM } from '../../../../api/Iptv/iptvAdminService';
import { type IptvProgramDto } from '../../../../api/Iptv/iptvClientService';
import { type IptvRecordingSessionVM } from '../../../../api/Iptv/dvrService';
import { HOURS_TO_SHOW, PX_PER_MINUTE } from '../guideConstants';

export type CleanedProgram = IptvProgramDto & { _safeStart: number, _safeEnd: number };

interface GuideRowProps {
    channel: IptvChannelVM;
    cleanPrograms: CleanedProgram[];
    timelineStart: Date;
    timelineEnd: Date;
    recordingSessions: IptvRecordingSessionVM[];
    isCurrentlyPlaying: boolean;
    isFavorite: boolean;
    onPlay: (channel: IptvChannelVM, program?: IptvProgramDto) => void;
    onHover: (channel: IptvChannelVM, program: IptvProgramDto | null) => void;
    onRightClick: (e: MouseEvent, channelId: string) => void;
    onProgramContextMenu: (channel: IptvChannelVM, program: IptvProgramDto) => void;
}

export default function GuideRow({
    channel,
    cleanPrograms,
    timelineStart,
    timelineEnd,
    recordingSessions,
    isCurrentlyPlaying,
    isFavorite,
    onPlay,
    onHover,
    onRightClick,
    onProgramContextMenu,
}: GuideRowProps) {
    const now = new Date();
    const currentProgram = cleanPrograms.find(p => p._safeStart <= now.getTime() && p._safeEnd > now.getTime());

    return (
        <div
            onContextMenu={(e) => onRightClick(e, channel.externalChannelId)}
            className="vora-guide-row group flex h-[80px] transition-colors"
            style={{
                background: isCurrentlyPlaying ? 'rgba(255, 255, 255, 0.04)' : 'transparent',
                borderBottom: '1px solid var(--vora-border-subtle)',
            }}
        >
            <div
                onClick={() => onPlay(channel, currentProgram)}
                onMouseEnter={() => onHover(channel, currentProgram || null)}
                className="vora-guide-channel sticky left-0 z-20 flex w-64 shrink-0 cursor-pointer items-center gap-3 p-3 transition-colors"
                style={{
                    background: isCurrentlyPlaying ? 'var(--vora-bg-raised)' : 'var(--vora-bg-surface)',
                    borderLeft: `3px solid ${isCurrentlyPlaying ? 'var(--vora-accent-500)' : 'transparent'}`,
                    borderRight: '1px solid var(--vora-border-subtle)',
                }}
            >
                <div
                    className="relative flex h-14 w-14 shrink-0 items-center justify-center overflow-hidden rounded-full"
                    style={{ background: 'var(--vora-bg-sunken)', border: '1px solid var(--vora-border-subtle)' }}
                >
                    {channel.logoUrl ? (
                        <img src={channel.logoUrl} alt="" className="max-h-[78%] max-w-[78%] object-contain" />
                    ) : (
                        <span className="text-[9px]" style={{ color: 'var(--vora-text-disabled)' }}>No logo</span>
                    )}
                    {isFavorite && (
                        <span
                            className="absolute -top-0.5 -right-0.5 flex h-4 w-4 items-center justify-center rounded-full"
                            style={{ background: 'var(--vora-accent-500)', color: 'var(--vora-accent-contrast)' }}
                        >
                            <svg width="9" height="9" viewBox="0 0 20 20" fill="currentColor"><path fillRule="evenodd" d="M3.172 5.172a4 4 0 015.656 0L10 6.343l1.172-1.171a4 4 0 115.656 5.656L10 17.657l-6.828-6.829a4 4 0 010-5.656z" clipRule="evenodd" /></svg>
                        </span>
                    )}
                </div>
                <div className="min-w-0 flex-1">
                    <div className="flex items-center justify-between gap-1.5">
                        <h3
                            className="m-0 truncate text-sm font-semibold"
                            style={{ color: isCurrentlyPlaying ? 'var(--vora-accent-text)' : 'var(--vora-text-primary)' }}
                        >
                            {channel.name}
                        </h3>
                        {channel.resolution && channel.resolution !== 'Unknown' && (
                            <span
                                className="shrink-0 rounded px-1.5 py-0.5 text-[9px] font-bold"
                                style={{ background: 'var(--vora-accent-soft)', color: 'var(--vora-accent-text)' }}
                            >
                                {channel.resolution}
                            </span>
                        )}
                    </div>
                    <div className="mt-0.5 flex items-center gap-2">
                        {channel.countryCode && channel.countryCode !== 'Unknown' && (
                            <span className="text-[9px] font-semibold uppercase tracking-wider" style={{ color: 'var(--vora-text-muted)' }}>
                                {channel.countryCode}
                            </span>
                        )}
                        {channel.groupTitle && (
                            <span className="truncate text-[10px] font-medium uppercase tracking-wider" style={{ color: 'var(--vora-text-muted)' }}>
                                {channel.groupTitle.replace(/;/g, ' • ')}
                            </span>
                        )}
                    </div>
                </div>
            </div>

            <div className="flex-1 relative overflow-hidden" style={{ width: `${HOURS_TO_SHOW * 60 * PX_PER_MINUTE}px` }}>
                {cleanPrograms.map(program => {
                    const start = new Date(program._safeStart);
                    const end = new Date(program._safeEnd);
                    if (end <= timelineStart || start >= timelineEnd) return null;

                    const startDiffMins = (start.getTime() - timelineStart.getTime()) / 60000;
                    const durationMins = (end.getTime() - start.getTime()) / 60000;
                    let left = startDiffMins * PX_PER_MINUTE;
                    let width = durationMins * PX_PER_MINUTE;

                    if (left < 0) { width += left; left = 0; }
                    if (width <= 0 || isNaN(width) || isNaN(left)) return null;

                    const isPlayingNow = start <= now && end > now;
                    const isRestricted = program.title === "Restricted Content";

                    const isScheduled = recordingSessions.some(s => {
                        if (s.status !== 'Pending' && s.status !== 'Recording') return false;

                        if (s.externalProgramId && s.externalProgramId === program.id) return true;

                        if (s.title !== program.title || s.schedule?.channel?.name !== channel.name) return false;

                        const sStart = new Date(s.startTime).getTime();
                        const sEnd = new Date(s.endTime).getTime();
                        const pStart = new Date(program._safeStart).getTime();
                        const pEnd = new Date(program._safeEnd).getTime();

                        const overlapStart = Math.max(sStart, pStart);
                        const overlapEnd = Math.min(sEnd, pEnd);

                        return (overlapEnd - overlapStart) > 360000;
                    });

                    const isActivePlayback = isCurrentlyPlaying && isPlayingNow;

                    let tileBg: string;
                    let tileBorder: string;
                    let tileShadow: string;
                    let tileZ: number;
                    if (isActivePlayback) {
                        tileBg = 'var(--vora-accent-soft)';
                        tileBorder = 'var(--vora-accent-500)';
                        tileShadow = '0 0 14px color-mix(in srgb, var(--vora-accent-500) 35%, transparent)';
                        tileZ = 20;
                    } else if (isPlayingNow) {
                        tileBg = 'var(--vora-bg-raised)';
                        tileBorder = 'var(--vora-border-strong)';
                        tileShadow = 'var(--vora-shadow-sm)';
                        tileZ = 10;
                    } else {
                        tileBg = 'var(--vora-bg-surface)';
                        tileBorder = 'var(--vora-border-subtle)';
                        tileShadow = 'none';
                        tileZ = 0;
                    }
                    if (isScheduled) {
                        tileBorder = 'var(--vora-danger-500)';
                    }

                    return (
                        <div
                            key={program.id}
                            onClick={(e) => {
                                e.stopPropagation();
                                onPlay(channel, program);
                            }}
                            onContextMenu={(e) => {
                                e.preventDefault();
                                e.stopPropagation();
                                onProgramContextMenu(channel, program);
                            }}
                            onMouseEnter={() => onHover(channel, program)}
                            className={`vora-guide-tile absolute bottom-1 top-1 cursor-pointer overflow-hidden transition-all duration-200 ${isRestricted ? 'opacity-50 grayscale' : ''}`}
                            style={{
                                left: `${left}px`,
                                width: `${width}px`,
                                background: tileBg,
                                border: `1px solid ${tileBorder}`,
                                borderRadius: 'var(--vora-radius-md)',
                                boxShadow: tileShadow,
                                zIndex: tileZ,
                            }}
                        >
                            <div className="flex h-full flex-col justify-center p-2">
                                <div className="flex items-center gap-1.5">
                                    {isScheduled && (
                                        <span
                                            className="h-2 w-2 shrink-0 rounded-full"
                                            style={{
                                                background: 'var(--vora-danger-500)',
                                                boxShadow: '0 0 6px color-mix(in srgb, var(--vora-danger-500) 70%, transparent)',
                                            }}
                                        />
                                    )}
                                    <h4
                                        className="m-0 truncate text-sm font-semibold"
                                        style={{ color: isPlayingNow || isActivePlayback ? 'var(--vora-text-primary)' : 'var(--vora-text-secondary)' }}
                                    >
                                        {program.title}
                                    </h4>
                                </div>
                            </div>
                        </div>
                    );
                })}
            </div>
        </div>
    );
}
