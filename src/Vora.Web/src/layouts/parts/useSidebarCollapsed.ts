import { useCallback, useState } from 'react';
import { StorageKeys } from '../../utils/storageKeys';

const readCollapsed = (): boolean => {
    try {
        return localStorage.getItem(StorageKeys.sidebarCollapsed) === 'true';
    } catch {
        return false;
    }
};

const saveCollapsed = (collapsed: boolean) => {
    try {
        localStorage.setItem(StorageKeys.sidebarCollapsed, String(collapsed));
    } catch {
        return;
    }
};

// Whether the client sidebar is folded down to its icons on a tablet or wider
// screen. Remembered per browser, like the other layout choices; a phone always
// opens the full menu from its drawer, whatever is saved here.
export function useSidebarCollapsed(): [boolean, () => void] {
    const [collapsed, setCollapsed] = useState(readCollapsed);

    const toggle = useCallback(() => {
        setCollapsed(was => {
            saveCollapsed(!was);
            return !was;
        });
    }, []);

    return [collapsed, toggle];
}
