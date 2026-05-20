import { useEffect, useRef, useState, type ReactNode } from 'react';

export interface IconSelectOption<T extends string | number> {
    value: T;
    label: string;
    icon?: ReactNode;
}

interface IconSelectProps<T extends string | number> {
    value: T;
    options: IconSelectOption<T>[];
    onChange: (value: T) => void;
    className?: string;
    placeholder?: string;
}

export default function IconSelect<T extends string | number>({ value, options, onChange, className, placeholder }: IconSelectProps<T>) {
    const [isOpen, setIsOpen] = useState(false);
    const wrapperRef = useRef<HTMLDivElement>(null);

    useEffect(() => {
        if (!isOpen) return;
        const handleClickOutside = (event: MouseEvent) => {
            if (wrapperRef.current && !wrapperRef.current.contains(event.target as Node)) {
                setIsOpen(false);
            }
        };
        const handleKey = (event: KeyboardEvent) => {
            if (event.key === 'Escape') setIsOpen(false);
        };
        document.addEventListener('mousedown', handleClickOutside);
        document.addEventListener('keydown', handleKey);
        return () => {
            document.removeEventListener('mousedown', handleClickOutside);
            document.removeEventListener('keydown', handleKey);
        };
    }, [isOpen]);

    const selected = options.find(o => o.value === value);

    return (
        <div ref={wrapperRef} className={`relative ${className ?? ''}`}>
            <button
                type="button"
                onClick={() => setIsOpen(o => !o)}
                className={`w-full p-2 bg-gray-900 rounded border ${isOpen ? 'border-blue-500' : 'border-gray-600'} text-white text-left flex items-center justify-between gap-2 cursor-pointer hover:border-gray-500 transition-colors`}
            >
                <span className="flex items-center gap-2 min-w-0">
                    {selected?.icon && <span className="shrink-0 text-gray-300">{selected.icon}</span>}
                    <span className="truncate">{selected?.label ?? placeholder ?? 'Select...'}</span>
                </span>
                <svg className={`w-4 h-4 shrink-0 text-gray-400 transition-transform ${isOpen ? 'rotate-180' : ''}`} fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 9l-7 7-7-7" /></svg>
            </button>

            {isOpen && (
                <div className="absolute top-full left-0 right-0 mt-1 bg-gray-900 border border-gray-700 rounded shadow-2xl z-50 overflow-hidden">
                    <ul className="max-h-72 overflow-y-auto custom-scrollbar py-1">
                        {options.map(option => {
                            const isSelected = option.value === value;
                            return (
                                <li
                                    key={String(option.value)}
                                    onClick={() => { onChange(option.value); setIsOpen(false); }}
                                    className={`flex items-center gap-2 px-3 py-2 cursor-pointer transition-colors ${isSelected ? 'bg-blue-600/20 text-white' : 'text-gray-200 hover:bg-gray-800'}`}
                                >
                                    {option.icon && <span className="shrink-0 text-gray-300">{option.icon}</span>}
                                    <span className="truncate">{option.label}</span>
                                </li>
                            );
                        })}
                    </ul>
                </div>
            )}
        </div>
    );
}
