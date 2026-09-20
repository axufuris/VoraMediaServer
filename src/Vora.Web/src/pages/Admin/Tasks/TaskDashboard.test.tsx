import { describe, it, expect, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import TaskDashboard from './TaskDashboard';
import type { BackgroundTaskPage } from '../../../api/System/taskService';

const getTasks = vi.fn<(skip: number, take: number, serverId?: string) => Promise<BackgroundTaskPage>>();

vi.mock('../../../api/System/taskService', () => ({
    taskService: {
        getTasks: (skip: number, take: number, serverId?: string) => getTasks(skip, take, serverId),
        cancelTask: () => Promise.resolve(),
    },
}));

vi.mock('../../../hooks/useSignalREvent', () => ({ useSignalREvent: () => { } }));

const page = (skip: number, take: number, total: number, running = 0): BackgroundTaskPage => ({
    items: Array.from({ length: Math.max(0, Math.min(take, total - skip)) }, (_, i) => ({
        id: `task-${skip + i}`,
        name: `Scan File: Episode ${skip + i}`,
        status: running > 0 && skip + i === 0 ? 'Running' : 'Pending',
        progress: null,
    })),
    total,
    running,
    skip,
    take,
});

const renderDashboard = () => render(
    <MemoryRouter>
        <TaskDashboard />
    </MemoryRouter>,
);

describe('TaskDashboard', () => {
    beforeEach(() => {
        getTasks.mockReset();
        vi.useRealTimers();
    });

    it('asks for one page rather than the whole queue', async () => {
        getTasks.mockResolvedValue(page(0, 25, 4213, 2));

        renderDashboard();

        await waitFor(() => expect(getTasks).toHaveBeenCalledWith(0, 25, undefined));
        expect(await screen.findByText('2 running · 4213 total')).toBeInTheDocument();
        expect(screen.getAllByRole('button', { name: 'Cancel' })).toHaveLength(25);
    });

    it('walks to the next page from where the last one ended', async () => {
        getTasks.mockResolvedValue(page(0, 25, 4213));
        renderDashboard();
        await screen.findByText('Showing 1–25 of 4213');

        getTasks.mockResolvedValue(page(25, 25, 4213));
        fireEvent.click(screen.getByRole('button', { name: 'Next' }));

        await waitFor(() => expect(getTasks).toHaveBeenLastCalledWith(25, 25, undefined));
        expect(await screen.findByText('Showing 26–50 of 4213')).toBeInTheDocument();
        expect(screen.getByText('Page 2 of 169')).toBeInTheDocument();
    });

    it('cannot go back from the first page', async () => {
        getTasks.mockResolvedValue(page(0, 25, 4213));
        renderDashboard();

        expect(await screen.findByRole('button', { name: 'Previous' })).toBeDisabled();
    });

    it('hides the pager when everything fits on one page', async () => {
        getTasks.mockResolvedValue(page(0, 25, 3));
        renderDashboard();

        expect(await screen.findByText('0 running · 3 total')).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: 'Next' })).toBeNull();
    });

    it('says so when the queue is empty', async () => {
        getTasks.mockResolvedValue(page(0, 25, 0));
        renderDashboard();

        expect(await screen.findByText('All caught up')).toBeInTheDocument();
    });

    it('follows the queue back when it drains past the current page', async () => {
        getTasks.mockResolvedValue(page(0, 25, 100));
        renderDashboard();
        await screen.findByText('Page 1 of 4');

        fireEvent.click(screen.getByRole('button', { name: 'Next' }));
        await screen.findByText('Page 2 of 4');
        fireEvent.click(screen.getByRole('button', { name: 'Next' }));
        await screen.findByText('Page 3 of 4');

        // The scan drains most of the queue while the page is open. The next
        // refresh brings back a smaller total, and the view must not strand the
        // viewer on a page that no longer exists.
        getTasks.mockResolvedValue(page(25, 25, 30));
        fireEvent.click(screen.getByRole('button', { name: 'Next' }));

        expect(await screen.findByText('Page 2 of 2')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Next' })).toBeDisabled();
    });
});
