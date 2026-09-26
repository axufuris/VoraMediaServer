import { describe, it, expect, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { AxiosError, AxiosHeaders } from 'axios';
import AiPlaylistsSection from './AiPlaylistsSection';
import type { AiPlaylistsVM } from '../../../../api/Music/aiPlaylistService';

const mocks = vi.hoisted(() => ({
    get: vi.fn(),
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

const renderSection = (updateNav = vi.fn()) => {
    render(<MemoryRouter><AiPlaylistsSection updateNav={updateNav} /></MemoryRouter>);
    return updateNav;
};

describe('AiPlaylistsSection', () => {
    beforeEach(() => Object.values(mocks).forEach(m => m.mockReset()));

    it('shows nothing when AI playlists are off or the profile opted out', async () => {
        mocks.get.mockResolvedValue(data({ enabled: false }));
        renderSection();

        await waitFor(() => expect(mocks.get).toHaveBeenCalled());
        expect(screen.queryByText('Made for you by AI')).toBeNull();
        expect(screen.queryByRole('button', { name: 'Blend' })).toBeNull();
    });

    it('shows the weekly playlists, the Bridge and Blends, each labelled, and opens one', async () => {
        mocks.get.mockResolvedValue(data());
        const updateNav = renderSection();

        expect(await screen.findByText('Late Night Drive')).toBeInTheDocument();
        expect(screen.getByText('Moodier stuff after 10pm.')).toBeInTheDocument();
        expect(screen.getByText('Bridge')).toBeInTheDocument();
        expect(screen.getByText('With Sam')).toBeInTheDocument();

        fireEvent.click(screen.getByText('Late Night Drive'));
        expect(updateNav).toHaveBeenCalledWith({ view: 'mix', mixId: 'w1' });
    });

    it('hides the request button when the admin switched requests off', async () => {
        mocks.get.mockResolvedValue(data({ requestsEnabled: false }));
        renderSection();

        await screen.findByText('Late Night Drive');
        expect(screen.queryByRole('button', { name: 'Make me a playlist' })).toBeNull();
        expect(screen.getByRole('button', { name: 'Blend' })).toBeInTheDocument();
    });

    it('makes a playlist from a request and opens it', async () => {
        mocks.get.mockResolvedValue(data());
        mocks.make.mockResolvedValue({ mixId: 'r1' });
        const updateNav = renderSection();

        fireEvent.click(await screen.findByRole('button', { name: 'Make me a playlist' }));
        fireEvent.change(screen.getByLabelText('What the playlist is for'), { target: { value: 'road trip' } });
        fireEvent.click(screen.getByRole('button', { name: 'Make it' }));

        await waitFor(() => expect(updateNav).toHaveBeenCalledWith({ view: 'mix', mixId: 'r1' }));
        expect(mocks.make).toHaveBeenCalledWith('road trip', undefined);
    });

    it("shows the server's reason when the daily limit is reached", async () => {
        mocks.get.mockResolvedValue(data());
        mocks.make.mockRejectedValue(new AxiosError('limit', '429', undefined, undefined, {
            status: 429, statusText: 'Too Many Requests', headers: {}, config: { headers: new AxiosHeaders() },
            data: { title: 'Too Many Requests', detail: "You've made 10 playlists in the last day, the most this server allows. Try again later." },
        }));
        renderSection();

        fireEvent.click(await screen.findByRole('button', { name: 'Make me a playlist' }));
        fireEvent.click(screen.getByRole('button', { name: /Cooking dinner/ }));
        fireEvent.click(screen.getByRole('button', { name: 'Make it' }));

        expect(await screen.findByRole('alert')).toHaveTextContent('the most this server allows');
    });

    it('blends with a chosen profile and opens the Blend', async () => {
        mocks.get.mockResolvedValue(data());
        mocks.getBlendPartners.mockResolvedValue([{ profileId: 'sam', name: 'Sam', imageUrl: null }]);
        mocks.blend.mockResolvedValue({ mixId: 'x2' });
        const updateNav = renderSection();

        fireEvent.click(await screen.findByRole('button', { name: 'Blend' }));
        fireEvent.click(await screen.findByRole('button', { name: 'Sam' }));

        await waitFor(() => expect(updateNav).toHaveBeenCalledWith({ view: 'mix', mixId: 'x2' }));
        expect(mocks.blend).toHaveBeenCalledWith('sam', undefined);
    });

    it('says when there is no one to Blend with', async () => {
        mocks.get.mockResolvedValue(data());
        mocks.getBlendPartners.mockResolvedValue([]);
        renderSection();

        fireEvent.click(await screen.findByRole('button', { name: 'Blend' }));

        expect(await screen.findByText(/No one else on this server uses AI playlists yet/)).toBeInTheDocument();
    });
});
