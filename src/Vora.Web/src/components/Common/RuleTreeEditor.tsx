import { useMemo } from 'react';
import {
    emptyRuleGroup,
    type SmartPlaylistRule,
    type SmartPlaylistRuleGroup,
    type SmartPlaylistField,
    type SmartPlaylistOperator,
    type SmartPlaylistMatch,
    type PlaylistMediaType
} from '../../api/Music/smartPlaylistService';

type FieldKind = 'string' | 'int' | 'date' | 'bool';

export interface FieldDef {
    value: SmartPlaylistField;
    label: string;
    kind: FieldKind;
}

// Content-only fields for smart collections — no profile-scoped fields
// (watched / my-rating / last-played) since a collection is shared, not
// per-profile.
export const COLLECTION_FIELDS_BY_TYPE: Partial<Record<PlaylistMediaType, FieldDef[]>> = {
    Movies: [
        { value: 'Title', label: 'Title', kind: 'string' },
        { value: 'Genre', label: 'Genre', kind: 'string' },
        { value: 'ContentRating', label: 'Content Rating', kind: 'string' },
        { value: 'ReleaseYear', label: 'Year', kind: 'int' },
        { value: 'DurationSeconds', label: 'Duration (seconds)', kind: 'int' },
        { value: 'ServerAdminRating', label: 'Admin Rating', kind: 'int' },
        { value: 'AudienceRating', label: 'Audience Rating', kind: 'int' },
        { value: 'DateAdded', label: 'Date Added', kind: 'date' }
    ]
};

const STRING_OPS: { value: SmartPlaylistOperator; label: string }[] = [
    { value: 'Equals', label: 'is' },
    { value: 'NotEquals', label: 'is not' },
    { value: 'Contains', label: 'contains' },
    { value: 'NotContains', label: 'does not contain' },
    { value: 'StartsWith', label: 'starts with' },
    { value: 'EndsWith', label: 'ends with' },
    { value: 'IsNull', label: 'is empty' },
    { value: 'IsNotNull', label: 'is not empty' }
];

const INT_OPS: { value: SmartPlaylistOperator; label: string }[] = [
    { value: 'Equals', label: '=' },
    { value: 'NotEquals', label: '≠' },
    { value: 'GreaterThan', label: '>' },
    { value: 'LessThan', label: '<' },
    { value: 'Between', label: 'between' },
    { value: 'IsNull', label: 'is empty' },
    { value: 'IsNotNull', label: 'is not empty' }
];

const DATE_OPS: { value: SmartPlaylistOperator; label: string }[] = [
    { value: 'InLastDays', label: 'in last N days' },
    { value: 'NotInLastDays', label: 'not in last N days' },
    { value: 'GreaterThan', label: 'after' },
    { value: 'LessThan', label: 'before' },
    { value: 'IsNull', label: 'never' },
    { value: 'IsNotNull', label: 'has value' }
];

const BOOL_OPS: { value: SmartPlaylistOperator; label: string }[] = [
    { value: 'Equals', label: 'is' }
];

function getOpsForField(field: SmartPlaylistField, fields: FieldDef[]): { value: SmartPlaylistOperator; label: string }[] {
    const def = fields.find(f => f.value === field) ?? fields[0];
    switch (def?.kind) {
        case 'int': return INT_OPS;
        case 'date': return DATE_OPS;
        case 'bool': return BOOL_OPS;
        default: return STRING_OPS;
    }
}

function getFieldKind(field: SmartPlaylistField, fields: FieldDef[]): FieldKind {
    return fields.find(f => f.value === field)?.kind ?? 'string';
}

function operatorNeedsNoValue(op: SmartPlaylistOperator) {
    return op === 'IsNull' || op === 'IsNotNull';
}

function applyAtPath(group: SmartPlaylistRuleGroup, path: number[], updater: (g: SmartPlaylistRuleGroup) => SmartPlaylistRuleGroup): SmartPlaylistRuleGroup {
    if (path.length === 0) return updater(group);
    const [head, ...rest] = path;
    return {
        ...group,
        groups: group.groups.map((g, i) => i === head ? applyAtPath(g, rest, updater) : g)
    };
}

interface GroupEditorProps {
    group: SmartPlaylistRuleGroup;
    path: number[];
    fields: FieldDef[];
    onAddRule: (path: number[]) => void;
    onAddGroup: (path: number[]) => void;
    onUpdate: (path: number[], updater: (g: SmartPlaylistRuleGroup) => SmartPlaylistRuleGroup) => void;
}

function RuleGroupEditor({ group, path, fields, onAddRule, onAddGroup, onUpdate }: GroupEditorProps) {
    const indent = path.length === 0 ? '' : 'border-l-2 border-[var(--vora-accent-500)]/40 pl-4 ml-2';
    return (
        <div className={`${indent} space-y-2`}>
            <div className="flex items-center gap-3 text-sm">
                <span className="text-[var(--vora-text-muted)]">Match</span>
                <select
                    value={group.match}
                    onChange={e => onUpdate(path, g => ({ ...g, match: e.target.value as SmartPlaylistMatch }))}
                    className="bg-[var(--vora-bg-canvas)] border border-[var(--vora-border-subtle)] rounded px-2 py-1 text-[var(--vora-text-primary)] outline-none cursor-pointer"
                >
                    <option value="All">All</option>
                    <option value="Any">Any</option>
                </select>
                <span className="text-[var(--vora-text-muted)]">of the following:</span>
            </div>

            {group.rules.map((rule, idx) => (
                <RuleRow
                    key={`r-${idx}`}
                    rule={rule}
                    fields={fields}
                    onChange={(updater) => onUpdate(path, g => ({ ...g, rules: g.rules.map((r, i) => i === idx ? updater(r) : r) }))}
                    onRemove={() => onUpdate(path, g => ({ ...g, rules: g.rules.filter((_, i) => i !== idx) }))}
                />
            ))}

            {group.groups.map((sub, idx) => (
                <div key={`g-${idx}`} className="relative">
                    <button
                        type="button"
                        onClick={() => onUpdate(path, g => ({ ...g, groups: g.groups.filter((_, i) => i !== idx) }))}
                        className="absolute -left-2 top-0 w-5 h-5 rounded-full bg-[var(--vora-danger-500)] hover:opacity-80 text-[var(--vora-accent-contrast)] text-xs cursor-pointer z-10"
                        title="Remove group"
                    >×</button>
                    <RuleGroupEditor
                        group={sub}
                        path={[...path, idx]}
                        fields={fields}
                        onAddRule={onAddRule}
                        onAddGroup={onAddGroup}
                        onUpdate={onUpdate}
                    />
                </div>
            ))}

            <div className="flex gap-2 pt-1">
                <button type="button" onClick={() => onAddRule(path)} className="px-3 py-1 text-xs font-bold bg-[var(--vora-bg-sunken)] hover:bg-[var(--vora-bg-raised)] text-[var(--vora-text-primary)] rounded transition-colors cursor-pointer">+ Rule</button>
                <button type="button" onClick={() => onAddGroup(path)} className="px-3 py-1 text-xs font-bold bg-[var(--vora-bg-sunken)] hover:bg-[var(--vora-bg-raised)] text-[var(--vora-text-primary)] rounded transition-colors cursor-pointer">+ Group</button>
            </div>
        </div>
    );
}

interface RuleRowProps {
    rule: SmartPlaylistRule;
    fields: FieldDef[];
    onChange: (updater: (r: SmartPlaylistRule) => SmartPlaylistRule) => void;
    onRemove: () => void;
}

function RuleRow({ rule, fields, onChange, onRemove }: RuleRowProps) {
    const ops = useMemo(() => getOpsForField(rule.field, fields), [rule.field, fields]);
    const kind = getFieldKind(rule.field, fields);
    const noValue = operatorNeedsNoValue(rule.operator);
    const isBetween = rule.operator === 'Between';

    return (
        <div className="flex flex-wrap items-center gap-2 bg-[var(--vora-bg-canvas)]/60 border border-[var(--vora-border-subtle)] rounded px-2 py-2">
            <select
                value={rule.field}
                onChange={e => {
                    const newField = e.target.value as SmartPlaylistField;
                    const newOps = getOpsForField(newField, fields);
                    onChange(r => ({ ...r, field: newField, operator: newOps[0].value, value: '', secondValue: undefined }));
                }}
                className="bg-[var(--vora-bg-raised)] border border-[var(--vora-border-subtle)] rounded px-2 py-1 text-[var(--vora-text-primary)] text-sm outline-none cursor-pointer"
            >
                {fields.map(f => <option key={f.value} value={f.value}>{f.label}</option>)}
            </select>
            <select
                value={rule.operator}
                onChange={e => onChange(r => ({ ...r, operator: e.target.value as SmartPlaylistOperator }))}
                className="bg-[var(--vora-bg-raised)] border border-[var(--vora-border-subtle)] rounded px-2 py-1 text-[var(--vora-text-primary)] text-sm outline-none cursor-pointer"
            >
                {ops.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
            </select>

            {!noValue && (
                kind === 'bool' ? (
                    <select
                        value={rule.value ?? 'true'}
                        onChange={e => onChange(r => ({ ...r, value: e.target.value }))}
                        className="bg-[var(--vora-bg-raised)] border border-[var(--vora-border-subtle)] rounded px-2 py-1 text-[var(--vora-text-primary)] text-sm outline-none cursor-pointer"
                    >
                        <option value="true">true</option>
                        <option value="false">false</option>
                    </select>
                ) : kind === 'date' && (rule.operator === 'InLastDays' || rule.operator === 'NotInLastDays') ? (
                    <input
                        type="number"
                        min={1}
                        value={rule.value ?? ''}
                        onChange={e => onChange(r => ({ ...r, value: e.target.value }))}
                        className="bg-[var(--vora-bg-raised)] border border-[var(--vora-border-subtle)] rounded px-2 py-1 text-[var(--vora-text-primary)] text-sm outline-none w-24"
                        placeholder="days"
                    />
                ) : kind === 'date' ? (
                    <input
                        type="date"
                        value={rule.value ?? ''}
                        onChange={e => onChange(r => ({ ...r, value: e.target.value }))}
                        className="bg-[var(--vora-bg-raised)] border border-[var(--vora-border-subtle)] rounded px-2 py-1 text-[var(--vora-text-primary)] text-sm outline-none"
                    />
                ) : kind === 'int' ? (
                    <>
                        <input
                            type="number"
                            value={rule.value ?? ''}
                            onChange={e => onChange(r => ({ ...r, value: e.target.value }))}
                            className="bg-[var(--vora-bg-raised)] border border-[var(--vora-border-subtle)] rounded px-2 py-1 text-[var(--vora-text-primary)] text-sm outline-none w-24"
                        />
                        {isBetween && (
                            <>
                                <span className="text-[var(--vora-text-muted)] text-sm">and</span>
                                <input
                                    type="number"
                                    value={rule.secondValue ?? ''}
                                    onChange={e => onChange(r => ({ ...r, secondValue: e.target.value }))}
                                    className="bg-[var(--vora-bg-raised)] border border-[var(--vora-border-subtle)] rounded px-2 py-1 text-[var(--vora-text-primary)] text-sm outline-none w-24"
                                />
                            </>
                        )}
                    </>
                ) : (
                    <input
                        type="text"
                        value={rule.value ?? ''}
                        onChange={e => onChange(r => ({ ...r, value: e.target.value }))}
                        className="bg-[var(--vora-bg-raised)] border border-[var(--vora-border-subtle)] rounded px-2 py-1 text-[var(--vora-text-primary)] text-sm outline-none flex-1 min-w-[120px]"
                    />
                )
            )}

            <button type="button" onClick={onRemove} className="ml-auto px-2 py-1 text-[var(--vora-danger-500)] hover:bg-[var(--vora-danger-500)]/20 rounded text-sm cursor-pointer" title="Remove rule">×</button>
        </div>
    );
}

export default function RuleTreeEditor({ mediaType, value, onChange }: {
    mediaType: PlaylistMediaType;
    value: SmartPlaylistRuleGroup;
    onChange: (root: SmartPlaylistRuleGroup) => void;
}) {
    const fields = COLLECTION_FIELDS_BY_TYPE[mediaType] ?? COLLECTION_FIELDS_BY_TYPE.Movies!;

    const addRule = (path: number[]) => onChange(applyAtPath(value, path, g => {
        const first = fields[0];
        const op = getOpsForField(first.value, fields)[0].value;
        return { ...g, rules: [...g.rules, { field: first.value, operator: op, value: '' }] };
    }));
    const addGroup = (path: number[]) => onChange(applyAtPath(value, path, g => ({ ...g, groups: [...g.groups, emptyRuleGroup()] })));
    const update = (path: number[], updater: (g: SmartPlaylistRuleGroup) => SmartPlaylistRuleGroup) => onChange(applyAtPath(value, path, updater));

    return (
        <RuleGroupEditor group={value} path={[]} fields={fields} onAddRule={addRule} onAddGroup={addGroup} onUpdate={update} />
    );
}
