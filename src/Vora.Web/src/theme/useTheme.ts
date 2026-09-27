import { createContext, useContext } from 'react';
import type { ThemeManifest } from './types';

export interface ThemeContextValue {
    builtInThemes: ThemeManifest[];
    active: ThemeManifest;
    isLoading: boolean;
    isSwitching: boolean;
    setActive: (id: string) => Promise<boolean>;
}

export const ThemeContext = createContext<ThemeContextValue | null>(null);

export function useTheme(): ThemeContextValue {
    const ctx = useContext(ThemeContext);
    if (!ctx) {
        throw new Error('useTheme must be used inside <ThemeProvider>');
    }
    return ctx;
}
