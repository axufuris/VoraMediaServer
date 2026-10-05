import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { useState } from 'react';
import BackupSectionPicker from './BackupSectionPicker';
import type { AvailableSectionVM, BackupSizeEstimateVM } from '../../../api/System/backupsService';

const available: AvailableSectionVM[] = [
    { key: 'settings.server', displayName: 'Server Settings', group: 'Settings', requiresExplicitConfirm: false, canGrowLarge: false },
    { key: 'users.watch-history', displayName: 'Watch History', group: 'UserData', requiresExplicitConfirm: true, canGrowLarge: true },
    { key: 'users.playlists', displayName: 'Playlists', group: 'UserData', requiresExplicitConfirm: true, canGrowLarge: false }
];

const estimate: BackupSizeEstimateVM = {
    estimatedAtUtc: '2026-10-05T12:00:00Z',
    overheadBytes: 0,
    sections: [
        { key: 'settings.server', estimatedBytes: 2 * 1024 * 1024, rowCount: 1, failed: false },
        { key: 'users.watch-history', estimatedBytes: 10 * 1024 * 1024, rowCount: 50_000, failed: false },
        { key: 'users.playlists', estimatedBytes: 512 * 1024, rowCount: 300, failed: false }
    ]
};

function Harness({ initial = null, current = estimate, estimating = false, onReestimate = () => {} }: {
    initial?: string[] | null;
    current?: BackupSizeEstimateVM | null;
    estimating?: boolean;
    onReestimate?: () => void;
}) {
    const [keys, setKeys] = useState<string[] | null>(initial);
    return (
        <BackupSectionPicker
            available={available}
            includedKeys={keys}
            onChange={setKeys}
            estimate={current}
            estimating={estimating}
            estimateFailed={false}
            onReestimate={onReestimate}
        />
    );
}

describe('BackupSectionPicker', () => {
    it('shows each section size and a total that follows the ticked boxes', () => {
        render(<Harness />);

        expect(screen.getByText('10 MB')).toBeInTheDocument();
        expect(screen.getByText('512 KB')).toBeInTheDocument();
        expect(screen.getByText(/about 12.5 MB/)).toBeInTheDocument();

        fireEvent.click(screen.getByRole('checkbox', { name: /Watch History/ }));

        expect(screen.getByText(/about 2.5 MB/)).toBeInTheDocument();
    });

    it('marks only sections that can grow big as large', () => {
        render(<Harness />);

        expect(screen.getAllByText('large')).toHaveLength(1);
        expect(screen.getByRole('checkbox', { name: /Watch History/ })).toBeInTheDocument();
    });

    it('says it is estimating until the sizes arrive', () => {
        render(<Harness current={null} estimating />);

        expect(screen.getByText(/Estimating backup size/)).toBeInTheDocument();
        expect(screen.queryByText(/about/)).not.toBeInTheDocument();
    });

    it('asks the server for a fresh estimate on request', () => {
        const onReestimate = vi.fn();
        render(<Harness onReestimate={onReestimate} />);

        fireEvent.click(screen.getByRole('button', { name: 'Re-estimate' }));

        expect(onReestimate).toHaveBeenCalledTimes(1);
    });

    it('reports when nothing is ticked', () => {
        render(<Harness />);

        fireEvent.click(screen.getByRole('button', { name: 'None' }));

        expect(screen.getByText('No sections selected.')).toBeInTheDocument();
    });
});
