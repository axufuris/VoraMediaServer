import { describe, it, expect, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { AxiosError, AxiosHeaders } from 'axios';
import AiPlaylistsSection, { AiPlaylistActions } from './AiPlaylistsSection';
import type { AiPlaylistsVM } from '../../../../api/Music/aiPlaylistService';

const mocks = vi.hoisted(() => ({
    make: vi.fn(),
    getBlendPartners: vi.fn(),
    blend: vi.fn(),
}));

vi.mock('../../../../api/Music/aiPlaylistService', () => ({ aiPlaylistService: mocks }));

const data = (overrides: Partial<AiPlaylistsVM> = {}): AiPlaylistsVM => ({
    enabled: true,
    requestsEnabled: true,
    weekly: [
        { id: 'w1', kind: 'AiPlaylist', name: 'Late Night Drive', description: 'Moodier stuff after 10pm.', trackCount: 25, generatedAt: '2026-09-26' },
        { id: 'b1', kind: 'Bridge', name: 'Punk to Country', description: 'From one favourite to another.', trackCount: 20, generatedAt: '2026-09-26' },
    ],
    blends: [{ id: 'x1', kind: 'Blend', name: 'Andy + Sam', partnerName: 'Sam', trackCount: 30, generatedAt: '2026-09-26' }],
    requests: [],
    ...overrides,
});

const renderSection = (value: AiPlaylistsVM, updateNav = vi.fn()) => {
    render(<AiPlaylistsSection data={value} updateNav={updateNav} />);
    return updateNav;
};

const renderActions = (value: AiPlaylistsVM | null, onMade = vi.fn()) => {
    render(<AiPlaylistActions data={value} onMade={onMade} />);
    return onMade;
};

describe('AiPlaylistsSection', () => {
    beforeEach(() => Object.values(mocks).forEach(m => m.mockReset()));

    it('shows nothing when AI playlists are off or the profile opted out', () => {
        renderSection(data({ enabled: false }));
        renderActions(data({ enabled: false }));

        expect(screen.queryByText('Made for you by AI')).toBeNull();
        expect(screen.queryByRole('button', { name: 'Blend' })).toBeNull();
        expect(screen.queryByRole('button', { name: 'Make me a playlist' })).toBeNull();
    });

    it('shows no placeholder before there are any AI playlists', () => {
        const { container } = render(<AiPlaylistsSection data={data({ weekly: [], blends: [] })} updateNav={vi.fn()} />);

        expect(container).toBeEmptyDOMElement();
    });

    it('shows the weekly playlists, the Bridge and Blends, each labelled, and opens one', () => {
        const updateNav = renderSection(data());

        expect(screen.getByText('Late Night Drive')).toBeInTheDocument();
        expect(screen.getByText('Moodier stuff after 10pm.')).toBeInTheDocument();
        expect(screen.getByText('Bridge')).toBeInTheDocument();
        expect(screen.getByText('With Sam')).toBeInTheDocument();

        fireEvent.click(screen.getByText('Late Night Drive'));
        expect(updateNav).toHaveBeenCalledWith({ view: 'mix', mixId: 'w1' });
    });

    it('hides the request button when the admin switched requests off', () => {
        renderActions(data({ requestsEnabled: false }));

        expect(screen.queryByRole('button', { name: 'Make me a playlist' })).toBeNull();
        expect(screen.getByRole('button', { name: 'Blend' })).toBeInTheDocument();
    });

    it('makes a playlist from a request and opens it', async () => {
        mocks.make.mockResolvedValue({ mixId: 'r1' });
        const onMade = renderActions(data());

        fireEvent.click(screen.getByRole('button', { name: 'Make me a playlist' }));
        fireEvent.change(screen.getByLabelText('What the playlist is for'), { target: { value: 'road trip' } });
        fireEvent.click(screen.getByRole('button', { name: 'Make it' }));

        await waitFor(() => expect(onMade).toHaveBeenCalledWith('r1'));
        expect(mocks.make).toHaveBeenCalledWith('road trip', null, undefined);
    });

    it('sends the chosen length, and none when the AI is left to decide', async () => {
        mocks.make.mockResolvedValue({ mixId: 'r2' });
        const onMade = renderActions(data());

        fireEvent.click(screen.getByRole('button', { name: 'Make me a playlist' }));
        expect(screen.getByRole('radio', { name: 'Let AI decide' })).toHaveAttribute('aria-checked', 'true');
        fireEvent.change(screen.getByLabelText('What the playlist is for'), { target: { value: 'long drive' } });
        fireEvent.click(screen.getByRole('radio', { name: '45 songs' }));
        fireEvent.click(screen.getByRole('button', { name: 'Make it' }));

        await waitFor(() => expect(onMade).toHaveBeenCalledWith('r2'));
        expect(mocks.make).toHaveBeenCalledWith('long drive', 45, undefined);
    });

    it("shows the server's reason when the daily limit is reached", async () => {
        mocks.make.mockRejectedValue(new AxiosError('limit', '429', undefined, undefined, {
            status: 429, statusText: 'Too Many Requests', headers: {}, config: { headers: new AxiosHeaders() },
            data: { title: 'Too Many Requests', detail: "You've made 10 playlists in the last day, the most this server allows. Try again later." },
        }));
        renderActions(data());

        fireEvent.click(screen.getByRole('button', { name: 'Make me a playlist' }));
        fireEvent.click(screen.getByRole('button', { name: /Cooking dinner/ }));
        fireEvent.click(screen.getByRole('button', { name: 'Make it' }));

        expect(await screen.findByRole('alert')).toHaveTextContent('the most this server allows');
    });

    it('blends with a chosen profile and opens the Blend', async () => {
        mocks.getBlendPartners.mockResolvedValue([{ profileId: 'sam', name: 'Sam', imageUrl: null }]);
        mocks.blend.mockResolvedValue({ mixId: 'x2' });
        const onMade = renderActions(data());

        fireEvent.click(screen.getByRole('button', { name: 'Blend' }));
        fireEvent.click(await screen.findByRole('button', { name: 'Sam' }));

        await waitFor(() => expect(onMade).toHaveBeenCalledWith('x2'));
        expect(mocks.blend).toHaveBeenCalledWith('sam', undefined);
    });

    it('says when there is no one to Blend with', async () => {
        mocks.getBlendPartners.mockResolvedValue([]);
        renderActions(data());

        fireEvent.click(screen.getByRole('button', { name: 'Blend' }));

        expect(await screen.findByText(/No one else on this server uses AI playlists yet/)).toBeInTheDocument();
    });
});
