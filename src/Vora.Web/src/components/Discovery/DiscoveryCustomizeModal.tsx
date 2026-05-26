import React, { useState, useEffect } from 'react';
import { type DiscoveryRowConfig } from '../../api/Discovery/discoveryService';
import { Modal } from '../Common/Modal';

export interface ClientLayoutItem {
    uniqueId: string;
    isEnabled: boolean;
    orderIndex: number;
    name: string;
    providerName: string;
    serverName?: string;
}

interface Props {
    isOpen: boolean;
    onClose: () => void;
    configs: (DiscoveryRowConfig & { uniqueId: string, serverName?: string })[];
    savedLayout: ClientLayoutItem[];
    onSave: (newLayout: ClientLayoutItem[]) => void;
}

export default function DiscoveryCustomizeModal({ isOpen, onClose, configs, savedLayout, onSave }: Props) {
    const [layoutItems, setLayoutItems] = useState<ClientLayoutItem[]>([]);
    const [draggedIndex, setDraggedIndex] = useState<number | null>(null);

    useEffect(() => {
        if (isOpen) {
            setTimeout(() => {
                const merged = configs.map(config => {
                    const existing = savedLayout.find(l => l.uniqueId === config.uniqueId);
                    return {
                        uniqueId: config.uniqueId,
                        name: config.name,
                        providerName: config.providerName || config.providerId,
                        serverName: config.serverName,
                        isEnabled: existing ? existing.isEnabled : config.isEnabled,
                        orderIndex: existing ? existing.orderIndex : config.orderIndex
                    };
                }).sort((a, b) => a.orderIndex - b.orderIndex);

                setLayoutItems(merged.map((item, idx) => ({ ...item, orderIndex: idx })));
            }, 0);
        }
    }, [isOpen, configs, savedLayout]);

    const handleDragStart = (e: React.DragEvent, index: number) => {
        setDraggedIndex(index);
        e.dataTransfer.effectAllowed = "move";
        e.dataTransfer.setData("text/html", index.toString());
    };

    const handleDrop = (e: React.DragEvent, dropIndex: number) => {
        e.preventDefault();
        if (draggedIndex === null || draggedIndex === dropIndex) return;

        const newItems = [...layoutItems];
        const draggedItem = newItems[draggedIndex];

        newItems.splice(draggedIndex, 1);
        newItems.splice(dropIndex, 0, draggedItem);

        setLayoutItems(newItems.map((item, idx) => ({ ...item, orderIndex: idx })));
        setDraggedIndex(null);
    };

    const toggleRow = (uniqueId: string) => {
        setLayoutItems(prev => prev.map(item =>
            item.uniqueId === uniqueId ? { ...item, isEnabled: !item.isEnabled } : item
        ));
    };

    const handleSave = () => {
        onSave(layoutItems);
        onClose();
    };

    return (
        <Modal
            isOpen={isOpen}
            onClose={onClose}
            size="lg"
            zIndex="z-[60]"
            position="absolute"
            surface="gray-900"
            closeOnBackdropClick
            cardClassName="p-8 flex flex-col max-h-[85vh] animate-fade-in"
        >
            <div className="mb-6 border-b border-[var(--vora-border-subtle)] pb-4 shrink-0">
                <h2 className="text-2xl font-bold text-[var(--vora-text-primary)] mb-2">Customize Discovery</h2>
                <p className="text-[var(--vora-text-muted)] text-sm">Drag to reorder rows. Toggle to hide lists you don't want to see. Saved to this device.</p>
            </div>

            <ul className="flex-1 overflow-y-auto custom-scrollbar pr-2 space-y-2">
                {layoutItems.map((item, index) => (
                    <li
                        key={item.uniqueId}
                        draggable
                        onDragStart={(e) => handleDragStart(e, index)}
                        onDragOver={(e) => e.preventDefault()}
                        onDrop={(e) => handleDrop(e, index)}
                        className={`flex items-center justify-between p-4 bg-[var(--vora-bg-sunken)] border ${draggedIndex === index ? 'border-orange-500 opacity-50' : 'border-[var(--vora-border-subtle)]'} rounded-lg cursor-grab hover:bg-[var(--vora-bg-raised)] transition-colors shadow-sm`}
                    >
                        <div className="flex items-center gap-4">
                            <svg className="w-5 h-5 text-[var(--vora-text-muted)]" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 8h16M4 16h16" /></svg>
                            <div>
                                <p className={`font-bold text-sm ${item.isEnabled ? 'text-[var(--vora-text-primary)]' : 'text-[var(--vora-text-muted)]'}`}>{item.name}</p>
                                <p className="text-[var(--vora-text-muted)] text-xs uppercase tracking-wider mt-0.5">
                                    {item.serverName ? `${item.serverName} • ` : ''}{item.providerName}
                                </p>
                            </div>
                        </div>

                        <button
                            onClick={() => toggleRow(item.uniqueId)}
                            className={`relative inline-flex h-6 w-11 items-center rounded-full transition-colors cursor-pointer ${item.isEnabled ? 'bg-[var(--vora-accent-500)]' : 'bg-[var(--vora-bg-sunken)]'}`}
                        >
                            <span className={`inline-block h-4 w-4 transform rounded-full bg-white transition-transform ${item.isEnabled ? 'translate-x-6' : 'translate-x-1'}`} />
                        </button>
                    </li>
                ))}
            </ul>

            <div className="flex justify-end gap-4 mt-6 pt-6 border-t border-[var(--vora-border-subtle)] shrink-0">
                <button onClick={onClose} className="px-4 py-2 text-[var(--vora-text-muted)] hover:text-[var(--vora-text-primary)] transition-colors cursor-pointer font-medium">Cancel</button>
                <button onClick={handleSave} className="px-6 py-2 bg-[var(--vora-accent-500)] hover:bg-[var(--vora-accent-500)] text-[var(--vora-text-primary)] font-bold rounded shadow-lg transition-colors cursor-pointer">Save Layout</button>
            </div>
        </Modal>
    );
}
