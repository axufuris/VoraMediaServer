import { useRef } from 'react';
import type { PlayableMedia } from '../../contexts/usePlayer';
import { NowPlayingShell } from './NowPlaying/NowPlayingShell';
import { NowPlayingArtwork } from './NowPlaying/NowPlayingArtwork';
import { NowPlayingControlRow, NowPlayingPlayButton, NowPlayingSeekBar, NowPlayingSkipButton, NowPlayingVolume } from './NowPlaying/NowPlayingControls';

// A podcast episode's full screen, built from the same pieces as the music and
// radio screens. Episodes are long and listened to out of order, so the
// transport is skip back / play / skip forward around a seek bar, rather than
// previous and next.
interface PodcastNowPlayingProps {
    episode: PlayableMedia;
    isPlaying: boolean;
    currentTime: number;
    duration: number;
    volume: number;
    onVolumeChange: (value: number) => void;
    onTogglePlay: () => void;
    onSeek: (seconds: number) => void;
    onSkipBack: (seconds: number) => void;
    onSkipForward: (seconds: number) => void;
    onMinimize: () => void;
    onClose: () => void;
}

const SkipBackSeconds = 10;
const SkipForwardSeconds = 30;

const podcastIcon = <svg width="96" height="96" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M12 14a3 3 0 003-3V5a3 3 0 00-6 0v6a3 3 0 003 3zm5-3a5 5 0 01-10 0H5a7 7 0 006 6.92V21h2v-3.08A7 7 0 0019 11h-2z" /></svg>;

export default function PodcastNowPlaying({
    episode,
    isPlaying,
    currentTime,
    duration,
    volume,
    onVolumeChange,
    onTogglePlay,
    onSeek,
    onSkipBack,
    onSkipForward,
    onMinimize,
    onClose,
}: PodcastNowPlayingProps) {
    const playButtonRef = useRef<HTMLButtonElement>(null);

    return (
        <NowPlayingShell
            artworkKey={episode.id}
            posterUrl={episode.posterUrl}
            label="Podcast"
            onMinimize={onMinimize}
            onClose={onClose}
            initialFocusRef={playButtonRef}
            controls={
                <>
                    <NowPlayingSeekBar currentTime={currentTime} duration={duration} onSeek={onSeek} />
                    <NowPlayingControlRow
                        transport={
                            <>
                                <NowPlayingSkipButton seconds={SkipBackSeconds} direction="back" onClick={() => onSkipBack(SkipBackSeconds)} />
                                <NowPlayingPlayButton isPlaying={isPlaying} onClick={onTogglePlay} buttonRef={playButtonRef} />
                                <NowPlayingSkipButton seconds={SkipForwardSeconds} direction="forward" onClick={() => onSkipForward(SkipForwardSeconds)} />
                            </>
                        }
                        actions={<NowPlayingVolume value={volume} onChange={onVolumeChange} />}
                    />
                </>
            }
        >
            <div className="flex min-h-0 min-w-0 flex-1 flex-col items-center px-8 pb-4">
                <NowPlayingArtwork
                    artworkKey={episode.id}
                    posterUrl={episode.posterUrl}
                    title={episode.title}
                    subtitle={episode.subtitle || 'Podcast'}
                    fallbackIcon={podcastIcon}
                />
            </div>
        </NowPlayingShell>
    );
}
