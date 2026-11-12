import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import RadioTab from './RadioTab';
import type { IptvChannelVM } from '../../../api/Iptv/iptvAdminService';
import { LONG_PRESS_MS } from '../../../hooks/useMenuTrigger';

const mocks = vi.hoisted(() => ({
    getPlaylists: vi.fn(),
    getRadioPrefs: vi.fn(),
    saveRadioPrefs: vi.fn(),
    playMedia: vi.fn(),
}));

vi.mock('../../../api/Iptv/iptvClientService', () => ({
    iptvClientService: { getPlaylists: mocks.getPlaylists },
}));
vi.mock('../../../api/Users/profileDeviceSettingsService', () => ({
    profileDeviceSettingsService: { getRadioPrefs: mocks.getRadioPrefs, saveRadioPrefs: mocks.saveRadioPrefs },
}));
vi.mock('../../../contexts/usePlayer', () => ({ usePlayer: () => ({ playMedia: mocks.playMedia }) }));
vi.mock('../../../hooks/useSignalREvent', () => ({ useSignalREvent: () => { } }));

const station = (id: string, name: string, extra: Partial<IptvChannelVM> = {}): IptvChannelVM => ({
    id,
    name,
    playlistId: 'radio',
    externalChannelId: `ext-${id}`,
    streamUrl: `https://streams.example.com/${id}`,
    groupTitle: 'News',
    countryCode: 'GB',
    isHiddenByAdmin: false,
    kind: 'Radio',
    ...extra,
});

const bbc = station('bbc', 'BBC Radio 4', { logoUrl: 'https://logos.example.com/bbc.png' });
const jazz = station('jazz', 'Jazz FM', { logoUrl: 'https://logos.example.com/jazz.png', groupTitle: 'Jazz' });
const kexp = station('kexp', 'KEXP', { countryCode: 'US', groupTitle: 'Indie' });
const gone = station('gone', 'Gone FM', { countryCode: 'Unknown', groupTitle: 'Indie' });

const profileToken = `x.${btoa(JSON.stringify({ sub: 'profile-1' }))}.y`;

const tileFor = (name: string) => {
    const tile = screen.getByText(name).closest('.vora-media-card');
    if (!(tile instanceof HTMLElement)) throw new Error(`No tile for ${name}`);
    return tile;
};

const renderTab = async () => {
    render(<MemoryRouter><RadioTab /></MemoryRouter>);
    await screen.findByText('BBC Radio 4');
};

beforeEach(() => {
    localStorage.clear();
    localStorage.setItem('profile_token', profileToken);
    localStorage.setItem('user_id', 'user-1');
    localStorage.setItem('device_id', 'device-1');
    mocks.getPlaylists.mockResolvedValue([{ id: 'radio', name: 'Radio', channels: [bbc, jazz, kexp, gone] }]);
    mocks.getRadioPrefs.mockResolvedValue(JSON.stringify({ favoriteIds: ['jazz'], hiddenIds: ['gone'], countryFilter: 'All' }));
    mocks.saveRadioPrefs.mockResolvedValue(undefined);
});

afterEach(() => {
    cleanup();
    vi.useRealTimers();
    vi.clearAllMocks();
});

describe('RadioTab', () => {
    it('draws every station as a square media tile with its logo contained, and no card box', async () => {
        await renderTab();

        const tile = tileFor('BBC Radio 4');
        expect(tile).toHaveAttribute('role', 'button');
        expect(tile).toHaveAttribute('tabindex', '0');
        expect(tile.closest('.vora-card')).toBeNull();

        const art = tile.querySelector('.vora-media-card-art');
        expect(art).toHaveStyle({ aspectRatio: '1 / 1' });

        const logo = screen.getByRole('img', { name: 'BBC Radio 4' });
        expect(logo.tagName).toBe('IMG');
        expect(logo).toHaveAttribute('src', 'https://logos.example.com/bbc.png');
        expect(logo).toHaveClass('object-contain');
        expect(art).toContainElement(logo);
    });

    it('captions a tile with the station name and then its country', async () => {
        await renderTab();

        const tile = tileFor('BBC Radio 4');
        expect(tile).toHaveTextContent('BBC Radio 4GB');
        expect(tileFor('KEXP')).toHaveTextContent('KEXPUS');
    });

    it('draws a station without a logo as the placeholder with the radio glyph', async () => {
        await renderTab();

        const placeholder = screen.getByRole('img', { name: 'KEXP' });
        expect(placeholder.tagName).toBe('DIV');
        expect(placeholder.querySelector('svg circle')).not.toBeNull();
    });

    it('marks a favourite with a star inside the artwork, and lists it under Favorites', async () => {
        await renderTab();

        const favoriteTile = tileFor('Jazz FM');
        const badge = screen.getByRole('img', { name: 'Favorite' });
        expect(favoriteTile.querySelector('.vora-media-card-art')).toContainElement(badge);
        expect(screen.getAllByRole('img', { name: 'Favorite' })).toHaveLength(1);
        expect(screen.getByText('★ Favorites').closest('section')).toContainElement(favoriteTile);
    });

    it('leaves hidden stations out until they are shown, then dims the whole tile', async () => {
        await renderTab();
        expect(screen.queryByText('Gone FM')).not.toBeInTheDocument();

        fireEvent.click(screen.getByLabelText('Show 1 hidden station'));

        const hiddenTile = tileFor('Gone FM');
        expect(hiddenTile).toHaveClass('opacity-45');
        expect(hiddenTile).toHaveTextContent(/^Gone FM$/);
        expect(tileFor('KEXP')).not.toHaveClass('opacity-45');
    });

    it('plays a station when its tile is clicked or activated from the keyboard', async () => {
        await renderTab();

        fireEvent.click(tileFor('BBC Radio 4'));
        expect(mocks.playMedia).toHaveBeenCalledWith(expect.objectContaining({
            id: 'bbc',
            title: 'BBC Radio 4',
            streamUrl: 'https://streams.example.com/bbc',
            playbackContextType: 'LiveRadio',
        }));

        fireEvent.keyDown(tileFor('KEXP'), { key: 'Enter' });
        expect(mocks.playMedia).toHaveBeenLastCalledWith(expect.objectContaining({ id: 'kexp' }));
    });

    it('opens the favourite and hide menu on right-click, and acts on the station', async () => {
        await renderTab();

        fireEvent.contextMenu(tileFor('BBC Radio 4'), { clientX: 120, clientY: 80 });
        fireEvent.click(screen.getByRole('button', { name: 'Add to favorites' }));

        await waitFor(() => expect(screen.getAllByRole('img', { name: 'Favorite' })).toHaveLength(2));
        expect(screen.getByText('★ Favorites').closest('section')).toContainElement(tileFor('BBC Radio 4'));
        expect(mocks.playMedia).not.toHaveBeenCalled();

        fireEvent.contextMenu(tileFor('Jazz FM'), { clientX: 120, clientY: 80 });
        expect(screen.getByRole('button', { name: 'Remove favorite' })).toBeInTheDocument();
        fireEvent.click(screen.getByRole('button', { name: 'Hide station' }));

        await waitFor(() => expect(screen.queryByText('Jazz FM')).not.toBeInTheDocument());
    });

    it('opens the menu after a finger is held on a tile, without playing the station', async () => {
        await renderTab();
        vi.useFakeTimers();

        const tile = tileFor('KEXP');
        fireEvent.pointerDown(tile, { pointerType: 'touch', clientX: 40, clientY: 60 });
        act(() => { vi.advanceTimersByTime(LONG_PRESS_MS); });
        fireEvent.pointerUp(tile, { pointerType: 'touch' });
        fireEvent.click(tile);

        expect(screen.getByRole('button', { name: 'Add to favorites' })).toBeInTheDocument();
        expect(mocks.playMedia).not.toHaveBeenCalled();
    });
});
