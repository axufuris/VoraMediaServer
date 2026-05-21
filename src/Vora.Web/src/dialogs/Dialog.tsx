import { useCallback, useEffect, useRef, useState } from 'react';
import type { ReactNode } from 'react';
import { DialogContext, type AlertOptions, type ConfirmOptions, type PromptOptions, type DialogApi } from './useDialog';

type DialogState =
    | { kind: 'alert'; options: AlertOptions; resolve: () => void }
    | { kind: 'confirm'; options: ConfirmOptions; resolve: (value: boolean) => void }
    | { kind: 'prompt'; options: PromptOptions; resolve: (value: string | null) => void };

const normalizeAlert = (input: AlertOptions | string): AlertOptions =>
    typeof input === 'string' ? { message: input } : input;

const normalizeConfirm = (input: ConfirmOptions | string): ConfirmOptions =>
    typeof input === 'string' ? { message: input } : input;

const normalizePrompt = (input: PromptOptions | string): PromptOptions =>
    typeof input === 'string' ? { message: input } : input;

export function DialogProvider({ children }: { children: ReactNode }) {
    const [stack, setStack] = useState<DialogState[]>([]);

    const api: DialogApi = {
        alert: (input) => new Promise<void>((resolve) => {
            setStack((s) => [...s, { kind: 'alert', options: normalizeAlert(input), resolve }]);
        }),
        confirm: (input) => new Promise<boolean>((resolve) => {
            setStack((s) => [...s, { kind: 'confirm', options: normalizeConfirm(input), resolve }]);
        }),
        prompt: (input) => new Promise<string | null>((resolve) => {
            setStack((s) => [...s, { kind: 'prompt', options: normalizePrompt(input), resolve }]);
        })
    };

    const closeTop = useCallback(() => setStack((s) => s.slice(0, -1)), []);

    return (
        <DialogContext.Provider value={api}>
            {children}
            {stack.map((dialog, idx) => (
                <DialogShell key={idx} dialog={dialog} onClose={closeTop} isTop={idx === stack.length - 1} />
            ))}
        </DialogContext.Provider>
    );
}

interface DialogShellProps {
    dialog: DialogState;
    onClose: () => void;
    isTop: boolean;
}

function DialogShell({ dialog, onClose, isTop }: DialogShellProps) {
    const inputRef = useRef<HTMLInputElement | HTMLTextAreaElement>(null);
    const confirmButtonRef = useRef<HTMLButtonElement>(null);
    const [promptValue, setPromptValue] = useState(
        dialog.kind === 'prompt' ? (dialog.options.defaultValue ?? '') : ''
    );

    const tone = dialog.options.tone ?? 'default';
    const confirmClass =
        tone === 'danger' ? 'bg-red-600 hover:bg-red-500'
            : tone === 'success' ? 'bg-emerald-600 hover:bg-emerald-500'
                : 'bg-orange-600 hover:bg-orange-500';

    const handleCancel = useCallback(() => {
        if (dialog.kind === 'confirm') dialog.resolve(false);
        else if (dialog.kind === 'prompt') dialog.resolve(null);
        else dialog.resolve();
        onClose();
    }, [dialog, onClose]);

    const handleConfirm = useCallback(() => {
        if (dialog.kind === 'confirm') dialog.resolve(true);
        else if (dialog.kind === 'prompt') dialog.resolve(promptValue);
        else dialog.resolve();
        onClose();
    }, [dialog, promptValue, onClose]);

    useEffect(() => {
        if (!isTop) return;
        const handler = (e: KeyboardEvent) => {
            if (e.key === 'Escape') {
                e.preventDefault();
                handleCancel();
            } else if (e.key === 'Enter' && dialog.kind !== 'prompt') {
                e.preventDefault();
                handleConfirm();
            }
        };
        window.addEventListener('keydown', handler);
        return () => window.removeEventListener('keydown', handler);
    }, [isTop, dialog.kind, handleCancel, handleConfirm]);

    useEffect(() => {
        if (!isTop) return;
        if (dialog.kind === 'prompt') {
            inputRef.current?.focus();
            inputRef.current?.select();
        } else {
            confirmButtonRef.current?.focus();
        }
    }, [isTop, dialog.kind]);

    return (
        <div
            role="dialog"
            aria-modal="true"
            aria-label={dialog.options.title ?? 'Dialog'}
            className="fixed inset-0 z-[1000] flex items-center justify-center bg-black/60 p-4"
            onClick={(e) => { if (e.target === e.currentTarget) handleCancel(); }}
        >
            <div className="bg-gray-900 border border-gray-700 rounded-lg shadow-2xl max-w-md w-full p-6">
                {dialog.options.title && (
                    <h2 className="text-lg font-semibold text-white mb-2">{dialog.options.title}</h2>
                )}
                <p className="text-gray-300 whitespace-pre-line">{dialog.options.message}</p>

                {dialog.kind === 'prompt' && (
                    dialog.options.multiline ? (
                        <textarea
                            ref={inputRef as React.RefObject<HTMLTextAreaElement>}
                            value={promptValue}
                            onChange={(e) => setPromptValue(e.target.value)}
                            placeholder={dialog.options.placeholder}
                            rows={4}
                            className="mt-4 w-full bg-gray-800 border border-gray-700 rounded px-3 py-2 text-white focus:outline-none focus:border-orange-500"
                        />
                    ) : (
                        <input
                            ref={inputRef as React.RefObject<HTMLInputElement>}
                            type="text"
                            value={promptValue}
                            onChange={(e) => setPromptValue(e.target.value)}
                            placeholder={dialog.options.placeholder}
                            onKeyDown={(e) => { if (e.key === 'Enter') handleConfirm(); }}
                            className="mt-4 w-full bg-gray-800 border border-gray-700 rounded px-3 py-2 text-white focus:outline-none focus:border-orange-500"
                        />
                    )
                )}

                <div className="mt-6 flex justify-end gap-2">
                    {dialog.kind !== 'alert' && (
                        <button
                            type="button"
                            onClick={handleCancel}
                            className="px-4 py-2 rounded text-gray-300 hover:bg-gray-800"
                        >
                            {dialog.options.cancelText ?? 'Cancel'}
                        </button>
                    )}
                    <button
                        ref={confirmButtonRef}
                        type="button"
                        onClick={handleConfirm}
                        className={`px-4 py-2 rounded text-white ${confirmClass}`}
                    >
                        {dialog.options.confirmText ?? (dialog.kind === 'alert' ? 'OK' : 'Confirm')}
                    </button>
                </div>
            </div>
        </div>
    );
}
