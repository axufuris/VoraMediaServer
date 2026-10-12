import { describe, it, expect, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import CalendarPage from './CalendarPage';

const mocks = vi.hoisted(() => ({ getEvents: vi.fn() }));

vi.mock('../../api/Discovery/calendarService', () => ({
    calendarService: { getEvents: mocks.getEvents },
}));

const renderPage = () => render(<MemoryRouter><CalendarPage /></MemoryRouter>);

const activeView = () => screen.getAllByRole('button').find(b => b.getAttribute('aria-pressed') === 'true')?.textContent;

const middayToday = () => {
    const noon = new Date();
    noon.setHours(12, 0, 0, 0);
    return noon;
};

const episodeOn = (day: Date) => ({
    id: 'ev-1',
    title: 'Severance',
    subTitle: 'S3 · E1',
    mediaType: 'Episode',
    releaseDate: day.toISOString(),
    releaseType: 'Digital',
    contentRating: 'TV-MA',
    isInLibrary: true,
    isWatchlisted: false,
});

const longDay = (day: Date) => day.toLocaleDateString(undefined, { weekday: 'long', month: 'long', day: 'numeric' });

beforeEach(() => {
    localStorage.clear();
    mocks.getEvents.mockResolvedValue([]);
});

describe('CalendarPage view mode', () => {

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

describe('CalendarPage day list for phones', () => {
    it('lists every day of the week with what releases on it', async () => {
        const today = middayToday();
        mocks.getEvents.mockResolvedValue([episodeOn(today)]);

        renderPage();

        const todaySection = await screen.findByRole('region', { name: longDay(today) });
        expect(await within(todaySection).findByText('Severance')).toBeInTheDocument();
        expect(screen.getAllByRole('region')).toHaveLength(7);
    });

    it('lists only the days with releases in the month view', async () => {
        const today = middayToday();
        mocks.getEvents.mockResolvedValue([episodeOn(today)]);

        renderPage();
        fireEvent.click(await screen.findByRole('button', { name: 'Month' }));

        const todaySection = await screen.findByRole('region', { name: longDay(today) });
        expect(within(todaySection).getByText('Severance')).toBeInTheDocument();
        expect(screen.getAllByRole('region')).toHaveLength(1);
    });
});
