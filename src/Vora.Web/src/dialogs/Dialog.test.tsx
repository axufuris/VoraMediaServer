import { describe, it, expect } from 'vitest';
import { render, screen, fireEvent, act } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { DialogProvider } from './Dialog';
import { useDialog } from './useDialog';

function AlertButton({ message = 'You clicked' }: { message?: string }) {
    const dialog = useDialog();
    return <button onClick={() => dialog.alert(message)}>Open Alert</button>;
}

function ConfirmButton({ onResult }: { onResult: (v: boolean) => void }) {
    const dialog = useDialog();
    return (
        <button
            onClick={async () => {
                const v = await dialog.confirm('Delete it?');
                onResult(v);
            }}
        >
            Open Confirm
        </button>
    );
}

function PromptButton({ onResult }: { onResult: (v: string | null) => void }) {
    const dialog = useDialog();
    return (
        <button
            onClick={async () => {
                const v = await dialog.prompt({ message: 'Enter:', defaultValue: 'preset' });
                onResult(v);
            }}
        >
            Open Prompt
        </button>
    );
}

describe('Dialog system', () => {
    it('alert renders message and resolves on confirm click', async () => {
        let resolved = false;

        function Inner() {
            const dialog = useDialog();
            return (
                <button
                    onClick={async () => {
                        await dialog.alert('Hello');
                        resolved = true;
                    }}
                >
                    Trigger
                </button>
            );
        }

        render(
            <DialogProvider>
                <Inner />
            </DialogProvider>
        );

        await userEvent.click(screen.getByText('Trigger'));
        expect(screen.getByText('Hello')).toBeInTheDocument();
        await userEvent.click(screen.getByText('OK'));
        // Allow promise microtask to flush
        await act(async () => {});
        expect(resolved).toBe(true);
        expect(screen.queryByText('Hello')).toBeNull();
    });

    it('alert from string normalizes to message', async () => {
        render(
            <DialogProvider>
                <AlertButton message="Just a string" />
            </DialogProvider>
        );
        await userEvent.click(screen.getByText('Open Alert'));
        expect(screen.getByText('Just a string')).toBeInTheDocument();
    });

    it('confirm resolves true on confirm and false on cancel', async () => {
        const results: boolean[] = [];

        render(
            <DialogProvider>
                <ConfirmButton onResult={(v) => results.push(v)} />
            </DialogProvider>
        );

        await userEvent.click(screen.getByText('Open Confirm'));
        await userEvent.click(screen.getByText('Confirm'));
        await act(async () => {});
        expect(results).toEqual([true]);

        await userEvent.click(screen.getByText('Open Confirm'));
        await userEvent.click(screen.getByText('Cancel'));
        await act(async () => {});
        expect(results).toEqual([true, false]);
    });

    it('prompt resolves with value on confirm and null on cancel', async () => {
        const results: (string | null)[] = [];

        render(
            <DialogProvider>
                <PromptButton onResult={(v) => results.push(v)} />
            </DialogProvider>
        );

        await userEvent.click(screen.getByText('Open Prompt'));
        const input = screen.getByDisplayValue('preset') as HTMLInputElement;
        await userEvent.clear(input);
        await userEvent.type(input, 'typed-value');
        await userEvent.click(screen.getByText('Confirm'));
        await act(async () => {});
        expect(results).toEqual(['typed-value']);

        await userEvent.click(screen.getByText('Open Prompt'));
        await userEvent.click(screen.getByText('Cancel'));
        await act(async () => {});
        expect(results).toEqual(['typed-value', null]);
    });

    it('Escape key cancels the top dialog', async () => {
        const results: boolean[] = [];

        render(
            <DialogProvider>
                <ConfirmButton onResult={(v) => results.push(v)} />
            </DialogProvider>
        );

        await userEvent.click(screen.getByText('Open Confirm'));
        fireEvent.keyDown(window, { key: 'Escape' });
        await act(async () => {});
        expect(results).toEqual([false]);
    });

    it('Enter key confirms non-prompt dialogs', async () => {
        const results: boolean[] = [];

        render(
            <DialogProvider>
                <ConfirmButton onResult={(v) => results.push(v)} />
            </DialogProvider>
        );

        await userEvent.click(screen.getByText('Open Confirm'));
        fireEvent.keyDown(window, { key: 'Enter' });
        await act(async () => {});
        expect(results).toEqual([true]);
    });

    it('Backdrop click cancels the dialog', async () => {
        const results: boolean[] = [];

        render(
            <DialogProvider>
                <ConfirmButton onResult={(v) => results.push(v)} />
            </DialogProvider>
        );

        await userEvent.click(screen.getByText('Open Confirm'));
        const backdrop = screen.getByRole('dialog');
        fireEvent.click(backdrop);
        await act(async () => {});
        expect(results).toEqual([false]);
    });

    it('useDialog throws when used outside DialogProvider', () => {
        // Suppress error output from the boundary
        const spy = console.error;
        console.error = () => {};
        try {
            expect(() => render(<AlertButton />)).toThrow(/DialogProvider/);
        } finally {
            console.error = spy;
        }
    });

    it('renders custom confirmText and cancelText', async () => {
        function Custom() {
            const dialog = useDialog();
            return (
                <button onClick={() => dialog.confirm({ message: 'x', confirmText: 'Yes please', cancelText: 'No thanks' })}>
                    Trigger
                </button>
            );
        }

        render(
            <DialogProvider>
                <Custom />
            </DialogProvider>
        );

        await userEvent.click(screen.getByText('Trigger'));
        expect(screen.getByText('Yes please')).toBeInTheDocument();
        expect(screen.getByText('No thanks')).toBeInTheDocument();
    });
});
