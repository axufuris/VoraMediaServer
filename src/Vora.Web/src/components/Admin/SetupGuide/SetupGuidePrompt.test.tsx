import { describe, it, expect, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import SetupGuidePrompt from './SetupGuidePrompt';
import type { SetupGuideVM } from '../../../api/System/setupGuideService';

const mocks = vi.hoisted(() => ({ get: vi.fn(), save: vi.fn() }));
vi.mock('../../../api/System/setupGuideService', () => ({ setupGuideService: { get: mocks.get, save: mocks.save } }));

const guide = (status: SetupGuideVM['status']): SetupGuideVM => ({ status, step: 'artwork', moviesAndShows: true, music: false, liveTv: false, internetRadio: false, podcasts: false });

const renderAt = (path: string) => render(
    <MemoryRouter initialEntries={[path]}>
        <Routes>
            <Route path="/admin/*" element={<SetupGuidePrompt />} />
        </Routes>
    </MemoryRouter>,
);

const tick = () => new Promise(resolve => setTimeout(resolve, 0));

describe('SetupGuidePrompt', () => {
    beforeEach(() => {
        mocks.get.mockReset();
        mocks.save.mockReset();
        mocks.save.mockImplementation((g: SetupGuideVM) => Promise.resolve(g));
        sessionStorage.clear();
    });

    it('offers to continue an unfinished guide, and Not now waits until the next session', async () => {
        mocks.get.mockResolvedValue(guide('InProgress'));
        const first = renderAt('/admin');

        expect(await screen.findByRole('heading', { name: 'Pick up where you left off?' })).toBeInTheDocument();
        fireEvent.click(screen.getByRole('button', { name: 'Not now' }));
        await waitFor(() => expect(screen.queryByRole('heading', { name: 'Pick up where you left off?' })).not.toBeInTheDocument());
        first.unmount();

        renderAt('/admin/settings');
        await tick();
        expect(mocks.get).toHaveBeenCalledTimes(1);
        expect(mocks.save).not.toHaveBeenCalled();
    });

    it('offers to start a guide that has not been started', async () => {
        mocks.get.mockResolvedValue(guide('NotStarted'));
        renderAt('/admin');

        expect(await screen.findByRole('heading', { name: 'Set up your server' })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Start setup' })).toBeInTheDocument();
    });

    it('skips the guide for good from the pop-up', async () => {
        mocks.get.mockResolvedValue(guide('InProgress'));
        renderAt('/admin');

        fireEvent.click(await screen.findByRole('button', { name: 'Skip setup' }));

        await waitFor(() => expect(mocks.save).toHaveBeenCalledWith(expect.objectContaining({ status: 'Skipped' }), undefined));
    });

    it('stays quiet once the guide is skipped or finished', async () => {
        mocks.get.mockResolvedValue(guide('Skipped'));
        renderAt('/admin');
        await waitFor(() => expect(mocks.get).toHaveBeenCalled());
        await tick();

        expect(screen.queryByRole('heading', { name: /Pick up|Set up your server/ })).not.toBeInTheDocument();
    });

    it('does not pop up over the guide itself', async () => {
        mocks.get.mockResolvedValue(guide('InProgress'));
        renderAt('/admin/setup');
        await tick();

        expect(mocks.get).not.toHaveBeenCalled();
    });

    it('asks the server the admin is managing', async () => {
        mocks.get.mockResolvedValue(guide('Completed'));
        renderAt('/admin/server/abc/libraries');

        await waitFor(() => expect(mocks.get).toHaveBeenCalledWith('abc'));
    });
});
