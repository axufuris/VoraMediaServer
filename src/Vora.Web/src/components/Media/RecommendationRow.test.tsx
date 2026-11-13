import { describe, it, expect, afterEach } from 'vitest';
import { cleanup, render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import type { LibraryItem } from '../../api/Media/libraryService';
import type { RecommendationListVM } from '../../api/Discovery/recommendationService';
import { DialogProvider } from '../../dialogs';
import MediaItemMenuProvider from './ItemMenu/MediaItemMenuProvider';
import RecommendationRow from './RecommendationRow';

const heat = { id: 'movie-1', title: 'Heat', type: 'Movie' } as LibraryItem;

function renderRow(list: RecommendationListVM) {
    render(
        <MemoryRouter>
            <DialogProvider>
                <div data-vora-client="">
                    <MediaItemMenuProvider>
                        <RecommendationRow list={list} />
                    </MediaItemMenuProvider>
                </div>
            </DialogProvider>
        </MemoryRouter>,
    );
}

describe('RecommendationRow', () => {
    afterEach(cleanup);

    it('marks a row the AI made with a small icon beside its title, not with text', () => {
        renderRow({ title: 'Gritty Heists', weight: 1, isAi: true, items: [heat] });

        const heading = screen.getByRole('heading', { level: 2 });
        expect(within(heading).getByText('Gritty Heists')).toBeInTheDocument();
        const mark = within(heading).getByRole('img', { name: 'Made by AI' });
        expect(mark.tagName.toLowerCase()).toBe('svg');
        expect(mark).not.toHaveAttribute('tabindex');
        expect(heading.textContent).not.toMatch(/AI:/);
    });

    it('shows no mark on a row the AI did not make', () => {
        renderRow({ title: 'Because you watched Heat', weight: 1, isAi: false, items: [heat] });

        const heading = screen.getByRole('heading', { level: 2, name: 'Because you watched Heat' });
        expect(within(heading).queryByRole('img')).toBeNull();
    });

    it('shows no mark when the server does not say', () => {
        renderRow({ title: 'Because you like Crime', weight: 1, items: [heat] });

        expect(screen.queryByRole('img', { name: 'Made by AI' })).toBeNull();
    });
});
