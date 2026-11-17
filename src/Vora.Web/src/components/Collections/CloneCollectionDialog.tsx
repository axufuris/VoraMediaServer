import { useEffect, useState } from 'react';
import { useParams } from 'react-router-dom';
import type { CollectionDetails } from '../../api/Collections/collectionService';
import { libraryService, type LibrarySummary } from '../../api/Media/libraryService';
import { Modal, ModalHeader, ModalBody, ModalFooter } from '../Common/Modal';
import { smartMediaForLibrary } from './smartCollectionMedia';

export type CloneKind = 'Movies' | 'Shows' | 'Global';

export interface CloneTarget {
    activeTab: string;
    libraryType?: string;
}

interface CloneCollectionDialogProps {
    onClose: () => void;
    collection: CollectionDetails;
    onChoose: (target: CloneTarget) => void;
}

const KINDS: { kind: CloneKind; label: string; hint: string; none: string }[] = [
    { kind: 'Movies', label: 'Movie', hint: 'Lives in one of your movie libraries.', none: 'You have no movie library.' },
    { kind: 'Shows', label: 'Show', hint: 'Lives in one of your TV show libraries.', none: 'You have no TV show library.' },
    { kind: 'Global', label: 'Global', hint: 'Visible across all libraries.', none: '' },
];

const librariesOfKind = (libraries: LibrarySummary[], kind: CloneKind): LibrarySummary[] =>
    kind === 'Global' ? [] : libraries.filter(lib => smartMediaForLibrary(lib.type) === kind);

export default function CloneCollectionDialog({ onClose, collection, onChoose }: CloneCollectionDialogProps) {
    const { serverId } = useParams<{ serverId?: string }>();
    const [libraries, setLibraries] = useState<LibrarySummary[] | null>(null);
    const [pickedKind, setPickedKind] = useState<CloneKind | null>(null);
    const [pickedLibraryId, setPickedLibraryId] = useState<string | null>(null);

    useEffect(() => {
        let cancelled = false;
        libraryService.getLibraries(serverId)
            .then(found => { if (!cancelled) setLibraries(found); })
            .catch(error => {
                console.error(error);
                if (!cancelled) setLibraries([]);
            });
        return () => { cancelled = true; };
    }, [serverId]);

    const choices = libraries ?? [];
    const source = choices.find(lib => lib.id === collection.libraryId);
    const kind: CloneKind = pickedKind ?? (source ? smartMediaForLibrary(source.type) : undefined) ?? 'Global';
    const matching = librariesOfKind(choices, kind);
    const chosenLibrary = matching.find(lib => lib.id === (pickedLibraryId ?? source?.id)) ?? matching[0];
    const canContinue = libraries !== null && (kind === 'Global' || chosenLibrary !== undefined);

    const choose = () => {
        if (kind === 'Global') onChoose({ activeTab: 'global' });
        else if (chosenLibrary) onChoose({ activeTab: chosenLibrary.id, libraryType: chosenLibrary.type });
    };

    return (
        <Modal isOpen onClose={onClose} size="md" cardClassName="flex flex-col max-h-[90vh]">
            <ModalHeader title="Clone Collection" subtitle={collection.title} onClose={onClose} />

            <ModalBody className="p-6">
                <fieldset className="space-y-2">
                    <legend className="block text-sm font-medium text-[var(--vora-text-muted)] mb-2">What kind of collection will the copy be?</legend>
                    {KINDS.map(option => {
                        const available = option.kind === 'Global' || librariesOfKind(choices, option.kind).length > 0;
                        const selected = kind === option.kind;
                        return (
                            <label
                                key={option.kind}
                                className={`flex items-start gap-3 rounded-lg border p-3 transition-colors ${available ? 'cursor-pointer' : 'cursor-not-allowed opacity-50'} ${selected ? 'border-[var(--vora-accent-500)] bg-[var(--vora-bg-raised)]' : 'border-[var(--vora-border-subtle)]'}`}
                            >
                                <input
                                    type="radio"
                                    name="clone-collection-kind"
                                    value={option.kind}
                                    checked={selected}
                                    disabled={!available}
                                    onChange={() => setPickedKind(option.kind)}
                                    className="mt-1 w-4 h-4 accent-[var(--vora-accent-500)]"
                                />
                                <span>
                                    <span className="block text-sm font-semibold text-[var(--vora-text-primary)]">{option.label}</span>
                                    <span className="block text-xs text-[var(--vora-text-muted)]">{available || libraries === null ? option.hint : option.none}</span>
                                </span>
                            </label>
                        );
                    })}
                </fieldset>

                {matching.length > 1 && (
                    <div className="mt-4">
                        <label htmlFor="clone-collection-library" className="block text-sm font-medium text-[var(--vora-text-muted)] mb-1">Library</label>
                        <select
                            id="clone-collection-library"
                            value={chosenLibrary?.id ?? ''}
                            onChange={e => setPickedLibraryId(e.target.value)}
                            className="w-full bg-[var(--vora-bg-raised)] border border-[var(--vora-border-subtle)] rounded-md p-2 text-[var(--vora-text-primary)] focus:border-[var(--vora-accent-500)] outline-none"
                        >
                            {matching.map(lib => <option key={lib.id} value={lib.id}>{lib.name}</option>)}
                        </select>
                    </div>
                )}
            </ModalBody>

            <ModalFooter className="flex justify-end gap-3">
                <button type="button" onClick={onClose} className="px-5 py-2 text-[var(--vora-text-muted)] hover:text-[var(--vora-text-primary)] font-medium transition-colors cursor-pointer">Cancel</button>
                <button type="button" onClick={choose} disabled={!canContinue} className="px-6 py-2 bg-[var(--vora-accent-500)] hover:bg-[var(--vora-accent-hover)] disabled:opacity-50 text-[var(--vora-accent-contrast)] font-bold rounded shadow-[var(--vora-shadow-lg)] transition-colors cursor-pointer">Continue</button>
            </ModalFooter>
        </Modal>
    );
}
