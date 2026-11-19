import { describe, it, expect } from 'vitest';
import { AxiosError, type AxiosResponse } from 'axios';
import type { WebhookSettingDefinition } from '../../../api/System/webhookAdminService';
import { changeKindLabel, hasMissingRequired, initialSettings, isSettingVisible, parseOption, setupSteps, webhookErrorMessage } from './connectionForm';

const definition = (overrides: Partial<WebhookSettingDefinition>): WebhookSettingDefinition => ({
    key: 'k',
    label: 'K',
    type: 'text',
    defaultValue: '',
    description: '',
    placeholder: '',
    required: false,
    options: [],
    showWhen: '',
    ...overrides
});

const failure = (status: number, data: object | string) =>
    new AxiosError('Request failed', String(status), undefined, undefined, { status, data } as AxiosResponse);

describe('parseOption', () => {
    it('keeps a plain option as both value and label', () => {
        expect(parseOption('Movie')).toEqual({ value: 'Movie', label: 'Movie' });
    });

    it('splits value=Label on the first equals sign', () => {
        expect(parseOption('TvShow=TV shows')).toEqual({ value: 'TvShow', label: 'TV shows' });
        expect(parseOption('a=b=c')).toEqual({ value: 'a', label: 'b=c' });
    });
});

describe('isSettingVisible', () => {
    it('shows a setting with no rule', () => {
        expect(isSettingVisible(definition({ showWhen: '' }), {})).toBe(true);
    });

    it('shows it only while the other setting has that value', () => {
        const rule = definition({ showWhen: 'mode=folder' });
        expect(isSettingVisible(rule, { mode: 'folder' })).toBe(true);
        expect(isSettingVisible(rule, { mode: 'file' })).toBe(false);
        expect(isSettingVisible(rule, {})).toBe(false);
    });

    it('shows a setting whose rule it cannot read', () => {
        expect(isSettingVisible(definition({ showWhen: 'nonsense' }), {})).toBe(true);
    });
});

describe('initialSettings', () => {
    const definitions = [definition({ key: 'a', defaultValue: 'x' }), definition({ key: 'b' })];

    it('uses each default when creating', () => {
        expect(initialSettings(definitions)).toEqual({ a: 'x', b: '' });
    });

    it('keeps what is stored, including an empty value, and fills what is missing', () => {
        expect(initialSettings(definitions, { a: '' })).toEqual({ a: '', b: '' });
        expect(initialSettings(definitions, { b: 'saved' })).toEqual({ a: 'x', b: 'saved' });
    });
});

describe('hasMissingRequired', () => {
    const definitions = [
        definition({ key: 'path', required: true }),
        definition({ key: 'depth', required: true, showWhen: 'mode=folder' }),
        definition({ key: 'mode' })
    ];

    it('is true while a visible required setting is empty or blank', () => {
        expect(hasMissingRequired(definitions, { path: '', mode: 'file' })).toBe(true);
        expect(hasMissingRequired(definitions, { path: '   ', mode: 'file' })).toBe(true);
    });

    it('ignores a required setting that is hidden', () => {
        expect(hasMissingRequired(definitions, { path: 'p', mode: 'file', depth: '' })).toBe(false);
    });

    it('counts a required setting once it is shown', () => {
        expect(hasMissingRequired(definitions, { path: 'p', mode: 'folder', depth: '' })).toBe(true);
        expect(hasMissingRequired(definitions, { path: 'p', mode: 'folder', depth: '2' })).toBe(false);
    });
});

describe('setupSteps', () => {
    it('splits on new lines and drops blank ones', () => {
        expect(setupSteps('One\n\n  Two  \r\nThree\n')).toEqual(['One', 'Two', 'Three']);
        expect(setupSteps('')).toEqual([]);
    });
});

describe('webhookErrorMessage', () => {
    it('shows the message of a 400 the server explained', () => {
        expect(webhookErrorMessage(failure(400, { error: 'A connection with that name exists.' }), 'Could not save.')).toBe('A connection with that name exists.');
    });

    it('falls back to the reason the API gave, then to the plain sentence', () => {
        expect(webhookErrorMessage(failure(500, { title: 'Server error', detail: 'The database is down.' }), 'Could not save.')).toBe('Could not save. The database is down.');
        expect(webhookErrorMessage(failure(404, {}), 'Could not save.')).toBe('Could not save.');
    });
});

describe('changeKindLabel', () => {
    it('names each kind of change', () => {
        expect(changeKindLabel('Added')).toBe('Added');
        expect(changeKindLabel('Removed')).toBe('Removed');
        expect(changeKindLabel('FolderRemoved')).toBe('Folder removed');
        expect(changeKindLabel('ScanLibrary')).toBe('Scan library');
    });
});
