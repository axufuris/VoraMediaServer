import { useState } from 'react';
import FolderBrowserModal from './FolderBrowserModal';

interface FolderPathInputProps {
    value: string;
    onChange: (value: string) => void;
    onEnter?: () => void;
    placeholder?: string;
    serverId?: string;
    className?: string;
    inputClassName?: string;
    disabled?: boolean;
    browseLabel?: string;
    modalTitle?: string;
}

export default function FolderPathInput({
    value,
    onChange,
    onEnter,
    placeholder,
    serverId,
    className,
    inputClassName,
    disabled,
    browseLabel = 'Browse',
    modalTitle
}: FolderPathInputProps) {
    const [open, setOpen] = useState(false);

    return (
        <>
            <div className={`flex gap-2 ${className ?? ''}`}>
                <input
                    type="text"
                    value={value}
                    onChange={e => onChange(e.target.value)}
                    onKeyDown={(e) => { if (e.key === 'Enter' && onEnter) { onEnter(); } }}
                    placeholder={placeholder}
                    disabled={disabled}
                    className={`vora-input flex-1 font-mono text-sm ${inputClassName ?? ''}`}
                />
                <button
                    type="button"
                    onClick={() => setOpen(true)}
                    disabled={disabled}
                    className="vora-button-secondary flex items-center gap-1.5 shrink-0 disabled:opacity-50 disabled:cursor-not-allowed"
                >
                    <FolderOpenIcon className="w-4 h-4" />
                    {browseLabel}
                </button>
            </div>
            <FolderBrowserModal
                isOpen={open}
                onClose={() => setOpen(false)}
                onSelect={onChange}
                initialPath={value || undefined}
                serverId={serverId}
                title={modalTitle}
            />
        </>
    );
}

function FolderOpenIcon({ className }: { className?: string }) {
    return (
        <svg className={className} fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M3 7a2 2 0 012-2h4l2 2h8a2 2 0 012 2v8a2 2 0 01-2 2H5a2 2 0 01-2-2V7z" />
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M3 11h18" />
        </svg>
    );
}
