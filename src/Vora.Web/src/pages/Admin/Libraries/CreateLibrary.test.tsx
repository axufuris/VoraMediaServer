import { describe, it, expect, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import CreateLibrary from './CreateLibrary';

const mocks = vi.hoisted(() => ({
    createLibrary: vi.fn(),
    getServerSettings: vi.fn(),
    alert: vi.fn(),
}));

vi.mock('../../../api/Media/libraryAdminService', () => ({ libraryAdminService: { createLibrary: mocks.createLibrary } }));
vi.mock('../../../api/System/pluginAdminService', () => ({
    pluginAdminService: {
        getMetadataProviders: () => Promise.resolve([]),
        getRatingsProviders: () => Promise.resolve([]),
        getArtworkProviders: () => Promise.resolve([]),
    },
}));
vi.mock('../../../api/System/systemSettingsAdminService', () => ({
    systemSettingsAdminService: { getServerSettings: mocks.getServerSettings },
}));
vi.mock('../../../dialogs', () => ({ useDialog: () => ({ alert: mocks.alert, confirm: vi.fn() }) }));

const movieLabel = 'Enable intro detection (studio logos and opening titles)';

const renderCreate = async () => {
    render(
        <MemoryRouter>
            <CreateLibrary />
        </MemoryRouter>,
    );
    await waitFor(() => expect(mocks.getServerSettings).toHaveBeenCalled());
};

describe('CreateLibrary intro detection', () => {
    beforeEach(() => {
        Object.values(mocks).forEach(m => m.mockReset());
        mocks.getServerSettings.mockResolvedValue({ runDetections: 1, videoThumbnailGeneration: 0 });
        mocks.createLibrary.mockResolvedValue(undefined);
    });

    it('offers the intro checkbox on a movie library, on by default from the server setting', async () => {
        await renderCreate();

        await waitFor(() => expect(screen.getByLabelText(movieLabel)).toBeChecked());
    });

    it('sends the choice when the admin turns it off', async () => {
        await renderCreate();
        await waitFor(() => expect(screen.getByLabelText(movieLabel)).toBeChecked());

        fireEvent.click(screen.getByLabelText(movieLabel));
        fireEvent.change(screen.getByPlaceholderText('e.g. Movies, Kids TV'), { target: { value: 'Films' } });
        fireEvent.click(screen.getByRole('button', { name: 'Create library' }));

        await waitFor(() => expect(mocks.createLibrary).toHaveBeenCalledWith(
            expect.objectContaining({ type: 1, enableIntroDetection: false }),
            undefined,
        ));
    });
});
