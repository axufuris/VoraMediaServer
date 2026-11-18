import { useCallback, useState } from 'react';
import { StorageKeys } from '../../utils/storageKeys';

const readPinned = (): boolean => {
    try {
        return localStorage.getItem(StorageKeys.sidebarPinned) === 'true';
    } catch {
        return false;
    }
};

const savePinned = (pinned: boolean) => {
    try {
        localStorage.setItem(StorageKeys.sidebarPinned, String(pinned));
    } catch {
        return;
    }
};

// Whether the client sidebar is pinned: it stays as the viewer set it, folded
// or open, instead of folding away on its own. Only an installed app or Vora
// Desktop folds on its own, so only they draw the pin. Unpinned is the
// default, and it is remembered per browser like the fold itself.
export function useSidebarPinned(): [boolean, () => void] {
    const [pinned, setPinned] = useState(readPinned);

    const toggle = useCallback(() => {
        setPinned(was => {
            savePinned(!was);
            return !was;
        });
    }, []);

    return [pinned, toggle];
}
