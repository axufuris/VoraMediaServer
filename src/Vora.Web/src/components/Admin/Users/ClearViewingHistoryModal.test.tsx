import { describe, it, expect, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { AxiosError, AxiosHeaders } from 'axios';
import ClearViewingHistoryModal from './ClearViewingHistoryModal';
import { type UserProfileVM } from '../../../api/Users/profileService';

const mocks = vi.hoisted(() => ({
    adminGetViewingHistory: vi.fn(),
    adminClearViewingHistory: vi.fn(),
}));

vi.mock('../../../api/Users/profileService', () => ({
    profileService: { adminGetViewingHistory: mocks.adminGetViewingHistory, adminClearViewingHistory: mocks.adminClearViewingHistory },
}));

const sam = { id: 'p-sam', name: 'Sam' } as UserProfileVM;

const renderModal = (onClose = vi.fn()) => render(<ClearViewingHistoryModal profile={sam} serverId="s1" onClose={onClose} />);

describe('ClearViewingHistoryModal', () => {
    beforeEach(() => {
        Object.values(mocks).forEach(m => m.mockReset());
        mocks.adminGetViewingHistory.mockResolvedValue({ watchedCount: 13293, inProgressCount: 37, ratingCount: 759 });
    });

    it("shows what the profile has, and clears both the watch history and the ratings", async () => {
        mocks.adminClearViewingHistory.mockResolvedValue({ watchStatesRemoved: 13340, ratingsRemoved: 759 });
        renderModal();

        expect(await screen.findByText(/13,293 titles watched, 37 in progress/)).toBeInTheDocument();
        expect(screen.getByText('759 ratings.')).toBeInTheDocument();
        fireEvent.click(screen.getByRole('button', { name: 'Clear history' }));

        expect(await screen.findByRole('status')).toHaveTextContent('Removed the watch history of 13,340 titles and 759 ratings from Sam.');
        expect(mocks.adminGetViewingHistory).toHaveBeenCalledWith('p-sam', 's1');
        expect(mocks.adminClearViewingHistory).toHaveBeenCalledWith('p-sam', { watchHistory: true, ratings: true }, 's1');
    });

    it('keeps the ratings when they are unticked', async () => {
        mocks.adminClearViewingHistory.mockResolvedValue({ watchStatesRemoved: 13340, ratingsRemoved: 0 });
        renderModal();

        fireEvent.click(await screen.findByRole('checkbox', { name: /Ratings/ }));
        fireEvent.click(screen.getByRole('button', { name: 'Clear history' }));

        expect(await screen.findByRole('status')).toHaveTextContent('Removed the watch history of 13,340 titles from Sam.');
        expect(mocks.adminClearViewingHistory).toHaveBeenCalledWith('p-sam', { watchHistory: true, ratings: false }, 's1');
    });

    it('will not clear with nothing ticked', async () => {
        renderModal();

        fireEvent.click(await screen.findByRole('checkbox', { name: /Watch history/ }));
        fireEvent.click(screen.getByRole('checkbox', { name: /Ratings/ }));

        expect(screen.getByRole('button', { name: 'Clear history' })).toBeDisabled();
    });

    it('says there is nothing to clear on a profile that has watched and rated nothing', async () => {
        mocks.adminGetViewingHistory.mockResolvedValue({ watchedCount: 0, inProgressCount: 0, ratingCount: 0 });
        renderModal();

        expect(await screen.findByText('This profile has no watch history or ratings to clear.')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Clear history' })).toBeDisabled();
    });

    it("shows the server's reason when clearing fails", async () => {
        mocks.adminClearViewingHistory.mockRejectedValue(new AxiosError('bad', '400', undefined, undefined, {
            status: 400, statusText: 'Bad Request', headers: {}, config: { headers: new AxiosHeaders() },
            data: { title: 'Bad Request', detail: 'Pick the watch history, the ratings or both.' },
        }));
        renderModal();

        fireEvent.click(await screen.findByRole('button', { name: 'Clear history' }));

        expect(await screen.findByRole('alert')).toHaveTextContent('Pick the watch history, the ratings or both.');
    });
});
