import { beforeEach, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import DedupePage from './DedupePage';
import { DialogProvider } from '../../dialogs';
import { adminService, type DedupeGroupVM, type DedupeItemVM } from '../../api/System/adminService';

vi.mock('../../api/System/adminService', () => ({
    adminService: { getDuplicates: vi.fn() },
}));

const part = (partId: string, fileName: string): DedupeItemVM => ({
    partId, fileName, filePath: `/tv/Ted Lasso/Season 1/${fileName}`, fileSizeBytes: 1000, videoCodec: 'h264', hdrFormat: '',
    audioTracks: [], qualityScore: 100, container: 'mkv',
});

const group = (episodesInFileNames: string[]): DedupeGroupVM => ({
    mediaItemId: 'episode-1', title: 'Ted Lasso - S01E01', type: 'Episode', mediaKind: 'video', resolution: '1080P',
    parts: [part('a', 'Ted Lasso.1x1 - Pilot.mkv'), part('b', 'Ted Lasso.1x2 - Biscuits.mkv')],
    episodesInFileNames,
});

const renderPage = () => render(<MemoryRouter><DialogProvider><DedupePage /></DialogProvider></MemoryRouter>);

beforeEach(() => vi.clearAllMocks());

it('says files named as different episodes are not copies', async () => {
    vi.mocked(adminService.getDuplicates).mockResolvedValue([group(['S01E01', 'S01E02'])]);
    renderPage();

    expect(await screen.findByText(/These aren't copies: the files are named as S01E01, S01E02\./)).toBeInTheDocument();
});

it('says nothing extra about real copies', async () => {
    vi.mocked(adminService.getDuplicates).mockResolvedValue([group([])]);
    renderPage();

    expect(await screen.findByText('Ted Lasso - S01E01')).toBeInTheDocument();
    expect(screen.queryByText(/These aren't copies/)).not.toBeInTheDocument();
});
