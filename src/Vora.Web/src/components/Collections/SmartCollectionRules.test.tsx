import { describe, it, expect, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { useState } from 'react';
import SmartCollectionRules from './SmartCollectionRules';
import type { SmartPlaylistRuleGroup } from '../../api/Music/smartPlaylistService';
import type { SmartCollectionMedia } from './smartCollectionMedia';
import { SMART_COLLECTION_TEMPLATES, SMART_TEMPLATE_GROUPS, templatesInGroup } from './smartCollectionTemplates';

const mocks = vi.hoisted(() => ({
    confirm: vi.fn(),
    onRootChange: vi.fn(),
}));

vi.mock('../../dialogs', () => ({
    useDialog: () => ({ alert: vi.fn(), confirm: mocks.confirm, prompt: vi.fn() }),
}));

const EMPTY: SmartPlaylistRuleGroup = { match: 'All', rules: [], groups: [] };
const HAS_RULES: SmartPlaylistRuleGroup = { match: 'All', rules: [{ field: 'Title', operator: 'Contains', value: 'Alien' }], groups: [] };

function Harness({ initial = EMPTY, media = 'Movies', isGlobal = false }: { initial?: SmartPlaylistRuleGroup; media?: SmartCollectionMedia; isGlobal?: boolean }) {
    const [root, setRoot] = useState(initial);
    return (
        <SmartCollectionRules
            idPrefix="test"
            isGlobal={isGlobal}
            media={media}
            onMediaChange={() => {}}
            root={root}
            onRootChange={next => { mocks.onRootChange(next); setRoot(next); }}
        />
    );
}

const picker = () => screen.getByLabelText('Template') as HTMLSelectElement;
const choose = (id: string) => fireEvent.change(picker(), { target: { value: id } });

describe('SmartCollectionRules templates', () => {
    beforeEach(() => {
        Object.values(mocks).forEach(m => m.mockReset());
        mocks.confirm.mockResolvedValue(true);
    });

    it('offers a template for every Vora poster, grouped the way the posters are', () => {
        render(<Harness />);

        const groups = Array.from(picker().querySelectorAll('optgroup'));
        expect(groups.map(g => g.label)).toEqual(SMART_TEMPLATE_GROUPS);
        for (const group of groups) {
            expect(within(group as HTMLElement).getAllByRole('option').map(o => (o as HTMLOptionElement).value))
                .toEqual(templatesInGroup(group.label as never).map(t => t.id));
        }
        expect(picker().querySelectorAll('optgroup option')).toHaveLength(SMART_COLLECTION_TEMPLATES.length);
        expect(SMART_COLLECTION_TEMPLATES).toHaveLength(75);
    });

    it('starts on its prompt, not on a template', () => {
        render(<Harness />);

        expect(picker().value).toBe('');
        expect(within(picker()).getByRole('option', { name: 'Start from a template…' })).toBeInTheDocument();
    });

    it('fills the rules from the template that is picked', async () => {
        render(<Harness />);

        choose('horror');

        await waitFor(() => expect(mocks.onRootChange).toHaveBeenCalledTimes(1));
        expect(mocks.onRootChange).toHaveBeenCalledWith({ match: 'Any', groups: [], rules: [{ field: 'Genre', operator: 'Contains', value: 'Horror' }] });
        expect(await screen.findByDisplayValue('Horror')).toBeInTheDocument();
        expect(mocks.confirm).not.toHaveBeenCalled();
    });

    it('goes back to the prompt after a pick, so the same template can be picked again', async () => {
        render(<Harness />);

        choose('horror');

        await screen.findByDisplayValue('Horror');
        expect(picker().value).toBe('');
    });

    it('fills a decade as a year range', async () => {
        render(<Harness />);

        choose('1980s');

        await waitFor(() => expect(mocks.onRootChange).toHaveBeenCalled());
        expect(mocks.onRootChange.mock.calls[0][0]).toEqual({
            match: 'All', groups: [], rules: [{ field: 'ReleaseYear', operator: 'Between', value: '1980', secondValue: '1989' }],
        });
    });

    it('fills nested rules, for a template with a group inside', async () => {
        render(<Harness />);

        choose('blockbusters');

        await waitFor(() => expect(mocks.onRootChange).toHaveBeenCalled());
        const root = mocks.onRootChange.mock.calls[0][0] as SmartPlaylistRuleGroup;
        expect(root.rules).toHaveLength(1);
        expect(root.groups[0].match).toBe('Any');
        expect(root.groups[0].rules.map(r => r.value)).toEqual(['Action', 'Adventure', 'Science Fiction', 'Fantasy', 'Animation']);
    });

    it('puts the new year range in for New Releases, as of today', async () => {
        render(<Harness />);

        choose('new-releases');

        await waitFor(() => expect(mocks.onRootChange).toHaveBeenCalled());
        const year = new Date().getFullYear();
        expect(mocks.onRootChange.mock.calls[0][0].rules).toEqual([
            { field: 'ReleaseYear', operator: 'Between', value: String(year - 1), secondValue: String(year) },
        ]);
    });

    it('asks before it replaces rules that are already there, and replaces them on yes', async () => {
        render(<Harness initial={HAS_RULES} />);

        choose('comedy');

        await waitFor(() => expect(mocks.confirm).toHaveBeenCalled());
        expect(mocks.confirm.mock.calls[0][0]).toMatchObject({ title: 'Replace the rules?', confirmText: 'Replace' });
        expect(mocks.confirm.mock.calls[0][0].message).toContain('Comedy');
        await waitFor(() => expect(mocks.onRootChange).toHaveBeenCalledTimes(1));
        expect(mocks.onRootChange.mock.calls[0][0].rules).toEqual([{ field: 'Genre', operator: 'Contains', value: 'Comedy' }]);
    });

    it('keeps the rules that are there when the admin says no', async () => {
        mocks.confirm.mockResolvedValue(false);
        render(<Harness initial={HAS_RULES} />);

        choose('comedy');

        await waitFor(() => expect(mocks.confirm).toHaveBeenCalled());
        expect(mocks.onRootChange).not.toHaveBeenCalled();
        expect(screen.getByDisplayValue('Alien')).toBeInTheDocument();
    });

    it('asks before replacing rules that are only a group', async () => {
        render(<Harness initial={{ match: 'All', rules: [], groups: [{ match: 'Any', rules: [], groups: [] }] }} />);

        choose('comedy');

        await waitFor(() => expect(mocks.confirm).toHaveBeenCalled());
    });

    it('shows the note for a template that needs one, and drops it for one that does not', async () => {
        render(<Harness />);

        choose('christmas');
        expect(await screen.findByText(/set the List Provider to AI Theme/)).toBeInTheDocument();

        choose('comedy');
        await waitFor(() => expect(screen.queryByText(/AI Theme/)).not.toBeInTheDocument());
    });

    it('says what a template stands in for when Vora has no data for it', async () => {
        render(<Harness />);

        choose('award-winners');

        expect(await screen.findByText(/Vora has no award data/)).toBeInTheDocument();
    });

    it('is there for a global collection too, whatever it matches', async () => {
        render(<Harness isGlobal media="Shows" />);

        choose('drama');

        await waitFor(() => expect(mocks.onRootChange).toHaveBeenCalled());
        expect(screen.getByLabelText('Match')).toBeInTheDocument();
        expect(picker()).toBeInTheDocument();
    });

    it('ignores a value that is not a template', () => {
        render(<Harness />);

        choose('');

        expect(mocks.onRootChange).not.toHaveBeenCalled();
    });
});
