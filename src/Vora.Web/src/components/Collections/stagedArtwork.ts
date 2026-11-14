import { useCallback, useEffect, useRef, useState } from 'react';
import { collectionAdminService } from '../../api/Collections/collectionAdminService';
import type { ArtworkResult } from '../../api/Media/artworkService';
import { errorDetail } from '../../utils/apiError';
import type { CollectionArtworkKind } from './CollectionArtworkPanel';

interface StagedArtworkBase {
    id: string;
    kind: CollectionArtworkKind;
    previewUrl: string;
}

export type StagedArtwork = (StagedArtworkBase & { file: File }) | (StagedArtworkBase & { url: string });

export type StagedSelection = Partial<Record<CollectionArtworkKind, StagedArtwork>>;

export interface StagedArtworkOutcome {
    problems: string[];
    missing: CollectionArtworkKind[];
}

const ARTWORK_KINDS: CollectionArtworkKind[] = ['Poster', 'Backdrop'];

export function useStagedArtwork() {
    const [staged, setStaged] = useState<StagedArtwork[]>([]);
    const previews = useRef(new Set<string>());
    const nextId = useRef(0);

    useEffect(() => {
        const urls = previews.current;
        return () => {
            urls.forEach(url => URL.revokeObjectURL(url));
            urls.clear();
        };
    }, []);

    const addFile = useCallback((kind: CollectionArtworkKind, file: File) => {
        const previewUrl = URL.createObjectURL(file);
        previews.current.add(previewUrl);
        const id = `staged-${nextId.current++}`;
        setStaged(list => [...list, { id, kind, previewUrl, file }]);
    }, []);

    const addUrl = useCallback((kind: CollectionArtworkKind, url: string) => {
        const id = `staged-${nextId.current++}`;
        setStaged(list => list.some(item => 'url' in item && item.kind === kind && item.url === url)
            ? list
            : [...list, { id, kind, previewUrl: url, url }]);
    }, []);

    const remove = useCallback((item: StagedArtwork) => {
        if ('file' in item) {
            URL.revokeObjectURL(item.previewUrl);
            previews.current.delete(item.previewUrl);
        }
        setStaged(list => list.filter(entry => entry.id !== item.id));
    }, []);

    const clear = useCallback(() => {
        previews.current.forEach(url => URL.revokeObjectURL(url));
        previews.current.clear();
        setStaged([]);
    }, []);

    return { staged, addFile, addUrl, remove, clear };
}

export const stagedArtworkOption = (item: StagedArtwork): ArtworkResult => ({
    id: item.id,
    url: item.previewUrl,
    type: item.kind,
    language: 'None',
    isUserUploaded: true,
});

export function webImageAddress(raw: string): string | undefined {
    try {
        const url = new URL(raw.trim());
        return url.protocol === 'http:' || url.protocol === 'https:' ? raw.trim() : undefined;
    } catch {
        return undefined;
    }
}

const describe = (item: StagedArtwork) => {
    const kind = item.kind.toLowerCase();
    return 'file' in item ? `The ${kind} file “${item.file.name}”` : `The ${kind} from ${item.url}`;
};

export async function saveStagedArtwork(collectionId: string, staged: StagedArtwork[], selected: StagedSelection, serverId?: string): Promise<StagedArtworkOutcome> {
    const chosen = new Set(ARTWORK_KINDS.map(kind => selected[kind]?.id));
    const ordered = [...staged].sort((a, b) => Number(chosen.has(b.id)) - Number(chosen.has(a.id)));
    const saved = new Map<string, string>();
    const outcome: StagedArtworkOutcome = { problems: [], missing: [] };

    for (const item of ordered) {
        try {
            const url = 'file' in item
                ? await collectionAdminService.uploadArtwork(collectionId, item.kind, item.file, serverId)
                : await collectionAdminService.addArtworkUrl(collectionId, item.kind, item.url, serverId);
            saved.set(item.id, url);
        } catch (error) {
            outcome.problems.push(errorDetail(error, `${describe(item)} was not saved`));
        }
    }

    for (const kind of ARTWORK_KINDS) {
        const item = selected[kind];
        if (!item) continue;
        const url = saved.get(item.id);
        if (!url) {
            outcome.missing.push(kind);
            continue;
        }
        try {
            await collectionAdminService.selectArtwork(collectionId, kind, url, serverId);
        } catch (error) {
            outcome.problems.push(errorDetail(error, `${describe(item)} was saved but not set as the ${kind.toLowerCase()}`));
            outcome.missing.push(kind);
        }
    }

    return outcome;
}

export function stagedArtworkProblems(title: string, outcome: StagedArtworkOutcome): string {
    const missing = outcome.missing.map(kind => kind.toLowerCase());
    const closing = missing.length > 0
        ? `It has no ${missing.join(' and no ')} for now. You can add ${missing.length > 1 ? 'them' : 'it'} in Edit Collection.`
        : `You can add ${outcome.problems.length > 1 ? 'them' : 'it'} again in Edit Collection.`;
    return [
        `“${title}” was created with everything else you entered.`,
        outcome.problems.map(problem => `• ${problem.replace(/\.$/, '')}.`).join('\n'),
        closing,
    ].join('\n\n');
}
