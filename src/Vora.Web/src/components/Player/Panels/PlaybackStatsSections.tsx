import { useEffect, useState } from 'react';
import { streamingService, type PlaybackStats } from '../../../api/Streaming/streamingService';
import { Section, Stat, StatList } from '../../Media/MediaInfoDialog';
import { formatCodec } from '../../../utils/mediaInfo';
import { knownLanguageName } from '../../../utils/trackLabels';
import {
    audioOutput,
    audioSourceDetail,
    audioSourceTitle,
    methodLabel,
    rateText,
    reasonLines,
    speedText,
    subtitleCodecName,
    subtitleSummary,
    videoOutput,
    videoSourceDetail,
    videoSourceTitle,
} from '../../../utils/playbackStats';

export const SERVER_REFRESH_MS = 2000;
export const PLAYER_REFRESH_MS = 1000;

// What the browser can say about playback, read from the <video> element and
// hls.js each second while Playback info is open.
export interface PlayerReading {
    engine: string;
    droppedFrames: number | null;
    totalFrames: number | null;
    bufferedSeconds: number | null;
    bandwidthKbps: number | null;
}

export interface SideloadedSubtitle {
    language?: string;
    title?: string;
    codec?: string;
}

interface PlaybackStatsSectionsProps {
    sessionId: string | null;
    serverId?: string;
    reasons?: string[];
    sideloadedSubtitle?: SideloadedSubtitle | null;
    readPlayer: () => PlayerReading;
}

const joined = (parts: (string | null | undefined)[]) => parts.filter((part): part is string => !!part).join(' · ');

function transcoderLine(stats: PlaybackStats): string | null {
    const transcoder = stats.transcoder;
    if (!transcoder) return null;
    const pace = transcoder.running
        ? joined([transcoder.fps != null ? `${Math.round(transcoder.fps)} fps` : null, speedText(transcoder.speed)])
        : 'Finished';
    return joined([transcoder.encodeHardware, pace, transcoder.retriedOnCpu ? 'moved to the CPU after a GPU error' : null]);
}

function deliveryLine(stats: PlaybackStats): string {
    if (stats.delivery === 'File') return 'The file itself';
    const cap = rateText(stats.streamBitrateKbps);
    return cap ? `HLS, up to ${cap}` : 'HLS';
}

export default function PlaybackStatsSections({ sessionId, serverId, reasons, sideloadedSubtitle, readPlayer }: PlaybackStatsSectionsProps) {
    const [stats, setStats] = useState<{ sessionId: string; value: PlaybackStats } | null>(null);
    const [failedFor, setFailedFor] = useState<string | null>(null);
    const [player, setPlayer] = useState<PlayerReading>(() => readPlayer());

    useEffect(() => {
        if (!sessionId) return;
        let current = true;
        const load = () => streamingService.getSessionStats(sessionId, serverId)
            .then(value => {
                if (!current) return;
                setStats({ sessionId, value });
                setFailedFor(null);
            })
            .catch(() => { if (current) setFailedFor(sessionId); });
        void load();
        const timer = window.setInterval(() => { void load(); }, SERVER_REFRESH_MS);
        return () => {
            current = false;
            window.clearInterval(timer);
        };
    }, [sessionId, serverId]);

    useEffect(() => {
        const timer = window.setInterval(() => setPlayer(readPlayer()), PLAYER_REFRESH_MS);
        return () => window.clearInterval(timer);
    }, [readPlayer]);

    const server = stats && stats.sessionId === sessionId ? stats.value : null;
    const failed = !!sessionId && failedFor === sessionId && !server;
    const lines = reasonLines(reasons);
    const video = server?.video;
    const audio = server?.audio;
    const transcoding = !!server?.transcoder;

    return (
        <article aria-label="How this is playing">
            {!server && !failed && <div className="vora-skeleton mb-6 h-40" aria-label="Loading playback info" />}
            {failed && (
                <p className="m-0 mb-6 text-sm" style={{ color: 'var(--vora-text-muted)' }}>
                    The server couldn't say how this is playing. What the player knows is below.
                </p>
            )}

            <div className="grid grid-cols-1 gap-x-10 gap-y-8 sm:grid-cols-2">
                <Section title="Stream">
                    <StatList>
                        {server && <Stat label="Playing as" value={methodLabel(server.method)} />}
                        {server && <Stat label="Source" value={joined([formatCodec(server.sourceContainer), rateText(server.sourceBitrateKbps)]) || null} />}
                        {server && <Stat label="Delivered as" value={deliveryLine(server)} />}
                        {server && <Stat label="Transcoder" value={transcoderLine(server)} />}
                        {transcoding && <Stat label="Decoding" value={server?.transcoder?.decodeHardware ?? 'CPU'} />}
                        <Stat label="Player" value={player.engine} />
                        <Stat label="Bandwidth" value={rateText(player.bandwidthKbps)} />
                        <Stat label="Buffered" value={player.bufferedSeconds != null ? `${Math.round(player.bufferedSeconds)} s ahead` : null} />
                    </StatList>
                    {lines.length > 0 && (
                        <ul className="m-0 mt-4 list-none space-y-1.5 border-t p-0 pt-4 text-sm" style={{ borderColor: 'var(--vora-border-subtle)' }} aria-label="Why it plays this way">
                            {lines.map(line => (
                                <li key={`${line.area}-${line.text}`} style={{ color: 'var(--vora-text-secondary)' }}>
                                    {line.area && <span className="font-medium" style={{ color: 'var(--vora-text-primary)' }}>{line.area}: </span>}
                                    {line.text}
                                </li>
                            ))}
                        </ul>
                    )}
                </Section>

                <Section title="Video">
                    <StatList>
                        {video && <Stat label="Source" value={videoSourceTitle(video) || null} />}
                        {video && <Stat label="Details" value={videoSourceDetail(video) || null} />}
                        {video && <Stat label="Output" value={videoOutput(video)} />}
                        {video && <Stat label="Tone mapping" value={video.toneMapping} />}
                        <Stat
                            label="Dropped frames"
                            value={player.droppedFrames != null && player.totalFrames != null
                                ? `${player.droppedFrames.toLocaleString('en-US')} of ${player.totalFrames.toLocaleString('en-US')}`
                                : null}
                        />
                    </StatList>
                </Section>

                {audio && (
                    <Section title="Audio">
                        <StatList>
                            <Stat label="Source" value={audioSourceTitle(audio) || null} />
                            <Stat label="Details" value={audioSourceDetail(audio) || null} />
                            <Stat label="Output" value={audioOutput(audio)} />
                        </StatList>
                    </Section>
                )}

                {(server?.subtitle || sideloadedSubtitle) && (
                    <Section title="Subtitles">
                        <StatList>
                            {server?.subtitle
                                ? <Stat label="Showing" value={subtitleSummary(server.subtitle)} />
                                : sideloadedSubtitle && (
                                    <Stat
                                        label="Showing"
                                        value={joined([
                                            knownLanguageName(sideloadedSubtitle.language) ?? sideloadedSubtitle.title ?? 'Subtitles',
                                            subtitleCodecName(sideloadedSubtitle.codec),
                                            'Drawn by the player',
                                        ])}
                                    />
                                )}
                        </StatList>
                    </Section>
                )}
            </div>
        </article>
    );
}
