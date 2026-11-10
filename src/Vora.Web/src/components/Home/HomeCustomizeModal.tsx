import { useState, useEffect, type DragEvent } from 'react';
import type { SmartListClientDto } from '../../api/Collections/smartListService';
import { Modal } from '../Common/Modal';
import { reorderListProps, reorderRowProps, useTouchReorder } from '../../hooks/useTouchReorder';

export interface HomeLayoutItem {
    listId: string;
    isEnabled: boolean;
    orderIndex: number;
}

interface ClientHomeCustomizeModalProps {
    isOpen: boolean;
    onClose: () => void;
    activeLists: SmartListClientDto[];
    savedLayout: HomeLayoutItem[];
    onSave: (newLayout: HomeLayoutItem[]) => void;
}

export default function HomeCustomizeModal({ isOpen, onClose, activeLists, savedLayout, onSave }: ClientHomeCustomizeModalProps) {
    const [workingLayout, setWorkingLayout] = useState<HomeLayoutItem[]>([]);
    const [draggedIndex, setDraggedIndex] = useState<number | null>(null);

    useEffect(() => {
        if (isOpen) {
            const timeoutId = window.setTimeout(() => {
                const prefById = new Map(savedLayout.map(p => [p.listId, p]));
                const withPref = activeLists
                    .filter(l => prefById.has(l.id))
                    .sort((a, b) => {
                        const pa = prefById.get(a.id);
                        const pb = prefById.get(b.id);
                        return (pa ? pa.orderIndex : 0) - (pb ? pb.orderIndex : 0);
                    });
                const withoutPref = activeLists.filter(l => !prefById.has(l.id));

                const combined = [...withPref, ...withoutPref].map((list, index) => {
                    const existing = prefById.get(list.id);
                    return { listId: list.id, isEnabled: existing ? existing.isEnabled : true, orderIndex: index };
                });
                setWorkingLayout(combined);
            }, 0);
            return () => window.clearTimeout(timeoutId);
        }
    }, [isOpen, activeLists, savedLayout]);

    // Drag a row to a new place, as in Customize Discovery and the sidebar:
    // the mouse drags the whole row, a finger drags its handle.
    const moveItem = (from: number, to: number) => {
        if (from === to) return;
        setWorkingLayout(prev => {
            const next = [...prev];
            const [moved] = next.splice(from, 1);
            next.splice(to, 0, moved);
            return next.map((item, i) => ({ ...item, orderIndex: i }));
        });
    };

    const touch = useTouchReorder(moveItem);

    const handleDrop = (e: DragEvent, dropIndex: number) => {
        e.preventDefault();
        if (draggedIndex !== null) moveItem(draggedIndex, dropIndex);
        setDraggedIndex(null);
    };

    const toggleItem = (index: number) => {
        const newLayout = [...workingLayout];
        newLayout[index].isEnabled = !newLayout[index].isEnabled;
        setWorkingLayout(newLayout);
    };

    const handleRevertToDefault = () => {
        const defaultLayout = activeLists.map((list, index) => ({
            listId: list.id,
            isEnabled: true,
            orderIndex: index
        }));
        setWorkingLayout(defaultLayout);
    };

    return (
        <Modal
            isOpen={isOpen}
            onClose={onClose}
            size="2xl"
            surface="gray-900"
            cardClassName="flex flex-col max-h-[85vh] overflow-hidden"
        >
            <div className="p-6 border-b border-[var(--vora-border-subtle)] flex justify-between items-center bg-[var(--vora-bg-canvas)] shrink-0">
                <div>
                    <h2 className="text-2xl font-bold text-[var(--vora-text-primary)] mb-1">Customize Home Screen</h2>
                    <p className="text-xs text-[var(--vora-text-muted)]">Drag to reorder, or untick a list to hide it. Continue Watching is always pinned to the top.</p>
                </div>
                <button onClick={onClose} className="text-[var(--vora-text-muted)] hover:text-[var(--vora-text-primary)] transition-colors cursor-pointer text-xl font-bold">✕</button>
            </div>

            <div {...reorderListProps} className="p-6 overflow-y-auto custom-scrollbar flex-1 space-y-3">
                <div className="flex items-center justify-between p-4 rounded-lg bg-[var(--vora-bg-sunken)]/30 border border-[var(--vora-border-subtle)] opacity-70">
                    <span className="font-bold text-[var(--vora-text-muted)]">Continue Watching</span>
                    <span className="text-[10px] uppercase font-bold text-[var(--vora-text-muted)] bg-[var(--vora-bg-sunken)] px-2 py-1 rounded">Pinned</span>
                </div>

                {workingLayout.map((item, index) => {
                    const listDetails = activeLists.find(l => l.id === item.listId);
                    if (!listDetails) return null;

                    return (
                        <div
                            key={item.listId}
                            {...reorderRowProps(index)}
                            data-testid={`home-layout-row-${item.listId}`}
                            style={touch.rowStyle(index)}
                            draggable
                            onDragStart={e => { setDraggedIndex(index); e.dataTransfer.effectAllowed = 'move'; e.dataTransfer.setData('text/plain', String(index)); }}
                            onDragOver={e => e.preventDefault()}
                            onDrop={e => handleDrop(e, index)}
                            onDragEnd={() => setDraggedIndex(null)}
                            className={`flex cursor-grab items-center justify-between p-4 rounded-lg border transition-colors ${draggedIndex === index ? 'border-[var(--vora-accent-500)] opacity-50' : 'border-[var(--vora-border-subtle)]'} ${item.isEnabled ? 'bg-[var(--vora-bg-sunken)]' : 'bg-[var(--vora-bg-raised)] opacity-60'}`}
                        >
                            <div className="flex items-center gap-4 flex-1 overflow-hidden">
                                <span {...touch.handleProps(index)} aria-hidden="true" className="flex shrink-0 touch-none">
                                    <svg className="w-5 h-5 text-[var(--vora-text-muted)]" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 8h16M4 16h16" /></svg>
                                </span>
                                <input type="checkbox" checked={item.isEnabled} onChange={() => toggleItem(index)} aria-label={`Show ${listDetails.title}`} className="w-5 h-5 accent-[var(--vora-accent-500)] cursor-pointer shrink-0" />
                                <span className={`font-bold truncate ${item.isEnabled ? 'text-[var(--vora-text-primary)]' : 'text-[var(--vora-text-muted)] line-through'}`}>{listDetails.title}</span>
                            </div>
                        </div>
                    );
                })}
            </div>

            <div className="p-5 border-t border-[var(--vora-border-subtle)] bg-[var(--vora-bg-canvas)] shrink-0 flex flex-col sm:flex-row gap-4 items-center justify-between">
                <button
                    onClick={handleRevertToDefault}
                    className="text-sm font-bold text-[var(--vora-text-muted)] hover:text-[var(--vora-text-primary)] transition-colors cursor-pointer whitespace-nowrap flex items-center gap-2"
                >
                    <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M3 10h10a8 8 0 018 8v2M3 10l6 6m-6-6l6-6" /></svg>
                    Revert to Default
                </button>
                <div className="flex gap-3 w-full sm:w-auto">
                    <button onClick={onClose} className="flex-1 sm:flex-none px-6 py-2.5 bg-[var(--vora-bg-sunken)] hover:bg-[var(--vora-bg-raised)] text-[var(--vora-text-primary)] font-bold rounded-lg transition-colors cursor-pointer">Cancel</button>
                    <button onClick={() => onSave(workingLayout)} className="flex-1 sm:flex-none px-6 py-2.5 bg-[var(--vora-accent-500)] hover:bg-[var(--vora-accent-hover)] text-[var(--vora-accent-contrast)] font-bold rounded-lg transition-colors shadow-[var(--vora-shadow-lg)] cursor-pointer">Save Layout</button>
                </div>
            </div>
        </Modal>
    );
}
