import { useContext } from 'react';
import { MediaItemMenuContext, type MediaMenuTarget } from './mediaItemMenu';
import { useMenuTrigger, type MenuTriggerProps } from '../../../hooks/useMenuTrigger';
import type { MenuPoint } from '../../Client/Primitives/menuEntries';

export function useMediaMenuOpener(target: MediaMenuTarget | null | undefined): ((at: MenuPoint) => void) | undefined {
    const menu = useContext(MediaItemMenuContext);
    if (!menu || !target) return undefined;
    return at => menu.open(target, at);
}

// Props for anything that stands for a title: spread them on the element and a
// right-click, the keyboard's menu key, or a long press with a finger opens the
// title's menu. Returns nothing when there is no title or no menu host, so the
// element keeps the browser's own menu.
export function useMediaMenuTrigger(target: MediaMenuTarget | null | undefined): MenuTriggerProps | undefined {
    return useMenuTrigger(useMediaMenuOpener(target));
}
