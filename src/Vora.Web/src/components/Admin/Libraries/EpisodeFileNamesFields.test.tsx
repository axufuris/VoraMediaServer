import { beforeEach, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import EpisodeFileNamesFields from './EpisodeFileNamesFields';
import { libraryAdminService, type EpisodeNamePreviewVM } from '../../../api/Media/libraryAdminService';

vi.mock('../../../api/Media/libraryAdminService', () => ({
    libraryAdminService: { previewEpisodeFileNames: vi.fn() },
}));

const checked: EpisodeNamePreviewVM = {
    filesFound: 3,
    moreFilesThanChecked: false,
    readFromName: 2,
    readFromDocument: 0,
    guessed: 0,
    unreadable: 1,
    files: [
        { path: 'Some Show/Season 1/Bonus Material.mkv', outcome: 'Unreadable' },
        { path: 'Ted Lasso/Season 1/Ted Lasso.1x1 - Pilot.mkv', outcome: 'Name', season: 1, episode: 1, title: 'Pilot' },
        { path: 'Ghosts/Season 5/Ghosts - S05E09-E10 - Carol.mkv', outcome: 'Name', season: 5, episode: 9, endEpisode: 10, title: 'Carol' },
    ],
};

const renderFields = (props: Partial<Parameters<typeof EpisodeFileNamesFields>[0]> = {}) => {
    const onScannerRegexChange = vi.fn();
    render(
        <EpisodeFileNamesFields
            folderPaths={['/media/tv']}
            excludeFilters={['.TDARR']}
            onScannerRegexChange={onScannerRegexChange}
            serverId="server"
            {...props}
        />,
    );
    return onScannerRegexChange;
};

beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(libraryAdminService.previewEpisodeFileNames).mockResolvedValue(checked);
});

it('starts on Automatic and only asks for a pattern when Custom is chosen', () => {
    const changed = renderFields();

    expect(screen.getByRole('radio', { name: /Automatic/ })).toBeChecked();
    expect(screen.queryByLabelText('Pattern')).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('radio', { name: /Custom pattern/ }));
    fireEvent.change(screen.getByLabelText('Pattern'), { target: { value: ' (?<Season>\\d+)x(?<Episode>\\d+) ' } });
    expect(changed).toHaveBeenLastCalledWith('(?<Season>\\d+)x(?<Episode>\\d+)');

    fireEvent.click(screen.getByRole('radio', { name: /Automatic/ }));
    expect(changed).toHaveBeenLastCalledWith(undefined);
});

it('shows a saved pattern as Custom', () => {
    renderFields({ scannerRegex: '(?<Season>\\d+)x(?<Episode>\\d+)' });

    expect(screen.getByRole('radio', { name: /Custom pattern/ })).toBeChecked();
    expect(screen.getByLabelText('Pattern')).toHaveValue('(?<Season>\\d+)x(?<Episode>\\d+)');
});

it('checks the files and lists the ones Vora cannot place first', async () => {
    renderFields();

    fireEvent.click(screen.getByRole('button', { name: 'Check my files' }));

    expect(await screen.findByText("Checked 3 files: 2 read from their names, 1 Vora can't place.")).toBeInTheDocument();
    expect(libraryAdminService.previewEpisodeFileNames).toHaveBeenCalledWith({ folderPaths: ['/media/tv'], scannerRegex: null, excludeFilters: ['.TDARR'] }, 'server');
    expect(screen.getByText('Some Show/Season 1/Bonus Material.mkv')).toBeInTheDocument();
    expect(screen.queryByText('Ted Lasso/Season 1/Ted Lasso.1x1 - Pilot.mkv')).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('checkbox', { name: "Only files Vora can't place" }));
    const table = within(screen.getByRole('table'));
    expect(table.getByText('S05E09-E10')).toBeInTheDocument();
    expect(table.getByText('S01E01')).toBeInTheDocument();
});

it('offers Automatic when it would read a custom pattern library better', async () => {
    vi.mocked(libraryAdminService.previewEpisodeFileNames).mockResolvedValue({ ...checked, unreadable: 2, automaticUnreadable: 0, automaticReadsDifferently: 0 });
    const changed = renderFields({ scannerRegex: '(?<Episode>\\d+) - (?<EpisodeTitle>.*)' });

    fireEvent.click(screen.getByRole('button', { name: 'Check my files' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Use Automatic' }));

    expect(changed).toHaveBeenLastCalledWith(undefined);
    expect(screen.getByRole('radio', { name: /Automatic/ })).toBeChecked();
});

it('says a result is out of date once the pattern changes', async () => {
    renderFields({ scannerRegex: '(?<Episode>\\d+) - (?<EpisodeTitle>.*)' });

    fireEvent.click(screen.getByRole('button', { name: 'Check my files' }));
    await screen.findByText(/Checked 3 files/);
    fireEvent.change(screen.getByLabelText('Pattern'), { target: { value: '(?<Episode>\\d+)' } });

    await waitFor(() => expect(screen.getByText(/The pattern changed since this check/)).toBeInTheDocument());
});

it('needs a folder before it can check', () => {
    renderFields({ folderPaths: [] });

    expect(screen.getByRole('button', { name: 'Check my files' })).toBeDisabled();
    expect(screen.getByText('Add a folder first.')).toBeInTheDocument();
});
