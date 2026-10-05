import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import RestoreResultView from './RestoreResultView';

describe('RestoreResultView', () => {
    it('shows what was skipped and why for each section', () => {
        render(
            <RestoreResultView
                sectionNames={{ 'users.watch-history': 'Watch History', 'users.ratings': 'Ratings & Likes' }}
                result={{
                    success: true,
                    sections: [
                        {
                            key: 'users.watch-history',
                            restored: true,
                            rowsImported: 1200,
                            rowsSkipped: 37,
                            warnings: ['37 watch-history rows were skipped because their item isn\'t on this server.']
                        },
                        { key: 'users.ratings', restored: true, rowsImported: 15, rowsSkipped: 0, warnings: [] }
                    ]
                }}
            />
        );

        expect(screen.getByText(/37 rows were skipped/)).toBeInTheDocument();
        expect(screen.getByText('Watch History')).toBeInTheDocument();
        expect(screen.getByText('1,200 restored · 37 skipped')).toBeInTheDocument();
        expect(screen.getByText(/because their item isn't on this server/)).toBeInTheDocument();
        expect(screen.getByText('15 restored')).toBeInTheDocument();
    });

    it('shows the error of a failed restore', () => {
        render(
            <RestoreResultView
                sectionNames={{}}
                result={{
                    success: false,
                    error: "Section 'users.ratings' failed; the entire restore was rolled back.",
                    sections: [{ key: 'users.ratings', restored: false, rowsImported: 0, rowsSkipped: 0, warnings: [], error: 'boom' }]
                }}
            />
        );

        expect(screen.getByText(/Restore failed: Section 'users.ratings' failed/)).toBeInTheDocument();
        expect(screen.getByText('boom')).toBeInTheDocument();
        expect(screen.getByText('users.ratings')).toBeInTheDocument();
    });
});
