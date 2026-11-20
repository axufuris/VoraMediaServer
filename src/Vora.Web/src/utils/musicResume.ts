import type { MusicResumeSave } from '../api/Music/musicService';
import type { PlayableMedia } from '../contexts/usePlayer';

export const RESUME_MAX_QUEUE = 300;
export const RESUME_LOOK_BEHIND = 50;

export function resumeWindow(ids: string[], index: number): { ids: string[]; index: number } {
    if (ids.length <= RESUME_MAX_QUEUE) return { ids, index };

    const start = Math.min(Math.max(0, index - RESUME_LOOK_BEHIND), ids.length - RESUME_MAX_QUEUE);
    return { ids: ids.slice(start, start + RESUME_MAX_QUEUE), index: index - start };
}

const isSong = (item: PlayableMedia | undefined): item is PlayableMedia => item?.playbackContextType === 'Music' && !!item.id;

export function musicResumeSnapshot(queue: PlayableMedia[], index: number): MusicResumeSave | null {
    const current = queue[index];
    if (!isSong(current)) return null;

    const songs = queue.filter(isSong);
    const position = songs.indexOf(current);
    if (position < 0) return null;

    const window = resumeWindow(songs.map(song => song.id), position);
    const source = current.musicSource;
    return { source: source?.type ?? 'None', sourceId: source?.id, trackIds: window.ids, index: window.index };
}

export function musicResumeKey(save: MusicResumeSave): string {
    const { trackIds } = save;
    return [save.source, save.sourceId ?? '', save.index, trackIds.length, trackIds[0] ?? '', trackIds[trackIds.length - 1] ?? ''].join('|');
}
