import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import type { ReactElement } from 'react';
import PageErrorBoundary from './PageErrorBoundary';

function BrokenPage(): ReactElement {
    throw new Error('The page broke');
}

describe('PageErrorBoundary', () => {
    beforeEach(() => { vi.spyOn(console, 'error').mockImplementation(() => { }); });
    afterEach(() => vi.restoreAllMocks());

    it('offers a reload instead of leaving the screen blank', () => {
        render(<PageErrorBoundary resetKey="/library/1"><BrokenPage /></PageErrorBoundary>);

        expect(screen.getByText("This page couldn't be shown")).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Reload' })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Go to Home' })).toBeInTheDocument();
    });

    it('shows the next page after navigating away', () => {
        const { rerender } = render(<PageErrorBoundary resetKey="/library/1"><BrokenPage /></PageErrorBoundary>);

        rerender(<PageErrorBoundary resetKey="/search"><h1>Search</h1></PageErrorBoundary>);

        expect(screen.getByRole('heading', { name: 'Search' })).toBeInTheDocument();
        expect(screen.queryByText("This page couldn't be shown")).not.toBeInTheDocument();
    });
});
