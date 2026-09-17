import { describe, it, expect, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import RadioNowPlaying from './RadioNowPlaying';
import type { IptvChannelVM } from '../../api/Iptv/iptvAdminService';
import type { PlayableMedia } from '../../contexts/usePlayer';

const channel = (id: string, name: string): IptvChannelVM => ({
    id,
    playlistId: 'p1',
    externalChannelId: id,
    name,
    groupTitle: '1930',
    streamUrl: `https://example.test/${id}`,
    isHiddenByAdmin: false,
    kind: 'Radio',
});

const stations = [channel('walm', 'Classic Vinyl HD'), channel('kexp', 'KEXP'), channel('wfmu', 'WFMU')];

const station: PlayableMedia = {
    id: 'walm',
    title: 'Classic Vinyl HD',
    subtitle: '1930',
    streamUrl: 'https://example.test/walm',
    playbackContextType: 'LiveRadio',
};

const renderRadio = (overrides: Partial<Parameters<typeof RadioNowPlaying>[0]> = {}) => {
    const props = {
        station,
        stations,
        isPlaying: true,
        isLoading: false,
        streamError: null,
        volume: 0.8,
        onVolumeChange: vi.fn(),
        onTogglePlay: vi.fn(),
        onPreviousStation: vi.fn(),
        onNextStation: vi.fn(),
        onSelectStation: vi.fn(),
        onMinimize: vi.fn(),
        onClose: vi.fn(),
        ...overrides,
    };
    render(<div data-vora-client=""><RadioNowPlaying {...props} /></div>);
    return props;
};

describe('radio now playing', () => {
    // A live stream has no position to move within.
    it('has no skip back or skip forward', () => {
        renderRadio();

        expect(screen.queryByRole('button', { name: /10/ })).toBeNull();
        expect(screen.queryByRole('button', { name: /30/ })).toBeNull();
        expect(screen.queryByRole('slider', { name: 'Playback position' })).toBeNull();
    });

    it('changes station with previous and next', () => {
        const props = renderRadio();

        fireEvent.click(screen.getByRole('button', { name: 'Next station' }));
        fireEvent.click(screen.getByRole('button', { name: 'Previous station' }));

        expect(props.onNextStation).toHaveBeenCalledOnce();
        expect(props.onPreviousStation).toHaveBeenCalledOnce();
    });

    it('cannot change station when there is only one', () => {
        renderRadio({ stations: [stations[0]] });

        expect(screen.getByRole('button', { name: 'Next station' })).toBeDisabled();
        expect(screen.getByRole('button', { name: 'Previous station' })).toBeDisabled();
    });

    it('shows the station and that it is live', () => {
        renderRadio();

        expect(screen.getByRole('heading', { name: 'Classic Vinyl HD' })).toBeInTheDocument();
        expect(screen.getByText('On air')).toBeInTheDocument();
        expect(screen.getByText('Live Radio')).toBeInTheDocument();
    });

    it('says it is tuning in while the stream loads', () => {
        renderRadio({ isLoading: true });

        expect(screen.getByText('Tuning in…')).toBeInTheDocument();
        expect(screen.queryByText('On air')).toBeNull();
    });

    it('shows a stream error instead of claiming to be on air', () => {
        renderRadio({ streamError: 'This station could not be played.' });

        expect(screen.getByRole('alert')).toHaveTextContent('This station could not be played.');
        expect(screen.queryByText('On air')).toBeNull();
    });

    it('opens the station list and plays the one picked', () => {
        const props = renderRadio();

        const toggle = screen.getByRole('button', { name: 'Stations' });
        expect(toggle).toHaveAttribute('aria-pressed', 'false');
        fireEvent.click(toggle);
        expect(toggle).toHaveAttribute('aria-pressed', 'true');

        fireEvent.click(screen.getByRole('button', { name: /KEXP/ }));

        expect(props.onSelectStation).toHaveBeenCalledWith(stations[1]);
    });

    it('marks the current station in the list', () => {
        renderRadio();
        fireEvent.click(screen.getByRole('button', { name: 'Stations' }));

        expect(screen.getByRole('button', { name: /Classic Vinyl HD/ })).toHaveAttribute('aria-current', 'true');
        expect(screen.getByRole('button', { name: /WFMU/ })).toHaveAttribute('aria-current', 'false');
    });

    it('hides the station list toggle when there are no stations to list', () => {
        renderRadio({ stations: [] });

        expect(screen.queryByRole('button', { name: 'Stations' })).toBeNull();
    });

    it('minimizes on Escape', () => {
        const props = renderRadio();

        fireEvent.keyDown(window, { key: 'Escape' });

        expect(props.onMinimize).toHaveBeenCalledOnce();
    });

    it('focuses play on open so a remote has somewhere to start', () => {
        renderRadio();

        expect(screen.getByRole('button', { name: 'Pause' })).toHaveFocus();
    });

    it('stops and closes from the header', () => {
        const props = renderRadio();

        fireEvent.click(screen.getByRole('button', { name: 'Stop and close player' }));

        expect(props.onClose).toHaveBeenCalledOnce();
    });
});
