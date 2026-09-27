import { describe, it, expect, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import CalendarPage from './CalendarPage';

vi.mock('../../api/Discovery/calendarService', () => ({
    calendarService: { getEvents: () => Promise.resolve([]) },
}));

const renderPage = () => render(<MemoryRouter><CalendarPage /></MemoryRouter>);

const activeView = () => screen.getAllByRole('button').find(b => b.getAttribute('aria-pressed') === 'true')?.textContent;

describe('CalendarPage view mode', () => {
    beforeEach(() => localStorage.clear());

    it('opens on Week when nothing is saved', async () => {
        renderPage();

        expect(await screen.findByRole('button', { name: 'Week' })).toHaveAttribute('aria-pressed', 'true');
    });

    it('keeps a chosen view across a reload', async () => {
        const { unmount } = renderPage();
        fireEvent.click(await screen.findByRole('button', { name: 'Month' }));
        unmount();

        renderPage();

        expect(await screen.findByRole('button', { name: 'Month' })).toHaveAttribute('aria-pressed', 'true');
    });

    // The key was renamed with the default, so a Month saved under the old
    // default doesn't keep an existing browser on Month.
    it('ignores a view saved under the old key', async () => {
        localStorage.setItem('calendar_view_mode', 'month');

        renderPage();

        await screen.findByRole('button', { name: 'Week' });
        expect(activeView()).toBe('Week');
    });
});
