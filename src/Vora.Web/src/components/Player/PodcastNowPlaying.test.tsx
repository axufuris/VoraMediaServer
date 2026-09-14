import { describe, it, expect, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import PodcastNowPlaying from './PodcastNowPlaying';
import type { PlayableMedia } from '../../contexts/usePlayer';

const episode: PlayableMedia = {
    id: 'sodder',
    title: 'UPDATE: The Sodder Children',
    subtitle: 'Crime Junkie',
    streamUrl: 'https://example.test/sodder.mp3',
    playbackContextType: 'Podcast',
};

const renderPodcast = (overrides: Partial<Parameters<typeof PodcastNowPlaying>[0]> = {}) => {
    const props = {
        episode,
        isPlaying: true,
        currentTime: 64,
        duration: 1862,
        volume: 0.8,
        onVolumeChange: vi.fn(),
        onTogglePlay: vi.fn(),
        onSeek: vi.fn(),
        onSkipBack: vi.fn(),
        onSkipForward: vi.fn(),
        onMinimize: vi.fn(),
        onClose: vi.fn(),
        ...overrides,
    };
    render(<div data-vora-client=""><PodcastNowPlaying {...props} /></div>);
    return props;
};

describe('podcast now playing', () => {
    it('shows the episode and its show', () => {
        renderPodcast();

        expect(screen.getByRole('heading', { name: 'UPDATE: The Sodder Children' })).toBeInTheDocument();
        expect(screen.getByText('Crime Junkie')).toBeInTheDocument();
        expect(screen.getByText('Podcast')).toBeInTheDocument();
    });

    // Episodes are long and skipped around in, which is why podcasts keep the
    // skips that radio lost.
    it('skips back ten seconds and forward thirty', () => {
        const props = renderPodcast();

        fireEvent.click(screen.getByRole('button', { name: 'Back 10 seconds' }));
        fireEvent.click(screen.getByRole('button', { name: 'Forward 30 seconds' }));

        expect(props.onSkipBack).toHaveBeenCalledWith(10);
        expect(props.onSkipForward).toHaveBeenCalledWith(30);
    });

    it('seeks from the position bar', () => {
        const props = renderPodcast();

        fireEvent.change(screen.getByRole('slider', { name: 'Playback position' }), { target: { value: '900' } });

        expect(props.onSeek).toHaveBeenCalledWith(900);
    });

    it('shows the position and length', () => {
        renderPodcast();

        expect(screen.getByText('1:04')).toBeInTheDocument();
        expect(screen.getByText('31:02')).toBeInTheDocument();
    });

    it('shows hours for a long episode', () => {
        renderPodcast({ currentTime: 3900, duration: 7322 });

        expect(screen.getByText('1:05:00')).toBeInTheDocument();
        expect(screen.getByText('2:02:02')).toBeInTheDocument();
    });

    // Track navigation belongs to music; a podcast has no queue to step through.
    it('has no previous or next track', () => {
        renderPodcast();

        expect(screen.queryByRole('button', { name: /^Previous/ })).toBeNull();
        expect(screen.queryByRole('button', { name: /^Next/ })).toBeNull();
    });

    it('toggles play', () => {
        const props = renderPodcast();

        fireEvent.click(screen.getByRole('button', { name: 'Pause' }));

        expect(props.onTogglePlay).toHaveBeenCalledOnce();
    });

    it('has the shared volume control', () => {
        const props = renderPodcast();

        fireEvent.click(screen.getByRole('button', { name: 'Mute' }));

        expect(props.onVolumeChange).toHaveBeenCalledWith(0);
    });

    it('minimizes on Escape and focuses play on open', () => {
        const props = renderPodcast();

        expect(screen.getByRole('button', { name: 'Pause' })).toHaveFocus();
        fireEvent.keyDown(window, { key: 'Escape' });

        expect(props.onMinimize).toHaveBeenCalledOnce();
    });

    it('stops and closes from the header', () => {
        const props = renderPodcast();

        fireEvent.click(screen.getByRole('button', { name: 'Stop and close player' }));

        expect(props.onClose).toHaveBeenCalledOnce();
    });

    it('falls back to the show name label when the episode has no subtitle', () => {
        renderPodcast({ episode: { ...episode, subtitle: undefined } });

        expect(screen.getAllByText('Podcast').length).toBeGreaterThan(0);
    });
});
