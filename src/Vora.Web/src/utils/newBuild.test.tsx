import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { act, render, screen } from '@testing-library/react';
import { Suspense } from 'react';
import { lazyPage, reloadForNewBuild, ReloadWindowMs } from './newBuild';
import PageErrorBoundary from '../components/Common/PageErrorBoundary';

const oldBuildFailure = () => Promise.reject(new Error('Failed to fetch dynamically imported module'));

describe('reloadForNewBuild', () => {
    beforeEach(() => sessionStorage.clear());
    afterEach(() => vi.restoreAllMocks());

    it('reloads once, then lets a page that keeps failing show its error', () => {
        const reload = vi.fn();

        expect(reloadForNewBuild(1_000_000, sessionStorage, reload)).toBe(true);
        expect(reloadForNewBuild(1_000_000 + ReloadWindowMs - 1, sessionStorage, reload)).toBe(false);
        expect(reload).toHaveBeenCalledTimes(1);
    });

    it('reloads again for a later update', () => {
        const reload = vi.fn();

        reloadForNewBuild(1_000_000, sessionStorage, reload);
        expect(reloadForNewBuild(1_000_000 + ReloadWindowMs, sessionStorage, reload)).toBe(true);
        expect(reload).toHaveBeenCalledTimes(2);
    });

    it('does not reload when the browser keeps no session storage, since it could not stop a loop', () => {
        vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('Storage is blocked'); });
        const reload = vi.fn();

        expect(reloadForNewBuild(1_000_000, sessionStorage, reload)).toBe(false);
        expect(reload).not.toHaveBeenCalled();
    });
});

describe('lazyPage', () => {
    afterEach(() => vi.restoreAllMocks());

    it('keeps the loading screen up while a tab from an older build reloads', async () => {
        const Page = lazyPage(oldBuildFailure, () => true);

        render(<Suspense fallback={<p>Loading</p>}><Page /></Suspense>);
        await act(async () => { await Promise.resolve(); });

        expect(screen.getByText('Loading')).toBeInTheDocument();
    });

    it('shows the error page instead of a blank screen when the page still fails after the reload', async () => {
        vi.spyOn(console, 'error').mockImplementation(() => { });
        const Page = lazyPage(oldBuildFailure, () => false);

        render(
            <PageErrorBoundary resetKey="/">
                <Suspense fallback={<p>Loading</p>}><Page /></Suspense>
            </PageErrorBoundary>
        );

        expect(await screen.findByText("This page couldn't be shown")).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Reload' })).toBeInTheDocument();
    });

    it('shows the page when it loads', async () => {
        const Page = lazyPage(() => Promise.resolve({ default: () => <h1>Library</h1> }), () => true);

        render(<Suspense fallback={<p>Loading</p>}><Page /></Suspense>);

        expect(await screen.findByRole('heading', { name: 'Library' })).toBeInTheDocument();
    });
});
