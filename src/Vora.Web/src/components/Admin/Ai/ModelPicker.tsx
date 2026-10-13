import { useState } from 'react';

const OTHER = '__other__';

interface ModelPickerProps {
    id: string;
    value: string;
    onChange: (value: string) => void;
    // The models the server says it has. With none, the name is typed.
    options: string[];
    // What an empty value means, offered as the first choice. Leave out when
    // a model has to be chosen.
    emptyLabel?: string;
    placeholder?: string;
}

// A list of the server's models with a way out of it: a model the list does not
// show, or a server that could not be asked, still takes a typed name.
export default function ModelPicker({ id, value, onChange, options, emptyLabel, placeholder }: ModelPickerProps) {
    const [typing, setTyping] = useState(false);
    const listed = value === '' || options.includes(value);
    const typed = typing || !listed;

    if (options.length === 0) {
        return (
            <input
                id={id}
                type="text"
                value={value}
                onChange={e => onChange(e.target.value)}
                placeholder={placeholder ?? emptyLabel}
                className="vora-input font-mono text-sm"
                autoComplete="off"
                spellCheck={false}
            />
        );
    }

    return (
        <div className="flex flex-col gap-2">
            <select
                id={id}
                value={typed ? OTHER : value}
                onChange={e => {
                    if (e.target.value === OTHER) {
                        setTyping(true);
                        return;
                    }
                    setTyping(false);
                    onChange(e.target.value);
                }}
                className="vora-input cursor-pointer"
            >
                {emptyLabel !== undefined
                    ? <option value="">{emptyLabel}</option>
                    : value === '' && <option value="">Choose a model…</option>}
                {options.map(option => <option key={option} value={option}>{option}</option>)}
                <option value={OTHER}>Another model…</option>
            </select>
            {typed && (
                <input
                    type="text"
                    aria-label="Model name"
                    value={value}
                    onChange={e => onChange(e.target.value)}
                    placeholder={placeholder ?? 'Model name'}
                    className="vora-input font-mono text-sm"
                    autoComplete="off"
                    spellCheck={false}
                />
            )}
        </div>
    );
}
