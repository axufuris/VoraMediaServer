import { createContext, useContext } from 'react';
import type { ThemeManifest } from './types';
import type { ActiveTemplateVM, TemplateScheduleVM } from '../api/System/clientTemplateService';

export interface ClientTemplateContextValue {
    builtInTemplates: ThemeManifest[];
    active: ThemeManifest;
    activeInfo: ActiveTemplateVM | null;
    activeSchedule: TemplateScheduleVM | null;
    isLoading: boolean;
    isSwitching: boolean;
    setActive: (id: string) => Promise<boolean>;
    clearActive: () => Promise<boolean>;
    refresh: () => Promise<void>;
}

export const ClientTemplateContext = createContext<ClientTemplateContextValue | null>(null);

export function useClientTemplate(): ClientTemplateContextValue {
    const ctx = useContext(ClientTemplateContext);
    if (!ctx) {
        throw new Error('useClientTemplate must be used inside <ClientTemplateProvider>');
    }
    return ctx;
}
