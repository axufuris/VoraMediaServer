import { useCallback, useMemo, useRef, useState, type ReactNode } from 'react';
import { useLocation } from 'react-router-dom';
import { mediaService, type MediaItem } from '../../../api/Media/mediaService';
import ActionMenu from '../../Client/Primitives/ActionMenu';
import type { MenuPoint } from '../../Client/Primitives/menuEntries';
import { seasonEpisodeLabel, seasonName } from '../../../utils/seasonLabel';
import { MediaItemMenuContext, type MediaItemMenuApi, type MediaMenuTarget } from './mediaItemMenu';
import { useMediaItemActions } from './useMediaItemActions';

interface OpenMenu {
    at: MenuPoint;
    path: string;
}

interface Subject {
    target: MediaMenuTarget;
    media: MediaItem | null;
    failed: boolean;
}

function menuTitle(media: MediaItem): string {
    if (media.type === 'Season' && media.tvShowTitle) {
        return `${media.tvShowTitle} · ${seasonName(media.seasonNumber, media.title)}`;
    }
    if (media.type === 'Episode' && media.tvShowTitle) {
        const number = media.seasonNumber != null && media.episodeNumber != null
            ? seasonEpisodeLabel(media.seasonNumber, media.episodeNumber, { endEpisodeNumber: media.endEpisodeNumber })
            : null;
        return [media.tvShowTitle, number, media.title].filter(Boolean).join(' · ');
    }
    return media.title;
}

// Shows a title's menu wherever one of its posters is right-clicked or held,
// and keeps the dialogs that menu opens. One host for the whole client: a
// poster only says which title it is.
export default function MediaItemMenuProvider({ children }: { children: ReactNode }) {
    const location = useLocation();
    const [menu, setMenu] = useState<OpenMenu | null>(null);
    const [subject, setSubject] = useState<Subject | null>(null);
    const latestRequest = useRef(0);

    const load = useCallback((target: MediaMenuTarget): Promise<MediaItem | null> => {
        const request = ++latestRequest.current;
        return mediaService.getMediaItem(target.id, target.serverId)
            .then(media => {
                if (latestRequest.current === request) setSubject({ target, media, failed: false });
                return media;
            })
            .catch(() => {
                if (latestRequest.current === request) setSubject({ target, media: null, failed: true });
                return null;
            });
    }, []);

    const open = useCallback((target: MediaMenuTarget, at: MenuPoint) => {
        setMenu({ at, path: window.location.pathname });
        setSubject(current => current?.media && current.target.id === target.id && current.target.serverId === target.serverId
            ? { ...current, target }
            : { target, media: null, failed: false });
        void load(target);
    }, [load]);

    const close = useCallback(() => setMenu(null), []);
    const api = useMemo<MediaItemMenuApi>(() => ({ open }), [open]);

    const target = subject?.target;
    const actions = useMediaItemActions(subject?.media ?? null, {
        serverId: target?.serverId,
        withWatchActions: true,
        onChanged: () => {
            if (!target) return;
            void load(target).then(media => target.onChanged?.(media ?? undefined));
        },
        onDeleted: () => {
            setSubject(null);
            (target?.onDeleted ?? target?.onChanged)?.();
        },
    });

    // Moving to another page leaves the menu behind, so it only shows on the
    // page it was opened from.
    const shown = menu && subject && menu.path === location.pathname ? menu : null;

    return (
        <MediaItemMenuContext.Provider value={api}>
            {children}
            {shown && subject && (
                <ActionMenu
                    at={shown.at}
                    title={subject.media ? menuTitle(subject.media) : undefined}
                    entries={actions.entries}
                    note={subject.failed ? "Couldn't load this title." : subject.media ? undefined : 'Loading…'}
                    onClose={close}
                />
            )}
            {actions.dialogs}
        </MediaItemMenuContext.Provider>
    );
}
