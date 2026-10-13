import { describe, it, expect, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import AiSettingsForm from './AiSettingsForm';
import { aiFields, OPENAI_DEFAULTS } from './aiTestFields';

const mocks = vi.hoisted(() => ({
    getPluginSettings: vi.fn(),
    updatePluginSettings: vi.fn(),
    testPluginConnection: vi.fn(),
    listModels: vi.fn(),
    triggerAiTask: vi.fn(),
    confirm: vi.fn(),
    alert: vi.fn(),
}));

vi.mock('../../../api/System/systemSettingsAdminService', () => ({
    systemSettingsAdminService: {
        getPluginSettings: mocks.getPluginSettings,
        updatePluginSettings: mocks.updatePluginSettings,
        testPluginConnection: mocks.testPluginConnection,
    },
}));
vi.mock('../../../api/System/aiSettingsService', () => ({ aiSettingsService: { listModels: mocks.listModels } }));
vi.mock('../../../api/System/aiStatsService', () => ({ aiStatsService: { triggerAiTask: mocks.triggerAiTask } }));
vi.mock('../../../dialogs', () => ({ useDialog: () => ({ confirm: mocks.confirm, alert: mocks.alert }) }));

const OLLAMA = 'http://host.docker.internal:11434/v1';

const renderForm = async (saved: Record<string, string> = {}) => {
    mocks.getPluginSettings.mockResolvedValue(aiFields(saved));
    const onSaved = vi.fn();
    render(<AiSettingsForm idPrefix="test-ai" onSaved={onSaved} />);
    await screen.findByLabelText('Server address');
    return onSaved;
};

const save = () => fireEvent.click(screen.getByRole('button', { name: 'Save settings' }));

describe('AiSettingsForm', () => {
    beforeEach(() => {
        Object.values(mocks).forEach(m => m.mockReset());
        mocks.updatePluginSettings.mockResolvedValue(undefined);
        mocks.triggerAiTask.mockResolvedValue(undefined);
        mocks.alert.mockResolvedValue(undefined);
        mocks.listModels.mockResolvedValue({ chatModels: [], embeddingModels: [], error: null });
    });

    it('shows a server that never saved these settings as OpenAI, on the models it runs on', async () => {
        await renderForm();

        expect(screen.getByLabelText('Server address')).toHaveValue('https://api.openai.com/v1');
        expect(screen.getByRole('button', { name: 'OpenAI' })).toHaveAttribute('aria-pressed', 'true');
        expect(screen.getByRole('combobox', { name: 'Recommendations' })).toHaveValue('gpt-4o-mini');
        expect(screen.getByRole('combobox', { name: 'Collections and story order' })).toHaveValue('gpt-5');
        expect(screen.getByRole('combobox', { name: 'AI playlists' })).toHaveValue('');
        expect(screen.getByRole('combobox', { name: 'Embeddings' })).toHaveValue('text-embedding-3-small');
        expect(mocks.getPluginSettings).toHaveBeenCalledWith('openai_recommendations', undefined);
        expect(mocks.listModels).not.toHaveBeenCalled();
    });

    it('saves a server already on OpenAI what it was shown, and has nothing to read again', async () => {
        const onSaved = await renderForm({ api_key: 'sk-test' });

        save();

        await waitFor(() => expect(mocks.updatePluginSettings).toHaveBeenCalledWith('openai_recommendations', { ...OPENAI_DEFAULTS, api_key: 'sk-test' }, undefined));
        expect(await screen.findByText('Saved.')).toBeInTheDocument();
        expect(onSaved).toHaveBeenCalled();
        expect(mocks.confirm).not.toHaveBeenCalled();
    });

    it('asks a server that can answer for its models as soon as the form opens', async () => {
        mocks.listModels.mockResolvedValue({ chatModels: ['gpt-4o-mini', 'gpt-5', 'gpt-6'], embeddingModels: ['text-embedding-3-small', 'text-embedding-3-large'], error: null });

        await renderForm({ api_key: 'sk-test' });

        expect(await screen.findByRole('option', { name: 'text-embedding-3-large' })).toBeInTheDocument();
        expect(mocks.listModels).toHaveBeenCalledWith(expect.objectContaining({ api_key: 'sk-test' }), undefined);
        expect(screen.getByText('5 models found on the server.')).toBeInTheDocument();
    });

    it('will not save OpenAI without a key', async () => {
        await renderForm();

        save();

        expect(await screen.findByRole('alert')).toHaveTextContent('Paste the API key first.');
        expect(mocks.updatePluginSettings).not.toHaveBeenCalled();
    });

    it("moves to Ollama: fills in its address, drops OpenAI's model names and asks for one", async () => {
        await renderForm({ api_key: 'sk-test' });

        fireEvent.click(screen.getByRole('button', { name: 'Ollama' }));

        expect(screen.getByLabelText('Server address')).toHaveValue(OLLAMA);
        expect(screen.getByLabelText('API key')).toHaveValue('');
        expect(screen.getByRole('textbox', { name: 'Recommendations' })).toHaveValue('');
        expect(screen.getByRole('textbox', { name: 'Collections and story order' })).toHaveValue('');
        expect(screen.getByRole('textbox', { name: 'Embeddings' })).toHaveValue('');
        expect(screen.getByText(/"localhost" is the Vora container/)).toBeInTheDocument();

        save();

        expect(await screen.findByRole('alert')).toHaveTextContent('Choose a model for recommendations first.');
        expect(mocks.updatePluginSettings).not.toHaveBeenCalled();
    });

    it('offers the models the server lists, saves the ones picked and reads the library again on request', async () => {
        await renderForm({ api_key: 'sk-test' });
        mocks.listModels.mockResolvedValue({ chatModels: ['llama3.1', 'qwen3:14b'], embeddingModels: ['nomic-embed-text'], error: null });
        mocks.confirm.mockResolvedValue(true);

        fireEvent.click(screen.getByRole('button', { name: 'Ollama' }));
        fireEvent.click(screen.getByRole('button', { name: 'Load models from the server' }));

        expect(await screen.findByText('3 models found on the server.')).toBeInTheDocument();
        expect(mocks.listModels).toHaveBeenLastCalledWith(expect.objectContaining({ base_url: OLLAMA, api_key: '' }), undefined);
        fireEvent.change(screen.getByRole('combobox', { name: 'Recommendations' }), { target: { value: 'llama3.1' } });
        fireEvent.change(screen.getByRole('combobox', { name: 'Embeddings' }), { target: { value: 'nomic-embed-text' } });
        save();

        await waitFor(() => expect(mocks.updatePluginSettings).toHaveBeenCalledWith('openai_recommendations', {
            ...OPENAI_DEFAULTS,
            base_url: OLLAMA,
            api_key: '',
            chat_model: 'llama3.1',
            collections_model: '',
            music_playlists_model: '',
            embedding_model: 'nomic-embed-text',
        }, undefined));
        await waitFor(() => expect(mocks.triggerAiTask).toHaveBeenCalledWith(undefined));
        expect(mocks.confirm).toHaveBeenCalledWith(expect.objectContaining({
            title: 'Read the library again now?',
            message: expect.stringContaining('read by "nomic-embed-text"'),
        }));
        expect(await screen.findByText('Saved. Reading the library has started; follow it under Tasks.')).toBeInTheDocument();
    });

    it('leaves the library for the nightly run when asked to wait', async () => {
        mocks.listModels.mockResolvedValue({ chatModels: ['gpt-4o-mini', 'gpt-5'], embeddingModels: ['text-embedding-3-small', 'text-embedding-3-large'], error: null });
        mocks.confirm.mockResolvedValue(false);
        await renderForm({ api_key: 'sk-test' });
        await screen.findByRole('option', { name: 'text-embedding-3-large' });

        fireEvent.change(screen.getByRole('combobox', { name: 'Embeddings' }), { target: { value: 'text-embedding-3-large' } });
        save();

        await waitFor(() => expect(mocks.confirm).toHaveBeenCalled());
        expect(mocks.updatePluginSettings).toHaveBeenCalledWith('openai_recommendations', expect.objectContaining({ embedding_model: 'text-embedding-3-large' }), undefined);
        expect(mocks.triggerAiTask).not.toHaveBeenCalled();
        expect(screen.getByText('Saved.')).toBeInTheDocument();
    });

    it('does not ask about the library when only a chat model changed', async () => {
        await renderForm({ api_key: 'sk-test', chat_model: 'gpt-4.1-mini' });

        fireEvent.change(screen.getByLabelText('Monthly token limit'), { target: { value: '500000' } });
        save();

        await waitFor(() => expect(mocks.updatePluginSettings).toHaveBeenCalledWith('openai_recommendations', expect.objectContaining({ chat_model: 'gpt-4.1-mini', monthly_token_limit: '500000' }), undefined));
        expect(mocks.confirm).not.toHaveBeenCalled();
    });

    it('keeps the key and the chosen models when the choice already in use is picked again', async () => {
        await renderForm({ api_key: 'sk-test', chat_model: 'gpt-4.1-mini' });

        fireEvent.click(screen.getByRole('button', { name: 'OpenAI' }));

        expect(screen.getByLabelText('API key')).toHaveValue('sk-test');
        expect(screen.getByLabelText('Recommendations')).toHaveValue('gpt-4.1-mini');
    });

    it('never carries one service\'s key over to another', async () => {
        await renderForm({ api_key: 'sk-test' });

        fireEvent.click(screen.getByRole('button', { name: 'OpenRouter' }));

        expect(screen.getByLabelText('Server address')).toHaveValue('https://openrouter.ai/api/v1');
        expect(screen.getByLabelText('API key')).toHaveValue('');
    });

    it("puts OpenAI's models back when a server of your own is swapped for OpenAI", async () => {
        await renderForm({ base_url: OLLAMA, chat_model: 'llama3.1', collections_model: '', embedding_model: 'nomic-embed-text' });

        fireEvent.click(screen.getByRole('button', { name: 'OpenAI' }));

        expect(screen.getByLabelText('Server address')).toHaveValue('https://api.openai.com/v1');
        expect(screen.getByRole('combobox', { name: 'Recommendations' })).toHaveValue('gpt-4o-mini');
        expect(screen.getByRole('combobox', { name: 'Collections and story order' })).toHaveValue('gpt-5');
        expect(screen.getByRole('combobox', { name: 'Embeddings' })).toHaveValue('text-embedding-3-small');
    });

    it('clears the address for a server only the admin knows', async () => {
        await renderForm();

        fireEvent.click(screen.getByRole('button', { name: 'Another server' }));

        expect(screen.getByLabelText('Server address')).toHaveValue('');
        expect(screen.getByRole('textbox', { name: 'Recommendations' })).toHaveValue('');
    });

    it('says why the server could not list its models', async () => {
        await renderForm();
        mocks.listModels.mockResolvedValue({ chatModels: [], embeddingModels: [], error: "Couldn't reach the AI server at host.docker.internal:11434." });

        fireEvent.click(screen.getByRole('button', { name: 'Ollama' }));
        fireEvent.click(screen.getByRole('button', { name: 'Load models from the server' }));

        expect(await screen.findByRole('alert')).toHaveTextContent("Couldn't reach the AI server at host.docker.internal:11434.");
        expect(screen.getByRole('textbox', { name: 'Recommendations' })).toBeInTheDocument();
    });

    it('tests the connection with what is typed, before anything is saved', async () => {
        await renderForm();
        mocks.testPluginConnection.mockResolvedValue({ success: false, message: 'Connected, but the server at host.docker.internal:11434 has no model named "llama9".' });

        fireEvent.click(screen.getByRole('button', { name: 'Ollama' }));
        fireEvent.change(screen.getByRole('textbox', { name: 'Recommendations' }), { target: { value: 'llama9' } });
        fireEvent.click(screen.getByRole('button', { name: 'Test connection' }));

        expect(await screen.findByRole('alert')).toHaveTextContent('has no model named "llama9"');
        expect(mocks.testPluginConnection).toHaveBeenCalledWith('openai_recommendations', expect.objectContaining({ base_url: OLLAMA, chat_model: 'llama9' }), undefined);
        expect(mocks.updatePluginSettings).not.toHaveBeenCalled();
    });

    it('goes back to what is saved when the changes are discarded', async () => {
        await renderForm({ api_key: 'sk-test' });
        expect(screen.queryByRole('button', { name: 'Discard changes' })).not.toBeInTheDocument();

        fireEvent.click(screen.getByRole('button', { name: 'LM Studio' }));
        fireEvent.click(screen.getByRole('button', { name: 'Discard changes' }));

        expect(screen.getByLabelText('Server address')).toHaveValue('https://api.openai.com/v1');
        expect(screen.getByLabelText('API key')).toHaveValue('sk-test');
    });

    it('says so when the settings could not be saved', async () => {
        const onSaved = await renderForm({ api_key: 'sk-test' });
        mocks.updatePluginSettings.mockRejectedValue(new Error('offline'));

        save();

        expect(await screen.findByRole('alert')).toHaveTextContent("Couldn't save the AI settings.");
        expect(onSaved).not.toHaveBeenCalled();
    });

    it('says so when the settings cannot be loaded', async () => {
        mocks.getPluginSettings.mockRejectedValue(new Error('offline'));

        render(<AiSettingsForm idPrefix="test-ai" />);

        expect(await screen.findByRole('alert')).toHaveTextContent("Couldn't load the AI settings.");
    });
});
