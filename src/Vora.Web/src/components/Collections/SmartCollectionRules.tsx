import { useState } from 'react';
import type { SmartPlaylistRuleGroup } from '../../api/Music/smartPlaylistService';
import { useDialog } from '../../dialogs';
import RuleTreeEditor from '../Common/RuleTreeEditor';
import { pruneRules, SMART_MEDIA_OPTIONS, type SmartCollectionMedia } from './smartCollectionMedia';
import { findSmartTemplate, SMART_TEMPLATE_GROUPS, templatesInGroup } from './smartCollectionTemplates';

interface SmartCollectionRulesProps {
    idPrefix: string;
    // A global collection can match movies, shows or both; a library
    // collection matches whatever its library holds, so it gets no choice.
    isGlobal: boolean;
    media: SmartCollectionMedia;
    onMediaChange: (media: SmartCollectionMedia) => void;
    root: SmartPlaylistRuleGroup;
    onRootChange: (root: SmartPlaylistRuleGroup) => void;
}

const NOUN: Record<SmartCollectionMedia, string> = {
    Movies: 'movie',
    Shows: 'show',
    Mixed: 'movie and show'
};

export default function SmartCollectionRules({ idPrefix, isGlobal, media, onMediaChange, root, onRootChange }: SmartCollectionRulesProps) {
    const dialog = useDialog();
    const selectId = `${idPrefix}-smart-media`;
    const templateId = `${idPrefix}-smart-template`;
    const where = isGlobal ? 'your libraries' : 'this library';
    const [templateNote, setTemplateNote] = useState<string | null>(null);
    const hasRules = root.rules.length > 0 || root.groups.length > 0;

    const changeMedia = (next: SmartCollectionMedia) => {
        onMediaChange(next);
        onRootChange(pruneRules(root, next));
    };

    const applyTemplate = async (id: string) => {
        const template = findSmartTemplate(id);
        if (!template) return;
        if (hasRules && !await dialog.confirm({
            title: 'Replace the rules?',
            message: `The rules you have now will be replaced by the ${template.label} template.`,
            confirmText: 'Replace',
            cancelText: 'Cancel',
        })) return;
        onRootChange(pruneRules(template.build(new Date()), media));
        setTemplateNote(template.note ?? null);
    };

    return (
        <div className="pt-3 mt-3 border-t border-[var(--vora-border-subtle)]/50 space-y-3">
            {isGlobal && (
                <div className="max-w-xs">
                    <label htmlFor={selectId} className="block text-sm font-medium text-[var(--vora-text-muted)] mb-1">Match</label>
                    <select
                        id={selectId}
                        value={media}
                        onChange={e => changeMedia(e.target.value as SmartCollectionMedia)}
                        className="w-full bg-[var(--vora-bg-canvas)] border border-[var(--vora-border-subtle)] rounded-md p-2 text-[var(--vora-text-primary)] outline-none"
                    >
                        {SMART_MEDIA_OPTIONS.map(option => (
                            <option key={option.value} value={option.value}>{option.label}</option>
                        ))}
                    </select>
                </div>
            )}
            <p className="text-xs text-[var(--vora-text-muted)] leading-relaxed">
                Adds every <span className="text-[var(--vora-text-secondary)]">{NOUN[media]}</span> in {where} that matches the rules below,
                for example Genre contains Horror and Year between 2010 and 2019.
                {media !== 'Movies' && ' Shows are added whole, not episode by episode.'}
                {' '}The collection updates itself as {isGlobal ? 'your libraries change' : 'the library changes'}.
            </p>
            <div className="max-w-xs">
                <label htmlFor={templateId} className="block text-sm font-medium text-[var(--vora-text-muted)] mb-1">Template</label>
                <select
                    id={templateId}
                    value=""
                    onChange={e => { void applyTemplate(e.target.value); }}
                    className="w-full bg-[var(--vora-bg-canvas)] border border-[var(--vora-border-subtle)] rounded-md p-2 text-[var(--vora-text-primary)] outline-none cursor-pointer"
                >
                    <option value="">Start from a template…</option>
                    {SMART_TEMPLATE_GROUPS.map(name => (
                        <optgroup key={name} label={name}>
                            {templatesInGroup(name).map(template => (
                                <option key={template.id} value={template.id}>{template.label}</option>
                            ))}
                        </optgroup>
                    ))}
                </select>
            </div>
            {templateNote && <p className="text-xs text-[var(--vora-text-muted)] leading-relaxed">{templateNote}</p>}
            <RuleTreeEditor mediaType={media} value={root} onChange={onRootChange} />
        </div>
    );
}
