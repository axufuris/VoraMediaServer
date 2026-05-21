import { useEffect, useMemo, useState } from 'react';
import {
    smartPlaylistService,
    emptyDefinition,
    emptyRuleGroup,
    type SmartPlaylistRule,
    type SmartPlaylistRuleGroup,
    type SmartPlaylistField,
    type SmartPlaylistOperator,
    type SmartPlaylistSortBy,
    type SmartPlaylistSortDirection,
    type SmartPlaylistMatch,
    type SmartPlaylistDefinition,
    type SmartPlaylistSummaryVM,
    type PlaylistMediaType
} from '../../../api/Music/smartPlaylistService';

interface Props {
    serverId?: string;
    initialId?: string;
    initialMediaType?: PlaylistMediaType;
    onClose: () => void;
    onSaved: (summary: SmartPlaylistSummaryVM) => void;
}

type FieldKind = 'string' | 'int' | 'date' | 'bool' | 'guid';

interface FieldDef {
    value: SmartPlaylistField;
    label: string;
    kind: FieldKind;
}

const FIELDS_BY_TYPE: Record<PlaylistMediaType, FieldDef[]> = {
    Music: [
        { value: 'Title', label: 'Title', kind: 'string' },
        { value: 'Artist', label: 'Artist', kind: 'string' },
        { value: 'AlbumTitle', label: 'Album', kind: 'string' },
        { value: 'AlbumArtist', label: 'Album Artist', kind: 'string' },
        { value: 'Genre', label: 'Genre', kind: 'string' },
        { value: 'ContentRating', label: 'Content Rating', kind: 'string' },
        { value: 'Year', label: 'Year', kind: 'int' },
        { value: 'DurationSeconds', label: 'Duration (seconds)', kind: 'int' },
        { value: 'PlayCount', label: 'Play Count', kind: 'int' },
        { value: 'TrackNumber', label: 'Track Number', kind: 'int' },
        { value: 'DiscNumber', label: 'Disc Number', kind: 'int' },
        { value: 'LastPlayedAt', label: 'Last Played', kind: 'date' },
        { value: 'DateAdded', label: 'Date Added', kind: 'date' },
        { value: 'Liked', label: 'Liked', kind: 'bool' },
        { value: 'IsCompilation', label: 'Is Compilation', kind: 'bool' }
    ],
    Movies: [
        { value: 'Title', label: 'Title', kind: 'string' },
        { value: 'Genre', label: 'Genre', kind: 'string' },
        { value: 'ContentRating', label: 'Content Rating', kind: 'string' },
        { value: 'ReleaseYear', label: 'Year', kind: 'int' },
        { value: 'DurationSeconds', label: 'Duration (seconds)', kind: 'int' },
        { value: 'ServerAdminRating', label: 'Server Admin Rating', kind: 'int' },
        { value: 'MyRating', label: 'My Rating', kind: 'int' },
        { value: 'AudienceRating', label: 'Audience Rating', kind: 'int' },
        { value: 'IsWatched', label: 'Is Watched', kind: 'bool' },
        { value: 'LastPlayedAt', label: 'Last Played', kind: 'date' },
        { value: 'DateAdded', label: 'Date Added', kind: 'date' }
    ],
    Shows: [
        { value: 'Title', label: 'Episode Title', kind: 'string' },
        { value: 'ShowTitle', label: 'Show', kind: 'string' },
        { value: 'Genre', label: 'Genre', kind: 'string' },
        { value: 'ContentRating', label: 'Content Rating', kind: 'string' },
        { value: 'SeasonNumber', label: 'Season #', kind: 'int' },
        { value: 'EpisodeNumber', label: 'Episode #', kind: 'int' },
        { value: 'ReleaseYear', label: 'Year', kind: 'int' },
        { value: 'DurationSeconds', label: 'Duration (seconds)', kind: 'int' },
        { value: 'ServerAdminRating', label: 'Server Admin Rating', kind: 'int' },
        { value: 'MyRating', label: 'My Rating', kind: 'int' },
        { value: 'AudienceRating', label: 'Audience Rating', kind: 'int' },
        { value: 'IsWatched', label: 'Is Watched', kind: 'bool' },
        { value: 'LastPlayedAt', label: 'Last Played', kind: 'date' },
        { value: 'DateAdded', label: 'Date Added', kind: 'date' }
    ],
    Mixed: []
};

const SORT_OPTIONS_BY_TYPE: Record<PlaylistMediaType, { value: SmartPlaylistSortBy; label: string }[]> = {
    Music: [
        { value: 'Random', label: 'Random' },
        { value: 'Title', label: 'Title' },
        { value: 'ArtistName', label: 'Artist' },
        { value: 'AlbumTitle', label: 'Album' },
        { value: 'Year', label: 'Year' },
        { value: 'DateAdded', label: 'Date Added' },
        { value: 'LastPlayedAt', label: 'Last Played' },
        { value: 'PlayCount', label: 'Play Count' },
        { value: 'DurationSeconds', label: 'Duration' }
    ],
    Movies: [
        { value: 'Random', label: 'Random' },
        { value: 'Title', label: 'Title' },
        { value: 'Year', label: 'Year' },
        { value: 'DateAdded', label: 'Date Added' },
        { value: 'LastPlayedAt', label: 'Last Played' },
        { value: 'DurationSeconds', label: 'Duration' }
    ],
    Shows: [
        { value: 'Random', label: 'Random' },
        { value: 'Title', label: 'Title' },
        { value: 'Year', label: 'Year' },
        { value: 'DateAdded', label: 'Date Added' },
        { value: 'LastPlayedAt', label: 'Last Played' }
    ],
    Mixed: [{ value: 'Random', label: 'Random' }]
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

function getFieldsForType(t: PlaylistMediaType): FieldDef[] {
    return FIELDS_BY_TYPE[t] ?? FIELDS_BY_TYPE.Music;
}

function getOpsForField(field: SmartPlaylistField, fields: FieldDef[]): { value: SmartPlaylistOperator; label: string }[] {
    const def = fields.find(f => f.value === field) ?? fields[0];
    if (!def) return STRING_OPS;
    switch (def.kind) {
        case 'string': return STRING_OPS;
        case 'int': return INT_OPS;
        case 'date': return DATE_OPS;
        case 'bool': return BOOL_OPS;
        case 'guid': return [{ value: 'Equals', label: 'is' }, { value: 'NotEquals', label: 'is not' }];
    }
}

function getFieldKind(field: SmartPlaylistField, fields: FieldDef[]) {
    return fields.find(f => f.value === field)?.kind ?? 'string';
}

function operatorNeedsNoValue(op: SmartPlaylistOperator) {
    return op === 'IsNull' || op === 'IsNotNull';
}

export default function SmartPlaylistEditorModal({ serverId, initialId, initialMediaType = 'Music', onClose, onSaved }: Props) {
    const [name, setName] = useState('');
    const [description, setDescription] = useState('');
    const [mediaType, setMediaType] = useState<PlaylistMediaType>(initialMediaType);
    const [definition, setDefinition] = useState<SmartPlaylistDefinition>(emptyDefinition());

    const [previewCount, setPreviewCount] = useState<number | null>(null);
    const [previewing, setPreviewing] = useState(false);
    const [saving, setSaving] = useState(false);
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState<string | null>(null);

    const fields = useMemo(() => getFieldsForType(mediaType), [mediaType]);
    const sortOptions = useMemo(() => SORT_OPTIONS_BY_TYPE[mediaType] ?? SORT_OPTIONS_BY_TYPE.Music, [mediaType]);

    useEffect(() => {
        if (!initialId) return;
        setLoading(true);
        smartPlaylistService.get(initialId, serverId)
            .then(d => {
                if (!d) return;
                setName(d.name);
                setDescription(d.description ?? '');
                setMediaType(d.mediaType);
                setDefinition(d.definition);
            })
            .catch(err => { console.error(err); setError('Could not load smart playlist.'); })
            .finally(() => setLoading(false));
    }, [initialId, serverId]);

    useEffect(() => {
        let cancelled = false;
        setPreviewing(true);
        const t = setTimeout(() => {
            smartPlaylistService.preview(mediaType, definition, serverId)
                .then(count => { if (!cancelled) setPreviewCount(count); })
                .catch(err => { console.error(err); if (!cancelled) setPreviewCount(null); })
                .finally(() => { if (!cancelled) setPreviewing(false); });
        }, 400);
        return () => { cancelled = true; clearTimeout(t); };
    }, [definition, mediaType, serverId]);

    const updateGroup = (path: number[], updater: (g: SmartPlaylistRuleGroup) => SmartPlaylistRuleGroup) => {
        setDefinition(prev => {
            const root = applyAtPath(prev.root, path, updater);
            return { ...prev, root };
        });
    };

    const addRule = (path: number[]) => {
        const firstField = fields[0]?.value ?? 'Title';
        updateGroup(path, g => ({ ...g, rules: [...g.rules, { field: firstField, operator: 'Contains', value: '' }] }));
    };

    const addGroup = (path: number[]) => {
        updateGroup(path, g => ({ ...g, groups: [...g.groups, emptyRuleGroup()] }));
    };

    const handleSave = async () => {
        if (!name.trim()) { setError('Name is required'); return; }
        setError(null);
        setSaving(true);
        try {
            const request = { name: name.trim(), description: description.trim() || null, mediaType, definition };
            const result = initialId
                ? await smartPlaylistService.update(initialId, request, serverId)
                : await smartPlaylistService.create(request, serverId);
            onSaved(result);
        } catch (err) {
            console.error(err);
            setError('Failed to save. Check console.');
        } finally {
            setSaving(false);
        }
    };

    const typeBadgeColor = mediaType === 'Music' ? 'fuchsia' : mediaType === 'Movies' ? 'sky' : 'amber';

    return (
        <div className="fixed inset-0 z-[200] flex items-center justify-center bg-black/80 backdrop-blur-sm p-4">
            <div className="bg-gray-900 border border-gray-700 rounded-xl shadow-2xl max-w-4xl w-full max-h-[90vh] overflow-y-auto">
                <div className="sticky top-0 bg-gray-900 border-b border-gray-800 px-6 py-4 flex items-center justify-between">
                    <h2 className="text-xl font-bold text-white flex items-center gap-2">
                        <span className={`text-${typeBadgeColor}-400`}>⚙</span>
                        {initialId ? 'Edit Smart Playlist' : 'New Smart Playlist'}
                        <span className={`text-xs uppercase tracking-widest font-bold rounded px-2 py-0.5 bg-${typeBadgeColor}-500/20 text-${typeBadgeColor}-300 border border-${typeBadgeColor}-500/30`}>{mediaType}</span>
                    </h2>
                    <button onClick={onClose} className="text-gray-400 hover:text-white cursor-pointer text-2xl leading-none">×</button>
                </div>

                <div className="p-6 space-y-6">
                    {loading ? (
                        <div className="text-gray-400">Loading…</div>
                    ) : (
                        <>
                            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                                <div>
                                    <label className="block text-sm font-bold text-gray-400 mb-2">Name</label>
                                    <input
                                        autoFocus
                                        type="text"
                                        value={name}
                                        onChange={e => setName(e.target.value)}
                                        className="w-full bg-gray-950 border border-gray-700 rounded-md p-2 text-white outline-none focus:border-fuchsia-500"
                                        placeholder={mediaType === 'Music' ? 'e.g. Heavy rotation rock' : mediaType === 'Movies' ? 'e.g. Unwatched sci-fi' : 'e.g. Unwatched comedy episodes'}
                                    />
                                </div>
                                <div>
                                    <label className="block text-sm font-bold text-gray-400 mb-2">Description (optional)</label>
                                    <input
                                        type="text"
                                        value={description}
                                        onChange={e => setDescription(e.target.value)}
                                        className="w-full bg-gray-950 border border-gray-700 rounded-md p-2 text-white outline-none focus:border-fuchsia-500"
                                    />
                                </div>
                            </div>

                            <div>
                                <div className="text-sm font-bold text-gray-300 mb-2">Rules</div>
                                <RuleGroupEditor
                                    group={definition.root}
                                    path={[]}
                                    fields={fields}
                                    onAddRule={addRule}
                                    onAddGroup={addGroup}
                                    onUpdate={updateGroup}
                                />
                            </div>

                            <div className="grid grid-cols-1 sm:grid-cols-3 gap-4 pt-4 border-t border-gray-800">
                                <div>
                                    <label className="block text-xs font-bold text-gray-500 uppercase tracking-wide mb-2">Sort by</label>
                                    <select
                                        value={definition.sortBy}
                                        onChange={e => setDefinition(d => ({ ...d, sortBy: e.target.value as SmartPlaylistSortBy }))}
                                        className="w-full bg-gray-950 border border-gray-700 rounded p-2 text-white outline-none cursor-pointer"
                                    >
                                        {sortOptions.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
                                    </select>
                                </div>
                                <div>
                                    <label className="block text-xs font-bold text-gray-500 uppercase tracking-wide mb-2">Direction</label>
                                    <select
                                        value={definition.sortDirection}
                                        onChange={e => setDefinition(d => ({ ...d, sortDirection: e.target.value as SmartPlaylistSortDirection }))}
                                        className="w-full bg-gray-950 border border-gray-700 rounded p-2 text-white outline-none cursor-pointer"
                                        disabled={definition.sortBy === 'Random'}
                                    >
                                        <option value="Asc">Ascending</option>
                                        <option value="Desc">Descending</option>
                                    </select>
                                </div>
                                <div>
                                    <label className="block text-xs font-bold text-gray-500 uppercase tracking-wide mb-2">Limit</label>
                                    <input
                                        type="number"
                                        min={0}
                                        max={5000}
                                        value={definition.limit ?? ''}
                                        onChange={e => setDefinition(d => ({ ...d, limit: e.target.value === '' ? undefined : Math.max(0, Number(e.target.value)) }))}
                                        className="w-full bg-gray-950 border border-gray-700 rounded p-2 text-white outline-none"
                                        placeholder="(no limit)"
                                    />
                                </div>
                            </div>

                            <div className="flex items-center gap-3 text-sm text-gray-400 pt-2 border-t border-gray-800">
                                <span>Preview:</span>
                                <span className="font-bold text-white">
                                    {previewing ? '...' : previewCount === null ? '—' : `${previewCount.toLocaleString()} ${mediaType === 'Shows' ? 'episodes' : mediaType === 'Movies' ? 'movies' : 'tracks'}`}
                                </span>
                            </div>

                            {error && <div className="text-rose-400 text-sm">{error}</div>}
                        </>
                    )}
                </div>

                <div className="sticky bottom-0 bg-gray-900 border-t border-gray-800 px-6 py-4 flex justify-end gap-3">
                    <button onClick={onClose} disabled={saving} className="px-4 py-2 rounded text-gray-300 hover:bg-gray-700 transition-colors cursor-pointer">Cancel</button>
                    <button onClick={handleSave} disabled={saving} className="px-6 py-2 bg-fuchsia-600 hover:bg-fuchsia-500 text-white font-bold rounded shadow-lg transition-colors cursor-pointer disabled:opacity-50">
                        {saving ? 'Saving…' : initialId ? 'Save changes' : 'Create'}
                    </button>
                </div>
            </div>
        </div>
    );
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
    const indent = path.length === 0 ? '' : 'border-l-2 border-fuchsia-700/40 pl-4 ml-2';
    return (
        <div className={`${indent} space-y-2`}>
            <div className="flex items-center gap-3 text-sm">
                <span className="text-gray-400">Match</span>
                <select
                    value={group.match}
                    onChange={e => onUpdate(path, g => ({ ...g, match: e.target.value as SmartPlaylistMatch }))}
                    className="bg-gray-950 border border-gray-700 rounded px-2 py-1 text-white outline-none cursor-pointer"
                >
                    <option value="All">All</option>
                    <option value="Any">Any</option>
                </select>
                <span className="text-gray-400">of the following:</span>
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
                        onClick={() => onUpdate(path, g => ({ ...g, groups: g.groups.filter((_, i) => i !== idx) }))}
                        className="absolute -left-2 top-0 w-5 h-5 rounded-full bg-rose-700 hover:bg-rose-500 text-white text-xs cursor-pointer z-10"
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
                <button onClick={() => onAddRule(path)} className="px-3 py-1 text-xs font-bold bg-gray-800 hover:bg-gray-700 text-gray-200 rounded transition-colors cursor-pointer">+ Rule</button>
                <button onClick={() => onAddGroup(path)} className="px-3 py-1 text-xs font-bold bg-gray-800 hover:bg-gray-700 text-gray-200 rounded transition-colors cursor-pointer">+ Group</button>
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
        <div className="flex flex-wrap items-center gap-2 bg-gray-950/60 border border-gray-800 rounded px-2 py-2">
            <select
                value={rule.field}
                onChange={e => {
                    const newField = e.target.value as SmartPlaylistField;
                    const newOps = getOpsForField(newField, fields);
                    onChange(r => ({ ...r, field: newField, operator: newOps[0].value, value: '', secondValue: undefined }));
                }}
                className="bg-gray-900 border border-gray-700 rounded px-2 py-1 text-white text-sm outline-none cursor-pointer"
            >
                {fields.map(f => <option key={f.value} value={f.value}>{f.label}</option>)}
            </select>
            <select
                value={rule.operator}
                onChange={e => onChange(r => ({ ...r, operator: e.target.value as SmartPlaylistOperator }))}
                className="bg-gray-900 border border-gray-700 rounded px-2 py-1 text-white text-sm outline-none cursor-pointer"
            >
                {ops.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
            </select>

            {!noValue && (
                kind === 'bool' ? (
                    <select
                        value={rule.value ?? 'true'}
                        onChange={e => onChange(r => ({ ...r, value: e.target.value }))}
                        className="bg-gray-900 border border-gray-700 rounded px-2 py-1 text-white text-sm outline-none cursor-pointer"
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
                        className="bg-gray-900 border border-gray-700 rounded px-2 py-1 text-white text-sm outline-none w-24"
                        placeholder="days"
                    />
                ) : kind === 'date' ? (
                    <input
                        type="date"
                        value={rule.value ?? ''}
                        onChange={e => onChange(r => ({ ...r, value: e.target.value }))}
                        className="bg-gray-900 border border-gray-700 rounded px-2 py-1 text-white text-sm outline-none"
                    />
                ) : kind === 'int' ? (
                    <>
                        <input
                            type="number"
                            value={rule.value ?? ''}
                            onChange={e => onChange(r => ({ ...r, value: e.target.value }))}
                            className="bg-gray-900 border border-gray-700 rounded px-2 py-1 text-white text-sm outline-none w-24"
                        />
                        {isBetween && (
                            <>
                                <span className="text-gray-500 text-sm">and</span>
                                <input
                                    type="number"
                                    value={rule.secondValue ?? ''}
                                    onChange={e => onChange(r => ({ ...r, secondValue: e.target.value }))}
                                    className="bg-gray-900 border border-gray-700 rounded px-2 py-1 text-white text-sm outline-none w-24"
                                />
                            </>
                        )}
                    </>
                ) : (
                    <input
                        type="text"
                        value={rule.value ?? ''}
                        onChange={e => onChange(r => ({ ...r, value: e.target.value }))}
                        className="bg-gray-900 border border-gray-700 rounded px-2 py-1 text-white text-sm outline-none flex-1 min-w-[120px]"
                    />
                )
            )}

            <button onClick={onRemove} className="ml-auto px-2 py-1 text-rose-400 hover:bg-rose-900/30 rounded text-sm cursor-pointer" title="Remove rule">×</button>
        </div>
    );
}

function applyAtPath(group: SmartPlaylistRuleGroup, path: number[], updater: (g: SmartPlaylistRuleGroup) => SmartPlaylistRuleGroup): SmartPlaylistRuleGroup {
    if (path.length === 0) return updater(group);
    const [head, ...rest] = path;
    return {
        ...group,
        groups: group.groups.map((g, i) => i === head ? applyAtPath(g, rest, updater) : g)
    };
}
