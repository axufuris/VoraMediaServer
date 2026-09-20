import { createContext, useContext } from 'react';

export type DialogTone = 'default' | 'danger' | 'success';

export interface AlertOptions {
    title?: string;
    message: string;
    confirmText?: string;
    tone?: DialogTone;
}

export interface ConfirmOptions extends AlertOptions {
    cancelText?: string;
}

export interface PromptOptions extends ConfirmOptions {
    placeholder?: string;
    defaultValue?: string;
    multiline?: boolean;
}

export interface DialogApi {
    alert: (options: AlertOptions | string) => Promise<void>;
    confirm: (options: ConfirmOptions | string) => Promise<boolean>;
    prompt: (options: PromptOptions | string) => Promise<string | null>;
}

export const DialogContext = createContext<DialogApi | null>(null);

export function useDialog(): DialogApi {
    const ctx = useContext(DialogContext);
    if (!ctx) {
        throw new Error('useDialog must be used inside <DialogProvider>.');
    }
    return ctx;
}
