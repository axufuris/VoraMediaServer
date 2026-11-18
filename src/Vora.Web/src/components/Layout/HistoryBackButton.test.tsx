import { afterEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import HistoryBackButton from './HistoryBackButton';

const installedApp = (installed: boolean) => {
    vi.stubGlobal('matchMedia', () => ({
        matches: installed,
        addEventListener: () => { },
        removeEventListener: () => { },
    }));
};

const renderAt = (entries: string[], homePath = '/', initialIndex = entries.length - 1) => render(
    <MemoryRouter initialEntries={entries} initialIndex={initialIndex}>
        <HistoryBackButton homePath={homePath} />
        <Routes>
            <Route path="/" element={<p>Home page</p>} />
            <Route path="/media/:id" element={<p>Details page</p>} />
            <Route path="/library/:id" element={<p>Library page</p>} />
            <Route path="/server/:serverId" element={<p>Server home page</p>} />
            <Route path="/server/:serverId/media/:id" element={<p>Server details page</p>} />
        </Routes>
    </MemoryRouter>,
);

describe('HistoryBackButton', () => {
    afterEach(() => {
        cleanup();
        vi.unstubAllGlobals();
        delete window.voraDesktop;
    });

    it('is not drawn in a browser tab, which has its own Back button', () => {
        installedApp(false);

        renderAt(['/', '/media/1']);

        expect(screen.queryByRole('button', { name: 'Back' })).toBeNull();
    });

    it('is drawn in an installed app on a page that is not Home', () => {
        installedApp(true);

        renderAt(['/', '/media/1']);

        expect(screen.getByRole('button', { name: 'Back' })).toBeInTheDocument();
    });

    it('is drawn in Vora Desktop, which has no browser around it', () => {
        installedApp(false);
        window.voraDesktop = {} as NonNullable<typeof window.voraDesktop>;

        renderAt(['/', '/media/1']);

        expect(screen.getByRole('button', { name: 'Back' })).toBeInTheDocument();
    });

    it('is not drawn on Home, for the default server or a chosen one', () => {
        installedApp(true);

        renderAt(['/'], '/');
        expect(screen.queryByRole('button', { name: 'Back' })).toBeNull();
        cleanup();

        renderAt(['/server/abc/'], '/server/abc');
        expect(screen.queryByRole('button', { name: 'Back' })).toBeNull();
    });

    it('steps back through the history to the page it came from', () => {
        installedApp(true);
        renderAt(['/', '/library/7', '/media/1']);

        fireEvent.click(screen.getByRole('button', { name: 'Back' }));

        expect(screen.getByText('Library page')).toBeInTheDocument();
    });

    it('goes Home when the page was opened straight from a link and has nothing behind it', () => {
        installedApp(true);
        renderAt(['/media/1']);

        fireEvent.click(screen.getByRole('button', { name: 'Back' }));

        expect(screen.getByText('Home page')).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: 'Back' })).toBeNull();
    });

    it('goes to the chosen server\'s Home when that is the server in use', () => {
        installedApp(true);
        renderAt(['/server/abc/media/1'], '/server/abc');

        fireEvent.click(screen.getByRole('button', { name: 'Back' }));

        expect(screen.getByText('Server home page')).toBeInTheDocument();
    });

    it('fills with the accent on focus instead of drawing a ring', () => {
        installedApp(true);
        renderAt(['/', '/media/1']);

        const classes = screen.getByRole('button', { name: 'Back' }).className;

        expect(classes).toContain('focus-visible:bg-[var(--vora-accent-500)]!');
        expect(classes).toContain('focus-visible:text-[var(--vora-accent-contrast)]!');
        expect(classes).toContain('focus-visible:outline-none!');
    });
});
