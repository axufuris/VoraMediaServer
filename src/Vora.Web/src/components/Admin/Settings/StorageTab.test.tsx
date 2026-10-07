import { describe, it, expect, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import StorageTab from './StorageTab';
import type { UnusedFilesReportVM } from '../../../api/System/maintenanceService';

const mocks = vi.hoisted(() => ({ scan: vi.fn(), remove: vi.fn(), confirm: vi.fn(), alert: vi.fn() }));
vi.mock('../../../api/System/maintenanceService', () => ({
    maintenanceService: { scanUnusedFiles: mocks.scan, removeUnusedFiles: mocks.remove },
}));
vi.mock('../../../dialogs', () => ({ useDialog: () => ({ confirm: mocks.confirm, alert: mocks.alert }) }));

const report: UnusedFilesReportVM = {
    scannedAt: '2026-10-07T12:00:00Z',
    removed: false,
    removableFiles: 3,
    removableBytes: 3 * 1024 * 1024,
    groups: [
        { kind: 'ScrubThumbnails', folder: '/app/data/video-thumbnails', files: 3, bytes: 3 * 1024 * 1024, removable: true, examples: ['3f2c…'] },
        { kind: 'Artwork', folder: '/app/data/custom_artwork', files: 0, bytes: 0, removable: true, examples: [] },
        { kind: 'Recordings', folder: '/app/data/iptv/dvr', files: 1, bytes: 2048, removable: false, examples: ['Old_Show_20250101_200000.ts'] },
    ],
};

const renderTab = () => render(<MemoryRouter><StorageTab /></MemoryRouter>);

describe('StorageTab', () => {
    beforeEach(() => {
        mocks.scan.mockReset().mockResolvedValue(report);
        mocks.remove.mockReset().mockResolvedValue(undefined);
        mocks.confirm.mockReset();
        mocks.alert.mockReset();
    });

    it('lists only the kinds of file it found, with recordings marked as left in place', async () => {
        renderTab();
        fireEvent.click(screen.getByRole('button', { name: 'Scan for unused files' }));

        expect(await screen.findByText('Scrub-bar thumbnails')).toBeInTheDocument();
        expect(screen.queryByText('Artwork and covers')).toBeNull();
        expect(screen.getByText('Recordings Vora doesn’t know about')).toBeInTheDocument();
        expect(screen.getByText(/left in place/)).toBeInTheDocument();
        expect(screen.getByText('3 files can be removed, freeing 3.0 MB.')).toBeInTheDocument();
    });

    it('removes nothing unless the admin confirms', async () => {
        mocks.confirm.mockResolvedValue(false);
        renderTab();
        fireEvent.click(screen.getByRole('button', { name: 'Scan for unused files' }));
        fireEvent.click(await screen.findByRole('button', { name: 'Remove 3 files' }));

        await waitFor(() => expect(mocks.confirm).toHaveBeenCalled());
        expect(mocks.remove).not.toHaveBeenCalled();
    });

    it('starts the clean-up once confirmed and points to the task', async () => {
        mocks.confirm.mockResolvedValue(true);
        renderTab();
        fireEvent.click(screen.getByRole('button', { name: 'Scan for unused files' }));
        fireEvent.click(await screen.findByRole('button', { name: 'Remove 3 files' }));

        expect(await screen.findByRole('status')).toHaveTextContent('Removing unused files in the background');
        expect(mocks.remove).toHaveBeenCalledTimes(1);
        expect(screen.queryByRole('button', { name: 'Remove 3 files' })).toBeNull();
    });

    it('says so when nothing is unused', async () => {
        mocks.scan.mockResolvedValue({ ...report, removableFiles: 0, removableBytes: 0, groups: report.groups.map(g => ({ ...g, files: 0, bytes: 0, examples: [] })) });
        renderTab();
        fireEvent.click(screen.getByRole('button', { name: 'Scan for unused files' }));

        expect(await screen.findByText(/Nothing unused/)).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: /^Remove/ })).toBeNull();
    });
});
