import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { AxiosError, type AxiosResponse } from 'axios';
import ConnectionModal from './ConnectionModal';
import { serverVault } from '../../../utils/serverVault';
import type { LibrarySummary } from '../../../api/Media/libraryService';
import type {
    CreateWebhookConnection,
    GeneratedWebhookSecret,
    SaveWebhookConnection,
    WebhookConnectionVM,
    WebhookConnectionWithSecret,
    WebhookPreviewRequest,
    WebhookPreviewResult,
    WebhookRequestServerMappings,
    WebhookRequestServerVM
} from '../../../api/System/webhookAdminService';
import {
    FAKE_SECRET,
    FOCUS_FILL,
    FOCUS_FILL_PRIMARY,
    allTemplates,
    makeConnection,
    radarrTemplate,
    requestServers,
    sonarrTemplate,
    withSecret
} from '../../../test/webhookFixtures';

const mocks = vi.hoisted(() => {
    const alert = vi.fn();
    const confirm = vi.fn();
    return {
        getConnection: vi.fn<(id: string, serverId?: string) => Promise<WebhookConnectionWithSecret>>(),
        createConnection: vi.fn<(body: CreateWebhookConnection, serverId?: string) => Promise<WebhookConnectionWithSecret>>(),
        updateConnection: vi.fn<(id: string, body: SaveWebhookConnection, serverId?: string) => Promise<WebhookConnectionVM>>(),
        regenerateSecret: vi.fn<(id: string, serverId?: string) => Promise<GeneratedWebhookSecret>>(),
        deleteConnection: vi.fn<(id: string, serverId?: string) => Promise<void>>(),
        preview: vi.fn<(request: WebhookPreviewRequest, serverId?: string) => Promise<WebhookPreviewResult>>(),
        getRequestServers: vi.fn<(serverId?: string) => Promise<WebhookRequestServerVM[]>>(),
        getRequestServerMappings: vi.fn<(id: string, serverId?: string) => Promise<WebhookRequestServerMappings>>(),
        getLibraries: vi.fn<(serverId?: string) => Promise<LibrarySummary[]>>(),
        copyText: vi.fn<(text: string) => Promise<boolean>>(),
        alert,
        confirm,
        dialog: { alert, confirm, prompt: vi.fn() }
    };
});

vi.mock('../../../api/System/webhookAdminService', () => ({
    webhookAdminService: {
        getConnection: mocks.getConnection,
        createConnection: mocks.createConnection,
        updateConnection: mocks.updateConnection,
        regenerateSecret: mocks.regenerateSecret,
        deleteConnection: mocks.deleteConnection,
        preview: mocks.preview,
        getRequestServers: mocks.getRequestServers,
        getRequestServerMappings: mocks.getRequestServerMappings
    }
}));
vi.mock('../../../api/Media/libraryService', () => ({ libraryService: { getLibraries: mocks.getLibraries } }));
vi.mock('../../../utils/copyText', () => ({ copyText: mocks.copyText }));
vi.mock('../../../dialogs', () => ({ useDialog: () => mocks.dialog }));

const libraries: LibrarySummary[] = [
    { id: 'lib1', name: 'Movies', type: 'Movie', isBeingWatched: true },
    { id: 'lib2', name: 'Kids movies', type: 'Movie', isBeingWatched: false }
];

const failure = (status: number, data: object) =>
    new AxiosError('Request failed', String(status), undefined, undefined, { status, data } as AxiosResponse);

const found = (overrides: Partial<WebhookRequestServerMappings> = {}): WebhookRequestServerMappings => ({
    name: 'Radarr 4K',
    rootFolders: ['/movies'],
    mappings: '/movies => /media/movies',
    unmatchedRoots: [],
    error: null,
    ...overrides
});

const renderModal = (props: Partial<React.ComponentProps<typeof ConnectionModal>> = {}) => {
    const onChanged = vi.fn();
    const onClose = vi.fn();
    const view = render(
        <ConnectionModal connection={null} templates={allTemplates()} onChanged={onChanged} onClose={onClose} {...props} />
    );
    return { onChanged, onClose, ...view };
};

const nameInput = () => screen.getByLabelText(/^Name/);
const mappingsInput = () => screen.getByLabelText('Path mappings');
const secretInput = () => screen.getByLabelText('Secret');
const save = () => screen.getByRole('button', { name: 'Save' });
const chooseTemplate = (id: string) => fireEvent.change(screen.getByLabelText('Template'), { target: { value: id } });

describe('ConnectionModal', () => {
    beforeEach(() => {
        vi.resetAllMocks();
        mocks.getConnection.mockResolvedValue(withSecret(makeConnection()));
        mocks.getRequestServers.mockResolvedValue(requestServers);
        mocks.getRequestServerMappings.mockResolvedValue(found());
        mocks.getLibraries.mockResolvedValue(libraries);
        mocks.copyText.mockResolvedValue(true);
        mocks.confirm.mockResolvedValue(true);
        mocks.alert.mockResolvedValue(undefined);
    });

    afterEach(() => {
        cleanup();
        localStorage.clear();
    });

    describe('adding a connection', () => {
        it('offers each template by name and describes the chosen one', () => {
            renderModal();

            const options = within(screen.getByLabelText('Template')).getAllByRole('option').map(option => option.textContent);
            expect(options).toEqual(['Radarr', 'Sonarr', 'Lidarr', 'Custom']);
            expect(screen.getByText(radarrTemplate.description)).toBeInTheDocument();

            chooseTemplate('sonarr_webhook');

            expect(screen.getByText(sonarrTemplate.description)).toBeInTheDocument();
            expect(screen.queryByText(radarrTemplate.description)).not.toBeInTheDocument();
        });

        it('starts from the template\'s name and follows it until the admin types their own', () => {
            renderModal();
            expect(nameInput()).toHaveValue('Radarr');

            chooseTemplate('custom_webhook');
            expect(nameInput()).toHaveValue('Custom webhook');

            fireEvent.change(nameInput(), { target: { value: 'My system' } });
            chooseTemplate('sonarr_webhook');
            expect(nameInput()).toHaveValue('My system');
        });

        it('starts on, with deletes handled, and has no delete button', () => {
            renderModal();

            expect(screen.getByRole('switch', { name: 'On' })).toHaveAttribute('aria-checked', 'true');
            expect(screen.getByRole('switch', { name: 'Move deleted files to Media Trash' })).toHaveAttribute('aria-checked', 'true');
            expect(screen.queryByRole('button', { name: 'Delete connection' })).not.toBeInTheDocument();
        });

        it('disables Save while the name is empty', () => {
            renderModal();
            expect(save()).toBeEnabled();

            fireEvent.change(nameInput(), { target: { value: '   ' } });

            expect(save()).toBeDisabled();
        });

        it('disables Save until every shown required setting is filled in', async () => {
            renderModal();
            chooseTemplate('custom_webhook');
            expect(save()).toBeDisabled();

            fireEvent.change(screen.getByLabelText(/^Path field/), { target: { value: 'file.path' } });
            expect(save()).toBeDisabled();

            const library = screen.getByLabelText(/^Library/);
            await within(library).findByRole('option', { name: 'Movies' });
            fireEvent.change(library, { target: { value: 'lib1' } });

            expect(save()).toBeEnabled();
        });

        it('creates the connection, then shows its address and secret in the same modal', async () => {
            mocks.createConnection.mockResolvedValue(withSecret(makeConnection({ id: 'new', name: 'Radarr', receivePath: '/api/webhooks/new', pathMappings: '' })));
            const { onChanged, onClose } = renderModal();

            fireEvent.click(save());

            expect(await screen.findByRole('heading', { name: 'Connection created' })).toBeInTheDocument();
            expect(mocks.createConnection).toHaveBeenCalledWith({
                providerId: 'radarr_webhook',
                name: 'Radarr',
                isEnabled: true,
                handleDeletes: true,
                pathMappings: '',
                settings: {}
            }, undefined);
            expect(screen.getByLabelText('Address to paste into Radarr')).toHaveValue(`${window.location.origin}/api/webhooks/new`);
            const secret = secretInput();
            expect(secret).toHaveValue(FAKE_SECRET);
            expect(secret).toHaveAttribute('type', 'text');
            expect(screen.queryByLabelText('Template')).not.toBeInTheDocument();
            expect(onChanged).toHaveBeenCalledTimes(1);
            expect(onClose).not.toHaveBeenCalled();
            expect(screen.getAllByRole('listitem').map(item => item.textContent)).toEqual([
                'In Radarr, go to Settings, Connect, and add a Webhook.',
                'Paste the address into the URL field.',
                'Set Password to the secret.'
            ]);

            fireEvent.click(screen.getByRole('button', { name: 'Done' }));
            expect(onClose).toHaveBeenCalledTimes(1);
        });

        it('copies the new connection\'s address and secret', async () => {
            mocks.createConnection.mockResolvedValue(withSecret(makeConnection({ id: 'new', receivePath: '/api/webhooks/new' })));
            renderModal();
            fireEvent.click(save());
            await screen.findByRole('heading', { name: 'Connection created' });

            fireEvent.click(screen.getByRole('button', { name: 'Copy address' }));
            await waitFor(() => expect(mocks.copyText).toHaveBeenCalledWith(`${window.location.origin}/api/webhooks/new`));
            fireEvent.click(screen.getByRole('button', { name: 'Copy secret' }));
            await waitFor(() => expect(mocks.copyText).toHaveBeenCalledWith(FAKE_SECRET));
        });

        it('shows the message of a refused connection and stays open', async () => {
            mocks.createConnection.mockRejectedValue(failure(400, { error: 'A connection named Radarr already exists.' }));
            const { onChanged, onClose } = renderModal();

            fireEvent.click(save());

            expect(await screen.findByRole('alert')).toHaveTextContent('A connection named Radarr already exists.');
            expect(screen.getByLabelText('Template')).toBeInTheDocument();
            expect(onChanged).not.toHaveBeenCalled();
            expect(onClose).not.toHaveBeenCalled();
            expect(save()).toBeEnabled();
        });

        it('sends the template\'s settings, using their defaults', async () => {
            mocks.createConnection.mockResolvedValue(withSecret(makeConnection({ id: 'new' })));
            renderModal();
            chooseTemplate('custom_webhook');
            fireEvent.change(screen.getByLabelText(/^Path field/), { target: { value: 'file.path' } });
            const library = screen.getByLabelText(/^Library/);
            await within(library).findByRole('option', { name: 'Kids movies' });
            fireEvent.change(library, { target: { value: 'lib2' } });
            fireEvent.change(screen.getByLabelText('Reports'), { target: { value: 'folder' } });

            fireEvent.click(save());

            await waitFor(() => expect(mocks.createConnection).toHaveBeenCalledTimes(1));
            expect(mocks.createConnection).toHaveBeenCalledWith({
                providerId: 'custom_webhook',
                name: 'Custom webhook',
                isEnabled: true,
                handleDeletes: true,
                pathMappings: '',
                settings: { pathField: 'file.path', mode: 'folder', folderDepth: '1', library: 'lib2', notes: '' }
            }, undefined);
        });
    });

    describe('filling in from a Request Server', () => {
        it('lists only the Request Servers that fit the chosen template', async () => {
            renderModal();

            const select = await screen.findByLabelText('Fill in from a Request Server');
            expect(within(select).getAllByRole('option').map(option => option.textContent)).toEqual(['Radarr 4K']);

            chooseTemplate('sonarr_webhook');
            const sonarr = await screen.findByLabelText('Fill in from a Request Server');
            expect(within(sonarr).getAllByRole('option').map(option => option.textContent)).toEqual(['Sonarr main']);
        });

        it('is not offered for a template with no Request Server provider, or with no server to use', async () => {
            renderModal();
            await screen.findByLabelText('Fill in from a Request Server');

            chooseTemplate('lidarr_webhook');
            expect(screen.queryByLabelText('Fill in from a Request Server')).not.toBeInTheDocument();
            chooseTemplate('custom_webhook');
            expect(screen.queryByLabelText('Fill in from a Request Server')).not.toBeInTheDocument();
        });

        it('is not offered when the admin has no matching Request Server', async () => {
            mocks.getRequestServers.mockResolvedValue([requestServers[1]]);
            renderModal();

            await waitFor(() => expect(mocks.getRequestServers).toHaveBeenCalled());
            expect(screen.queryByLabelText('Fill in from a Request Server')).not.toBeInTheDocument();
        });

        it('fills the name and the path mappings, without asking when the box is empty', async () => {
            renderModal();
            await screen.findByLabelText('Fill in from a Request Server');

            fireEvent.click(screen.getByRole('button', { name: 'Fill in' }));

            await waitFor(() => expect(mappingsInput()).toHaveValue('/movies => /media/movies'));
            expect(mocks.getRequestServerMappings).toHaveBeenCalledWith('rs1', undefined);
            expect(nameInput()).toHaveValue('Radarr 4K');
            expect(mocks.confirm).not.toHaveBeenCalled();
            expect(await screen.findByRole('status')).toHaveTextContent('Filled in from Radarr 4K.');
        });

        it('keeps a name the admin already typed', async () => {
            renderModal();
            await screen.findByLabelText('Fill in from a Request Server');
            fireEvent.change(nameInput(), { target: { value: 'Living room movies' } });

            fireEvent.click(screen.getByRole('button', { name: 'Fill in' }));

            await waitFor(() => expect(mappingsInput()).toHaveValue('/movies => /media/movies'));
            expect(nameInput()).toHaveValue('Living room movies');
        });

        it('asks before replacing text that is already in the path mappings, and replaces it when told to', async () => {
            renderModal();
            await screen.findByLabelText('Fill in from a Request Server');
            fireEvent.change(mappingsInput(), { target: { value: '/old => /new' } });

            fireEvent.click(screen.getByRole('button', { name: 'Fill in' }));

            await waitFor(() => expect(mappingsInput()).toHaveValue('/movies => /media/movies'));
            expect(mocks.confirm).toHaveBeenCalledTimes(1);
            expect(mocks.confirm.mock.calls[0][0]).toMatchObject({ title: 'Replace the path mappings?' });
            expect(nameInput()).toHaveValue('Radarr 4K');
        });

        it('changes nothing when the question is declined', async () => {
            mocks.confirm.mockResolvedValue(false);
            renderModal();
            await screen.findByLabelText('Fill in from a Request Server');
            fireEvent.change(mappingsInput(), { target: { value: '/old => /new' } });

            fireEvent.click(screen.getByRole('button', { name: 'Fill in' }));

            await waitFor(() => expect(mocks.confirm).toHaveBeenCalledTimes(1));
            await waitFor(() => expect(screen.getByRole('button', { name: 'Fill in' })).toBeEnabled());
            expect(mappingsInput()).toHaveValue('/old => /new');
            expect(nameInput()).toHaveValue('Radarr');
        });

        it('shows why the server could not be reached and fills nothing', async () => {
            mocks.getRequestServerMappings.mockResolvedValue(found({ mappings: '', error: 'Could not reach Radarr 4K.' }));
            renderModal();
            await screen.findByLabelText('Fill in from a Request Server');

            fireEvent.click(screen.getByRole('button', { name: 'Fill in' }));

            expect(await screen.findByRole('alert')).toHaveTextContent('Could not reach Radarr 4K.');
            expect(mappingsInput()).toHaveValue('');
            expect(nameInput()).toHaveValue('Radarr');
            expect(mocks.confirm).not.toHaveBeenCalled();
        });

        it('names the root folders it found no Vora folder for', async () => {
            mocks.getRequestServerMappings.mockResolvedValue(found({ unmatchedRoots: ['/archive'] }));
            renderModal();
            await screen.findByLabelText('Fill in from a Request Server');

            fireEvent.click(screen.getByRole('button', { name: 'Fill in' }));

            expect(await screen.findByRole('status')).toHaveTextContent('No Vora folder was found for /archive.');
        });

        it('says what to do when it read the root folders but could not pair any with a Vora folder', async () => {
            mocks.getRequestServerMappings.mockResolvedValue(found({ name: 'Movies', mappings: '', unmatchedRoots: ['/movies'] }));
            renderModal();
            await screen.findByLabelText('Fill in from a Request Server');

            fireEvent.click(screen.getByRole('button', { name: 'Fill in' }));

            const status = await screen.findByRole('status');
            expect(status).toHaveTextContent('Read the root folders of Movies (/movies) but could not tell which Vora folder each one is');
            expect(status).toHaveTextContent('Type the mapping yourself below');
            expect(mappingsInput()).toHaveValue('');
        });

        it('says so when the server has no root folders at all', async () => {
            mocks.getRequestServerMappings.mockResolvedValue(found({ name: 'Movies', mappings: '', unmatchedRoots: [], rootFolders: [] }));
            renderModal();
            await screen.findByLabelText('Fill in from a Request Server');

            fireEvent.click(screen.getByRole('button', { name: 'Fill in' }));

            expect(await screen.findByRole('status')).toHaveTextContent('Movies has no root folders to map.');
        });

        it('goes back to the template\'s own name when the template changes after a fill, and lets another fill replace the filled name', async () => {
            mocks.getRequestServerMappings.mockResolvedValue(found({ name: 'Movies', mappings: '/movies => /media/movies' }));
            renderModal();
            await screen.findByLabelText('Fill in from a Request Server');

            fireEvent.click(screen.getByRole('button', { name: 'Fill in' }));
            await waitFor(() => expect(nameInput()).toHaveValue('Movies'));
            mocks.getRequestServerMappings.mockResolvedValue(found({ name: 'Movies 4K', mappings: '/movies4k => /media/movies/4K' }));
            fireEvent.click(screen.getByRole('button', { name: 'Fill in' }));
            await waitFor(() => expect(nameInput()).toHaveValue('Movies 4K'));

            chooseTemplate('sonarr_webhook');

            expect(nameInput()).toHaveValue('Sonarr');
        });

        it('keeps a name the admin typed when the template changes', async () => {
            renderModal();
            await screen.findByLabelText('Fill in from a Request Server');

            fireEvent.change(nameInput(), { target: { value: 'My own name' } });
            chooseTemplate('sonarr_webhook');

            expect(nameInput()).toHaveValue('My own name');
        });

        it('is not part of editing a connection', () => {
            mocks.getConnection.mockResolvedValue(withSecret(makeConnection()));
            renderModal({ connection: makeConnection() });

            expect(mocks.getRequestServers).not.toHaveBeenCalled();
            expect(screen.queryByLabelText('Fill in from a Request Server')).not.toBeInTheDocument();
        });
    });

    describe('editing a connection', () => {
        const connection = makeConnection({ handleDeletes: false });

        it('loads the connection\'s fields and fixes its template', () => {
            renderModal({ connection });

            expect(screen.getByRole('heading', { name: 'Edit connection' })).toBeInTheDocument();
            expect(nameInput()).toHaveValue('Movies 4K');
            expect(screen.getByRole('switch', { name: 'On' })).toHaveAttribute('aria-checked', 'true');
            expect(screen.getByRole('switch', { name: 'Move deleted files to Media Trash' })).toHaveAttribute('aria-checked', 'false');
            expect(mappingsInput()).toHaveValue('/movies => /media/movies');
            expect(screen.queryByLabelText('Template')).not.toBeInTheDocument();
        });

        it('explains the switches and the path mappings', () => {
            renderModal({ connection });

            expect(screen.getByText('When this is off, deletes the app reports are ignored.')).toBeInTheDocument();
            expect(screen.getByText(/One per line, from => to: the folder as the app sees it, then as Vora sees it\. Leave this empty when both use the same paths\. Vora also matches by folder name\./)).toBeInTheDocument();
        });

        it('shows the address to paste, read-only, built from the receive path', () => {
            renderModal({ connection });

            const address = screen.getByLabelText('Address to paste into Radarr');
            expect(address).toHaveValue(`${window.location.origin}/api/webhooks/c1`);
            expect(address).toHaveAttribute('readonly');
        });

        it('uses the address of the server being managed on the multi-server page', async () => {
            serverVault.addOrUpdateServer({ id: 'second', name: 'Second', url: 'http://second:8080/', token: 't', profileId: 'p', isAdmin: true });
            renderModal({ connection, serverId: 'second' });

            expect(screen.getByLabelText('Address to paste into Radarr')).toHaveValue('http://second:8080/api/webhooks/c1');
            await waitFor(() => expect(mocks.getConnection).toHaveBeenCalledWith('c1', 'second'));
        });

        it('copies the address', async () => {
            renderModal({ connection });

            fireEvent.click(screen.getByRole('button', { name: 'Copy address' }));

            await waitFor(() => expect(mocks.copyText).toHaveBeenCalledWith(`${window.location.origin}/api/webhooks/c1`));
            expect(await screen.findByRole('button', { name: 'Address copied' })).toHaveTextContent('Copied');
        });

        it('loads the secret from the connection\'s own call and keeps it masked until revealed', async () => {
            renderModal({ connection });

            await waitFor(() => expect(secretInput()).toHaveValue(FAKE_SECRET));
            expect(mocks.getConnection).toHaveBeenCalledWith('c1', undefined);
            expect(secretInput()).toHaveAttribute('type', 'password');

            fireEvent.click(screen.getByRole('button', { name: 'Reveal secret' }));
            expect(secretInput()).toHaveAttribute('type', 'text');

            fireEvent.click(screen.getByRole('button', { name: 'Hide secret' }));
            expect(secretInput()).toHaveAttribute('type', 'password');
        });

        it('copies the secret', async () => {
            renderModal({ connection });
            await waitFor(() => expect(secretInput()).toHaveValue(FAKE_SECRET));

            fireEvent.click(screen.getByRole('button', { name: 'Copy secret' }));

            await waitFor(() => expect(mocks.copyText).toHaveBeenCalledWith(FAKE_SECRET));
            expect(await screen.findByRole('button', { name: 'Secret copied' })).toBeInTheDocument();
        });

        it('says so when the secret cannot be loaded, and offers nothing to reveal or copy', async () => {
            mocks.getConnection.mockRejectedValue(new Error('down'));
            renderModal({ connection });

            expect(await screen.findByText('The secret could not be loaded.')).toBeInTheDocument();
            expect(screen.getByRole('button', { name: 'Reveal secret' })).toBeDisabled();
            expect(screen.getByRole('button', { name: 'Copy secret' })).toBeDisabled();
        });

        it('asks before regenerating, explains the app must be updated, then shows the new secret', async () => {
            mocks.regenerateSecret.mockResolvedValue({ secret: 'another-fake-secret' });
            renderModal({ connection });
            await waitFor(() => expect(secretInput()).toHaveValue(FAKE_SECRET));

            fireEvent.click(screen.getByRole('button', { name: 'Regenerate secret' }));

            await waitFor(() => expect(secretInput()).toHaveValue('another-fake-secret'));
            expect(mocks.confirm).toHaveBeenCalledTimes(1);
            expect(mocks.confirm.mock.calls[0][0]).toMatchObject({
                title: 'Regenerate the secret?',
                message: expect.stringContaining('must then be updated with the new secret'),
                tone: 'danger'
            });
            expect(mocks.regenerateSecret).toHaveBeenCalledWith('c1', undefined);
            expect(secretInput()).toHaveAttribute('type', 'text');
        });

        it('keeps the secret when the question is declined', async () => {
            mocks.confirm.mockResolvedValue(false);
            renderModal({ connection });
            await waitFor(() => expect(secretInput()).toHaveValue(FAKE_SECRET));

            fireEvent.click(screen.getByRole('button', { name: 'Regenerate secret' }));

            await waitFor(() => expect(mocks.confirm).toHaveBeenCalledTimes(1));
            expect(mocks.regenerateSecret).not.toHaveBeenCalled();
            expect(secretInput()).toHaveValue(FAKE_SECRET);
        });

        it('says so when the secret could not be regenerated', async () => {
            mocks.regenerateSecret.mockRejectedValue(new Error('down'));
            renderModal({ connection });
            await waitFor(() => expect(secretInput()).toHaveValue(FAKE_SECRET));

            fireEvent.click(screen.getByRole('button', { name: 'Regenerate secret' }));

            expect(await screen.findByRole('alert')).toHaveTextContent('The secret could not be regenerated.');
            expect(secretInput()).toHaveValue(FAKE_SECRET);
        });

        it('flips the switches and saves what is on screen', async () => {
            mocks.updateConnection.mockResolvedValue(connection);
            const { onChanged, onClose } = renderModal({ connection });

            fireEvent.click(screen.getByRole('switch', { name: 'On' }));
            fireEvent.click(screen.getByRole('switch', { name: 'Move deleted files to Media Trash' }));
            expect(screen.getByRole('switch', { name: 'On' })).toHaveAttribute('aria-checked', 'false');
            expect(screen.getByRole('switch', { name: 'Move deleted files to Media Trash' })).toHaveAttribute('aria-checked', 'true');
            fireEvent.change(nameInput(), { target: { value: '  Renamed  ' } });
            fireEvent.change(mappingsInput(), { target: { value: '/a => /b' } });
            fireEvent.click(save());

            await waitFor(() => expect(onClose).toHaveBeenCalledTimes(1));
            expect(mocks.updateConnection).toHaveBeenCalledWith('c1', {
                name: 'Renamed',
                isEnabled: false,
                handleDeletes: true,
                pathMappings: '/a => /b',
                settings: {}
            }, undefined);
            expect(onChanged).toHaveBeenCalledTimes(1);
        });

        it('shows the server\'s message and stays open when a save is refused', async () => {
            mocks.updateConnection.mockRejectedValue(failure(400, { error: 'The path mappings are not valid.' }));
            const { onChanged, onClose } = renderModal({ connection });

            fireEvent.click(save());

            expect(await screen.findByRole('alert')).toHaveTextContent('The path mappings are not valid.');
            expect(onChanged).not.toHaveBeenCalled();
            expect(onClose).not.toHaveBeenCalled();
            expect(save()).toBeEnabled();
        });

        it('closes with Cancel and the close button without saving', () => {
            const { onClose } = renderModal({ connection });

            fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
            fireEvent.click(screen.getByRole('button', { name: 'Close' }));

            expect(onClose).toHaveBeenCalledTimes(2);
            expect(mocks.updateConnection).not.toHaveBeenCalled();
        });

        it('closes on Escape, but not while a question is being asked', async () => {
            let answer: (value: boolean) => void = () => { };
            mocks.confirm.mockReturnValue(new Promise<boolean>(resolve => { answer = resolve; }));
            const { onClose } = renderModal({ connection });

            fireEvent.click(screen.getByRole('button', { name: 'Delete connection' }));
            await waitFor(() => expect(mocks.confirm).toHaveBeenCalled());
            fireEvent.keyDown(window, { key: 'Escape' });
            expect(onClose).not.toHaveBeenCalled();

            answer(false);
            await waitFor(() => {
                fireEvent.keyDown(window, { key: 'Escape' });
                expect(onClose).toHaveBeenCalled();
            });
        });
    });

    describe('deleting a connection', () => {
        const connection = makeConnection();

        it('asks first, then deletes and closes', async () => {
            mocks.deleteConnection.mockResolvedValue(undefined);
            const { onChanged, onClose } = renderModal({ connection });

            fireEvent.click(screen.getByRole('button', { name: 'Delete connection' }));

            await waitFor(() => expect(onClose).toHaveBeenCalledTimes(1));
            expect(mocks.confirm.mock.calls[0][0]).toMatchObject({
                title: 'Delete this connection?',
                message: expect.stringContaining('Movies 4K'),
                tone: 'danger'
            });
            expect(mocks.deleteConnection).toHaveBeenCalledWith('c1', undefined);
            expect(onChanged).toHaveBeenCalledTimes(1);
        });

        it('does nothing when the question is declined', async () => {
            mocks.confirm.mockResolvedValue(false);
            const { onChanged, onClose } = renderModal({ connection });

            fireEvent.click(screen.getByRole('button', { name: 'Delete connection' }));

            await waitFor(() => expect(mocks.confirm).toHaveBeenCalledTimes(1));
            expect(mocks.deleteConnection).not.toHaveBeenCalled();
            expect(onChanged).not.toHaveBeenCalled();
            expect(onClose).not.toHaveBeenCalled();
        });

        it('says so and stays open when the delete fails', async () => {
            mocks.deleteConnection.mockRejectedValue(failure(404, {}));
            const { onClose } = renderModal({ connection });

            fireEvent.click(screen.getByRole('button', { name: 'Delete connection' }));

            expect(await screen.findByRole('alert')).toHaveTextContent('The connection could not be deleted.');
            expect(onClose).not.toHaveBeenCalled();
            expect(screen.getByRole('button', { name: 'Delete connection' })).toBeEnabled();
        });
    });

    describe('setup instructions', () => {
        it('are a numbered list, open for a new connection', () => {
            renderModal();

            const toggle = screen.getByRole('button', { name: 'Setup instructions' });
            expect(toggle).toHaveAttribute('aria-expanded', 'true');
            const list = screen.getByRole('list');
            expect(list.tagName).toBe('OL');
            expect(within(list).getAllByRole('listitem').map(item => item.textContent)).toEqual([
                'In Radarr, go to Settings, Connect, and add a Webhook.',
                'Paste the address into the URL field.',
                'Set Password to the secret.'
            ]);
        });

        it('follow the chosen template', () => {
            renderModal();

            chooseTemplate('sonarr_webhook');

            expect(within(screen.getByRole('list')).getAllByRole('listitem').map(item => item.textContent)).toEqual([
                'In Sonarr, go to Settings, Connect, and add a Webhook.'
            ]);
        });

        it('are collapsed when editing, and open and close from their heading', () => {
            renderModal({ connection: makeConnection() });

            const toggle = screen.getByRole('button', { name: 'Setup instructions' });
            expect(toggle).toHaveAttribute('aria-expanded', 'false');
            expect(screen.queryByRole('list')).not.toBeInTheDocument();

            fireEvent.click(toggle);
            expect(toggle).toHaveAttribute('aria-expanded', 'true');
            expect(screen.getByRole('list')).toBeInTheDocument();

            fireEvent.click(toggle);
            expect(screen.queryByRole('list')).not.toBeInTheDocument();
        });
    });

    describe('the template\'s own settings', () => {
        it('draws text, select, library and textarea fields with their labels, hints and placeholders', async () => {
            renderModal();
            chooseTemplate('custom_webhook');

            const path = screen.getByLabelText(/^Path field/);
            expect(path.tagName).toBe('INPUT');
            expect(path).toHaveAttribute('placeholder', 'file.path');
            expect(screen.getByText('The JSON field that holds the file path.')).toBeInTheDocument();

            const mode = screen.getByLabelText('Reports');
            expect(mode.tagName).toBe('SELECT');
            expect(screen.getByLabelText(/^Library/).tagName).toBe('SELECT');
            expect(screen.getByLabelText('Notes').tagName).toBe('TEXTAREA');
            await within(screen.getByLabelText(/^Library/)).findByRole('option', { name: 'Movies' });
        });

        it('marks a required field with a star and leaves the others plain', () => {
            renderModal();
            chooseTemplate('custom_webhook');

            expect(screen.getByLabelText(/^Path field/).closest('div')?.querySelector('label')?.textContent).toBe('Path field *');
            expect(screen.getByLabelText(/^Library/)).toHaveAttribute('aria-required', 'true');
            expect(screen.getByLabelText('Reports')).not.toHaveAttribute('aria-required');
            expect(screen.getByLabelText('Notes').closest('div')?.querySelector('label')?.textContent).toBe('Notes');
        });

        it('shows a select\'s labels, stores its values, and starts on the default', () => {
            renderModal();
            chooseTemplate('custom_webhook');

            const mode = screen.getByLabelText('Reports');
            expect(within(mode).getAllByRole('option').map(option => [option.textContent, option.getAttribute('value')])).toEqual([
                ['One file', 'file'],
                ['A whole folder', 'folder']
            ]);
            expect(mode).toHaveValue('file');
        });

        it('shows a field only while the setting it depends on has that value', () => {
            renderModal();
            chooseTemplate('custom_webhook');
            expect(screen.queryByLabelText(/^Folder depth/)).not.toBeInTheDocument();

            fireEvent.change(screen.getByLabelText('Reports'), { target: { value: 'folder' } });
            expect(screen.getByLabelText(/^Folder depth/)).toHaveValue('1');

            fireEvent.change(screen.getByLabelText('Reports'), { target: { value: 'file' } });
            expect(screen.queryByLabelText(/^Folder depth/)).not.toBeInTheDocument();
        });

        it('holds Save back for a required field only while it is shown', async () => {
            renderModal();
            chooseTemplate('custom_webhook');
            fireEvent.change(screen.getByLabelText(/^Path field/), { target: { value: 'file.path' } });
            const library = screen.getByLabelText(/^Library/);
            await within(library).findByRole('option', { name: 'Movies' });
            fireEvent.change(library, { target: { value: 'lib1' } });
            fireEvent.change(screen.getByLabelText('Reports'), { target: { value: 'folder' } });
            fireEvent.change(screen.getByLabelText(/^Folder depth/), { target: { value: '' } });
            expect(save()).toBeDisabled();

            fireEvent.change(screen.getByLabelText('Reports'), { target: { value: 'file' } });

            expect(save()).toBeEnabled();
        });

        it('fills the library dropdown from the server\'s libraries and stores the library id', async () => {
            renderModal();
            expect(mocks.getLibraries).not.toHaveBeenCalled();
            chooseTemplate('custom_webhook');

            const library = screen.getByLabelText(/^Library/);
            await within(library).findByRole('option', { name: 'Kids movies' });
            expect(mocks.getLibraries).toHaveBeenCalledWith(undefined);
            expect(within(library).getAllByRole('option').map(option => option.textContent)).toEqual(['Choose a library…', 'Movies', 'Kids movies']);

            fireEvent.change(library, { target: { value: 'lib2' } });
            expect(library).toHaveValue('lib2');
        });

        it('says so when the libraries cannot be loaded', async () => {
            mocks.getLibraries.mockRejectedValue(new Error('down'));
            renderModal();
            chooseTemplate('custom_webhook');

            expect(await screen.findByText('The libraries could not be loaded.')).toBeInTheDocument();
        });

        it('shows the stored values of the connection being edited', async () => {
            const connection = makeConnection({
                providerId: 'custom_webhook',
                providerName: 'Custom',
                settings: { pathField: 'movie.file', mode: 'folder', folderDepth: '3', library: 'lib2', notes: 'hello' }
            });
            renderModal({ connection });

            expect(screen.getByLabelText(/^Path field/)).toHaveValue('movie.file');
            expect(screen.getByLabelText('Reports')).toHaveValue('folder');
            expect(screen.getByLabelText(/^Folder depth/)).toHaveValue('3');
            expect(screen.getByLabelText('Notes')).toHaveValue('hello');
            await waitFor(() => expect(screen.getByLabelText(/^Library/)).toHaveValue('lib2'));
        });
    });

    describe('trying a sample payload', () => {
        const preview = (overrides: Partial<WebhookPreviewResult> = {}): WebhookPreviewResult => ({
            ok: true,
            error: null,
            event: 'Download',
            isTest: false,
            instance: '4K',
            changes: [],
            ...overrides
        });

        it('cannot be run until something is pasted', () => {
            renderModal({ connection: makeConnection() });

            expect(screen.getByRole('button', { name: 'Run' })).toBeDisabled();

            fireEvent.change(screen.getByLabelText('Sample payload'), { target: { value: '{}' } });
            expect(screen.getByRole('button', { name: 'Run' })).toBeEnabled();
        });

        it('sends the values on screen, saved or not, and lists the event and each change', async () => {
            mocks.preview.mockResolvedValue(preview({
                changes: [
                    { kind: 'Added', reportedPath: '/movies/Heat/Heat.mkv', mappedPath: '/media/movies/Heat/Heat.mkv', libraryName: 'Movies', existsOnDisk: true, note: '' },
                    { kind: 'Removed', reportedPath: '/movies/Old/Old.mkv', mappedPath: null, libraryName: null, existsOnDisk: false, note: 'No library folder matched, add a path mapping.' },
                    { kind: 'FolderRemoved', reportedPath: '/movies/Gone', mappedPath: null, libraryName: null, existsOnDisk: null, note: '' }
                ]
            }));
            renderModal({ connection: makeConnection() });
            fireEvent.change(mappingsInput(), { target: { value: '/a => /b' } });
            fireEvent.change(screen.getByLabelText('Sample payload'), { target: { value: '{"eventType":"Download"}' } });

            fireEvent.click(screen.getByRole('button', { name: 'Run' }));

            expect(await screen.findByText('Event: Download')).toBeInTheDocument();
            expect(mocks.preview).toHaveBeenCalledWith({
                providerId: 'radarr_webhook',
                settings: {},
                pathMappings: '/a => /b',
                body: '{"eventType":"Download"}'
            }, undefined);
            expect(screen.getByText(/instance 4K/)).toBeInTheDocument();

            const changes = screen.getAllByRole('listitem').filter(item => item.textContent?.includes('/movies/'));
            expect(changes).toHaveLength(3);
            expect(changes[0]).toHaveTextContent('Added');
            expect(changes[0]).toHaveTextContent('/movies/Heat/Heat.mkv');
            expect(changes[0]).toHaveTextContent('Maps to /media/movies/Heat/Heat.mkv');
            expect(changes[0]).toHaveTextContent('Library: Movies');
            expect(changes[0]).toHaveTextContent('On disk: yes');
            expect(changes[1]).toHaveTextContent('Removed');
            expect(changes[1]).not.toHaveTextContent('Maps to');
            expect(changes[1]).toHaveTextContent('No library folder matched, add a path mapping.');
            expect(changes[1]).toHaveTextContent('On disk: no');
            expect(changes[2]).toHaveTextContent('Folder removed');
            expect(changes[2]).not.toHaveTextContent('On disk');
        });

        it('uses the unsaved settings of the template being added', async () => {
            mocks.preview.mockResolvedValue(preview());
            renderModal();
            chooseTemplate('custom_webhook');
            fireEvent.change(screen.getByLabelText(/^Path field/), { target: { value: 'file.path' } });
            fireEvent.change(screen.getByLabelText('Sample payload'), { target: { value: '{}' } });

            fireEvent.click(screen.getByRole('button', { name: 'Run' }));

            await waitFor(() => expect(mocks.preview).toHaveBeenCalledTimes(1));
            expect(mocks.preview.mock.calls[0][0]).toMatchObject({
                providerId: 'custom_webhook',
                settings: { pathField: 'file.path', mode: 'file', folderDepth: '1', library: '', notes: '' }
            });
        });

        it('says when it was a test and when nothing would change', async () => {
            mocks.preview.mockResolvedValue(preview({ event: 'Test', isTest: true, instance: null }));
            renderModal({ connection: makeConnection() });
            fireEvent.change(screen.getByLabelText('Sample payload'), { target: { value: '{}' } });

            fireEvent.click(screen.getByRole('button', { name: 'Run' }));

            expect(await screen.findByText('Event: Test')).toBeInTheDocument();
            expect(screen.getByRole('status')).toHaveTextContent('(a test)');
            expect(screen.getByText('No file changes. Nothing would be queued.')).toBeInTheDocument();
        });

        it('shows the error when the sample is not understood', async () => {
            mocks.preview.mockResolvedValue(preview({ ok: false, error: 'That is not a payload Radarr sends.', event: '', instance: null }));
            renderModal({ connection: makeConnection() });
            fireEvent.change(screen.getByLabelText('Sample payload'), { target: { value: 'nope' } });

            fireEvent.click(screen.getByRole('button', { name: 'Run' }));

            expect(await screen.findByRole('alert')).toHaveTextContent('That is not a payload Radarr sends.');
        });

        it('shows why the dry run could not happen', async () => {
            mocks.preview.mockRejectedValue(failure(400, { error: 'The path mappings are not valid.' }));
            renderModal({ connection: makeConnection() });
            fireEvent.change(screen.getByLabelText('Sample payload'), { target: { value: '{}' } });

            fireEvent.click(screen.getByRole('button', { name: 'Run' }));

            expect(await screen.findByRole('alert')).toHaveTextContent('The path mappings are not valid.');
        });
    });

    describe('on a phone and with a keyboard', () => {
        it('scrolls inside the screen instead of running off it', () => {
            renderModal({ connection: makeConnection() });

            const dialog = screen.getByRole('dialog');
            expect(dialog.className).toContain('max-h-[90vh]');
            expect(dialog.className).toContain('max-md:max-h-[calc(100dvh-2rem)]');
            expect(dialog.querySelector('.overflow-y-auto')?.className).toContain('max-md:p-4');
            expect(screen.getByRole('button', { name: 'Save' }).parentElement?.parentElement?.className).toContain('flex-wrap');
        });

        it('fills every button and switch with the accent on focus, never a ring, and leaves dropdowns plain', async () => {
            renderModal();
            await screen.findByLabelText('Fill in from a Request Server');
            chooseTemplate('custom_webhook');
            await within(screen.getByLabelText(/^Library/)).findByRole('option', { name: 'Movies' });

            const dialog = screen.getByRole('dialog');
            const controls = [
                ...within(dialog).getAllByRole('button'),
                ...within(dialog).getAllByRole('switch')
            ];
            expect(controls.length).toBeGreaterThanOrEqual(6);
            for (const control of controls) {
                const expected = control === save() ? FOCUS_FILL_PRIMARY : FOCUS_FILL;
                for (const cls of expected) expect(control.className).toContain(cls);
            }

            const dropdowns = within(dialog).getAllByRole('combobox');
            expect(dropdowns.length).toBeGreaterThanOrEqual(2);
            for (const dropdown of dropdowns) {
                expect(dropdown.className).toContain('vora-input');
                expect(dropdown.className).not.toContain('accent');
            }
        });

        it('does the same for the secret and delete buttons of a saved connection', async () => {
            renderModal({ connection: makeConnection() });
            await waitFor(() => expect(secretInput()).toHaveValue(FAKE_SECRET));

            const dialog = screen.getByRole('dialog');
            const buttons = within(dialog).getAllByRole('button');
            expect(buttons.map(button => button.getAttribute('aria-label') ?? button.textContent)).toEqual(expect.arrayContaining([
                'Close', 'Copy address', 'Reveal secret', 'Copy secret', 'Regenerate secret', 'Delete connection', 'Cancel', 'Run'
            ]));
            for (const button of buttons) {
                const expected = button === save() ? FOCUS_FILL_PRIMARY : FOCUS_FILL;
                for (const cls of expected) expect(button.className).toContain(cls);
            }
        });

        it('wraps the address and secret rows on a phone', async () => {
            renderModal({ connection: makeConnection() });
            await waitFor(() => expect(secretInput()).toHaveValue(FAKE_SECRET));

            expect(screen.getByLabelText('Address to paste into Radarr').parentElement?.className).toContain('max-md:flex-wrap');
            expect(secretInput().parentElement?.className).toContain('max-md:flex-wrap');
        });
    });
});
