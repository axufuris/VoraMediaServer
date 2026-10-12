import { afterEach, describe, expect, it, vi } from 'vitest';
import { copyText } from './copyText';

const setClipboard = (clipboard: Pick<Clipboard, 'writeText'> | undefined) =>
    Object.defineProperty(navigator, 'clipboard', { value: clipboard, configurable: true });

const stubSelectionCopy = (result: boolean | Error) => {
    const copy = vi.fn(() => {
        if (result instanceof Error) throw result;
        return result;
    });
    Object.defineProperty(document, 'execCommand', { value: copy, configurable: true });
    return copy;
};

describe('copyText', () => {
    afterEach(() => {
        setClipboard(undefined);
        vi.restoreAllMocks();
    });

    it('uses the clipboard when the page has one', async () => {
        const writeText = vi.fn().mockResolvedValue(undefined);
        setClipboard({ writeText });
        const selectionCopy = stubSelectionCopy(true);

        expect(await copyText('https://vora.example/register')).toBe(true);
        expect(writeText).toHaveBeenCalledWith('https://vora.example/register');
        expect(selectionCopy).not.toHaveBeenCalled();
    });

    it('copies a selection on a plain http page, where there is no clipboard', async () => {
        setClipboard(undefined);
        const selectionCopy = stubSelectionCopy(true);

        expect(await copyText('Top Gear')).toBe(true);
        expect(selectionCopy).toHaveBeenCalledWith('copy');
        expect(document.querySelector('textarea')).toBeNull();
    });

    it('copies a selection when the clipboard refuses', async () => {
        setClipboard({ writeText: vi.fn().mockRejectedValue(new Error('NotAllowedError')) });
        const selectionCopy = stubSelectionCopy(true);

        expect(await copyText('Top Gear')).toBe(true);
        expect(selectionCopy).toHaveBeenCalledWith('copy');
    });

    it('reports failure when nothing can copy', async () => {
        setClipboard(undefined);
        stubSelectionCopy(new Error('unsupported'));

        expect(await copyText('Top Gear')).toBe(false);
        expect(document.querySelector('textarea')).toBeNull();
    });
});
