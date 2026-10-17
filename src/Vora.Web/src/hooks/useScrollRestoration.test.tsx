import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { useCallback } from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, useNavigate } from 'react-router-dom';
import { forgetScrollPositions, SCROLL_RESTORE_TIMEOUT_MS, useScrollRestoration } from './useScrollRestoration';

const VIEWPORT = 800;

const page = { height: VIEWPORT, top: 0 };

function fakeLayout(element: HTMLElement) {
    Object.defineProperty(element, 'clientHeight', { configurable: true, get: () => VIEWPORT });
    Object.defineProperty(element, 'scrollHeight', { configurable: true, get: () => page.height });
    Object.defineProperty(element, 'scrollTop', {
        configurable: true,
        get: () => page.top,
        set: (value: number) => { page.top = Math.max(0, Math.min(value, page.height - VIEWPORT)); },
    });
}

const PATHS = ['/library/movies', '/media/alien', '/media/heat', '/collections?tab=mine'];

function Shell() {
    const scrollRef = useScrollRestoration();
    const navigate = useNavigate();
    const mainRef = useCallback((node: HTMLElement | null) => {
        if (node) fakeLayout(node);
        scrollRef(node);
    }, [scrollRef]);
    return (
        <>
            {PATHS.map(path => <button key={path} type="button" onClick={() => navigate(path)}>{path}</button>)}
            <button type="button" onClick={() => navigate(-1)}>Back</button>
            <main data-testid="main" ref={mainRef} />
        </>
    );
}

function go(to: string) {
    fireEvent.click(screen.getByRole('button', { name: to }));
}

function scrollBy(viewer: HTMLElement, top: number) {
    viewer.scrollTop = top;
    fireEvent.scroll(viewer);
}

function renderAt(path: string) {
    render(<MemoryRouter initialEntries={[path]}><Shell /></MemoryRouter>);
    return screen.getByTestId('main');
}

describe('useScrollRestoration', () => {
    beforeEach(() => {
        forgetScrollPositions();
        page.height = 6000;
        page.top = 0;
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('goes back to where the library was left once its items have loaded', async () => {
        const main = renderAt('/library/movies');
        scrollBy(main, 3200);

        go('/media/alien');
        expect(page.top).toBe(0);

        page.height = 1000;
        go('Back');
        expect(page.top).toBe(0);

        page.height = 9000;
        await waitFor(() => expect(page.top).toBe(3200));
    });

    it('opens a new page at the top', () => {
        const main = renderAt('/library/movies');
        scrollBy(main, 2400);

        go('/media/heat');

        expect(page.top).toBe(0);
    });

    it('keeps the position when only the query string changes', () => {
        const main = renderAt('/collections');
        scrollBy(main, 1500);

        go('/collections?tab=mine');

        expect(page.top).toBe(1500);
    });

    it('lets the viewer take over instead of jumping once the page has loaded', async () => {
        const main = renderAt('/library/movies');
        scrollBy(main, 3200);
        go('/media/alien');

        page.height = 1000;
        go('Back');
        fireEvent.wheel(main);
        scrollBy(main, 50);
        page.height = 9000;
        await new Promise(resolve => setTimeout(resolve, 50));

        expect(page.top).toBe(50);
    });

    it('scrolls as far as the page goes when it never grows tall enough', async () => {
        const main = renderAt('/library/movies');
        scrollBy(main, 3200);
        go('/media/alien');

        page.height = 2000;
        let now = 0;
        vi.spyOn(performance, 'now').mockImplementation(() => now);
        go('Back');
        now = SCROLL_RESTORE_TIMEOUT_MS;

        await waitFor(() => expect(page.top).toBe(1200));
    });

    it('forgets nothing when the same page is visited twice', async () => {
        const main = renderAt('/library/movies');
        scrollBy(main, 900);
        go('/media/alien');
        go('/library/movies');
        scrollBy(main, 4000);

        go('Back');
        go('Back');

        await waitFor(() => expect(page.top).toBe(900));
    });
});
