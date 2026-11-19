import type { LibrarySummary } from '../../../api/Media/libraryService';
import type { WebhookSettingDefinition } from '../../../api/System/webhookAdminService';
import { FieldLabel, Hint } from '../../../pages/Admin/SetupGuide/SetupParts';
import { isSettingVisible, parseOption } from './connectionForm';

interface TemplateSettingsProps {
    definitions: WebhookSettingDefinition[];
    values: Record<string, string>;
    libraries: LibrarySummary[] | null;
    librariesFailed: boolean;
    onChange: (key: string, value: string) => void;
}

function SettingControl({ definition, value, libraries, onChange }: {
    definition: WebhookSettingDefinition;
    value: string;
    libraries: LibrarySummary[] | null;
    onChange: (value: string) => void;
}) {
    const id = `webhook-setting-${definition.key}`;
    const common = { id, 'aria-required': definition.required || undefined };

    if (definition.type === 'textarea') {
        return (
            <textarea
                {...common}
                rows={3}
                value={value}
                placeholder={definition.placeholder || undefined}
                onChange={e => onChange(e.target.value)}
                className="vora-input min-h-20 w-full"
            />
        );
    }

    if (definition.type === 'select') {
        const choices = definition.options.map(parseOption);
        const hasCurrent = choices.some(choice => choice.value === value);
        return (
            <select {...common} value={value} onChange={e => onChange(e.target.value)} className={`vora-input w-full cursor-pointer`}>
                {!hasCurrent && <option value="">{definition.placeholder || ''}</option>}
                {choices.map(choice => <option key={choice.value} value={choice.value}>{choice.label}</option>)}
            </select>
        );
    }

    if (definition.type === 'library') {
        return (
            <select {...common} value={value} disabled={libraries === null} onChange={e => onChange(e.target.value)} className={`vora-input w-full cursor-pointer disabled:opacity-60`}>
                <option value="">{libraries === null ? 'Loading libraries…' : 'Choose a library…'}</option>
                {(libraries ?? []).map(library => <option key={library.id} value={library.id}>{library.name}</option>)}
            </select>
        );
    }

    return (
        <input
            {...common}
            type="text"
            value={value}
            placeholder={definition.placeholder || undefined}
            onChange={e => onChange(e.target.value)}
            className="vora-input w-full"
        />
    );
}

export default function TemplateSettings({ definitions, values, libraries, librariesFailed, onChange }: TemplateSettingsProps) {
    const visible = definitions.filter(definition => isSettingVisible(definition, values));
    if (visible.length === 0) return null;

    return (
        <div className="space-y-4">
            {visible.map(definition => (
                <div key={definition.key}>
                    <FieldLabel htmlFor={`webhook-setting-${definition.key}`}>
                        {definition.label}
                        {definition.required && <span aria-hidden="true"> *</span>}
                    </FieldLabel>
                    <SettingControl
                        definition={definition}
                        value={values[definition.key] ?? ''}
                        libraries={libraries}
                        onChange={value => onChange(definition.key, value)}
                    />
                    {definition.description && <Hint>{definition.description}</Hint>}
                    {definition.type === 'library' && librariesFailed && (
                        <p role="alert" className="mt-1.5 text-xs text-[var(--vora-danger-text)]">The libraries could not be loaded.</p>
                    )}
                </div>
            ))}
        </div>
    );
}
