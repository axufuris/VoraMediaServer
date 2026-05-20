import { useEffect, useMemo, useState } from 'react';
import { useParams } from 'react-router-dom';
import {
    clientTemplateService,
    type TemplateMetaVM,
    type TemplateScheduleVM,
    type CreateTemplateScheduleRequest,
} from '../../../api/System/clientTemplateService';
import { useDialog } from '../../../dialogs';
import PageHeader from '../../../components/Admin/Primitives/PageHeader';
import HealthBadge from '../../../components/Admin/Primitives/HealthBadge';
import EmptyState from '../../../components/Admin/Primitives/EmptyState';
import { Modal } from '../../../components/Common/Modal';

interface ScheduleFormState {
    id: string | null;
    templateId: string;
    name: string;
    startsAtLocal: string;
    endsAtLocal: string;
    priority: number;
    enabled: boolean;
}

interface TemplateSwatch {
    canvas: string;
    surface: string;
    raised: string;
    accent: string;
    accentText: string;
    text: string;
    textMuted: string;
    border: string;
}

const BUILT_IN_SWATCHES: Record<string, TemplateSwatch> = {
    'vora-cinema': { canvas: '#08080b', surface: '#101015', raised: '#1a1a20', accent: '#f59e0b', accentText: '#fbbf24', text: '#fafafa', textMuted: '#a1a1aa', border: '#27272a' },
    'vora-noir':   { canvas: '#000000', surface: '#0a0a0a', raised: '#16161a', accent: '#94a3b8', accentText: '#cbd5e1', text: '#f8fafc', textMuted: '#94a3b8', border: '#1f1f25' },
    'vora-velvet': { canvas: '#1a0a0e', surface: '#2a1218', raised: '#3a1a22', accent: '#c2410c', accentText: '#fed7aa', text: '#fef9c3', textMuted: '#fde68a', border: '#451a25' },
    'vora-aurora': { canvas: '#020617', surface: '#0c1726', raised: '#15233a', accent: '#14b8a6', accentText: '#5eead4', text: '#e0f2fe', textMuted: '#94a3b8', border: '#1e2a44' },
    default:       { canvas: '#0a0a0e', surface: '#181820', raised: '#26262e', accent: '#9090a0', accentText: '#c4c4cc', text: '#fafafa', textMuted: '#a1a1aa', border: '#2e2e36' },
};

function TemplatePreview({ template }: { template: TemplateMetaVM }) {
    if (template.preview) {
        return (
            <div className="h-32 overflow-hidden" style={{ borderBottom: '1px solid var(--vora-border-subtle)' }}>
                <img src={template.preview} alt={`${template.name} preview`} className="h-full w-full object-cover" />
            </div>
        );
    }

    const s = BUILT_IN_SWATCHES[template.id] ?? BUILT_IN_SWATCHES.default;
    return (
        <div
            className="relative h-32 overflow-hidden"
            style={{ background: s.canvas, borderBottom: '1px solid var(--vora-border-subtle)' }}
            aria-label={`${template.name} preview`}
        >
            <div style={{ position: 'absolute', left: 0, top: 0, bottom: 0, width: 26, background: s.surface, borderRight: `1px solid ${s.border}` }} />
            <div style={{ position: 'absolute', left: 5, top: 8, height: 5, width: 16, borderRadius: 1.5, background: s.accent }} />
            <div style={{ position: 'absolute', left: 5, top: 18, height: 3, width: 12, borderRadius: 1, background: s.textMuted, opacity: 0.55 }} />
            <div style={{ position: 'absolute', left: 5, top: 26, height: 3, width: 14, borderRadius: 1, background: s.textMuted, opacity: 0.55 }} />
            <div style={{ position: 'absolute', left: 5, top: 34, height: 3, width: 10, borderRadius: 1, background: s.textMuted, opacity: 0.4 }} />

            <div style={{ position: 'absolute', left: 36, top: 10, height: 5, width: 70, borderRadius: 1.5, background: s.text, opacity: 0.95 }} />
            <div style={{ position: 'absolute', left: 36, top: 20, height: 3, width: 110, borderRadius: 1, background: s.textMuted, opacity: 0.6 }} />

            <div
                style={{
                    position: 'absolute',
                    left: 36,
                    right: 12,
                    top: 32,
                    height: 76,
                    borderRadius: 6,
                    background: s.surface,
                    border: `1px solid ${s.border}`,
                    overflow: 'hidden',
                }}
            >
                <div style={{ position: 'absolute', inset: 0, background: `linear-gradient(120deg, ${s.raised} 0%, ${s.surface} 100%)` }} />
                <div style={{ position: 'absolute', left: 8, top: 10, height: 4, width: 50, borderRadius: 1, background: s.text, opacity: 0.9 }} />
                <div style={{ position: 'absolute', left: 8, top: 18, height: 3, width: 36, borderRadius: 1, background: s.textMuted, opacity: 0.6 }} />

                <div
                    style={{
                        position: 'absolute',
                        left: 8,
                        bottom: 8,
                        height: 18,
                        width: 44,
                        borderRadius: 4,
                        background: s.accent,
                        display: 'flex',
                        alignItems: 'center',
                        paddingLeft: 8,
                    }}
                >
                    <div style={{ width: 0, height: 0, borderLeft: `6px solid ${s.text}`, borderTop: '4px solid transparent', borderBottom: '4px solid transparent' }} />
                    <div style={{ marginLeft: 6, height: 3, width: 20, borderRadius: 1, background: s.text, opacity: 0.9 }} />
                </div>
                <div
                    style={{
                        position: 'absolute',
                        left: 58,
                        bottom: 8,
                        height: 18,
                        width: 44,
                        borderRadius: 4,
                        border: `1px solid ${s.border}`,
                        background: 'transparent',
                    }}
                />

                <div style={{ position: 'absolute', right: 10, top: 10, height: 26, width: 22, borderRadius: 3, background: s.raised, border: `1px solid ${s.border}` }} />
            </div>
        </div>
    );
}

function emptyForm(defaultTemplateId: string): ScheduleFormState {
    const now = new Date();
    const tomorrow = new Date(now.getTime() + 24 * 60 * 60 * 1000);
    const weekFromNow = new Date(now.getTime() + 8 * 24 * 60 * 60 * 1000);
    return {
        id: null,
        templateId: defaultTemplateId,
        name: '',
        startsAtLocal: toLocalInputValue(tomorrow),
        endsAtLocal: toLocalInputValue(weekFromNow),
        priority: 0,
        enabled: true,
    };
}

function toLocalInputValue(d: Date): string {
    const pad = (n: number) => n.toString().padStart(2, '0');
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

function formatRange(startsAtUtc: string, endsAtUtc: string): string {
    const opts: Intl.DateTimeFormatOptions = { weekday: 'short', month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' };
    return `${new Date(startsAtUtc).toLocaleString(undefined, opts)} → ${new Date(endsAtUtc).toLocaleString(undefined, opts)}`;
}

function bucket(s: TemplateScheduleVM, now: Date): 'active' | 'upcoming' | 'past' | 'disabled' {
    if (!s.enabled) return 'disabled';
    const start = new Date(s.startsAtUtc);
    const end = new Date(s.endsAtUtc);
    if (now < start) return 'upcoming';
    if (now >= end) return 'past';
    return 'active';
}

export default function AdminTemplateSchedulesPage() {
    const dialog = useDialog();
    const { serverId } = useParams<{ serverId?: string }>();

    const [templates, setTemplates] = useState<TemplateMetaVM[]>([]);
    const [schedules, setSchedules] = useState<TemplateScheduleVM[]>([]);
    const [defaultId, setDefaultId] = useState<string>('vora-cinema');
    const [isLoading, setIsLoading] = useState(true);
    const [savingDefault, setSavingDefault] = useState<string | null>(null);
    const [rescanning, setRescanning] = useState(false);

    const [isFormOpen, setIsFormOpen] = useState(false);
    const [form, setForm] = useState<ScheduleFormState>(() => emptyForm('vora-cinema'));
    const [submittingForm, setSubmittingForm] = useState(false);

    const loadAll = async () => {
        try {
            const [tpls, schs, def] = await Promise.all([
                clientTemplateService.getAll(serverId),
                clientTemplateService.getSchedules(serverId),
                clientTemplateService.getDefault(serverId),
            ]);
            setTemplates(tpls);
            setSchedules(schs);
            setDefaultId(def);
        } catch (err) {
            console.error('Failed to load template admin data', err);
        } finally {
            setIsLoading(false);
        }
    };

    useEffect(() => {
        void loadAll();
    }, [serverId]);

    const handleSetDefault = async (templateId: string) => {
        if (templateId === defaultId) return;
        setSavingDefault(templateId);
        try {
            await clientTemplateService.setDefault(templateId, serverId);
            setDefaultId(templateId);
        } catch (err) {
            console.error('Failed to set default template', err);
            await dialog.alert({ title: 'Could not set default', message: 'Please try again.', tone: 'danger' });
        } finally {
            setSavingDefault(null);
        }
    };

    const handleRescan = async () => {
        setRescanning(true);
        try {
            const count = await clientTemplateService.rescan(serverId);
            await loadAll();
            await dialog.alert({ title: 'Rescan complete', message: `${count} plugin template${count === 1 ? '' : 's'} loaded.`, tone: 'success' });
        } catch (err) {
            console.error('Failed to rescan templates', err);
            await dialog.alert({ title: 'Rescan failed', message: 'Check the server log for details.', tone: 'danger' });
        } finally {
            setRescanning(false);
        }
    };

    const openCreate = () => {
        setForm(emptyForm(defaultId));
        setIsFormOpen(true);
    };

    const openEdit = (s: TemplateScheduleVM) => {
        setForm({
            id: s.id,
            templateId: s.templateId,
            name: s.name,
            startsAtLocal: toLocalInputValue(new Date(s.startsAtUtc)),
            endsAtLocal: toLocalInputValue(new Date(s.endsAtUtc)),
            priority: s.priority,
            enabled: s.enabled,
        });
        setIsFormOpen(true);
    };

    const handleDelete = async (s: TemplateScheduleVM) => {
        const ok = await dialog.confirm({
            title: `Delete "${s.name}"?`,
            message: 'This schedule will be removed. Profiles with overrides tied to it will revert to their personal defaults.',
            confirmText: 'Delete',
            tone: 'danger',
        });
        if (!ok) return;
        try {
            await clientTemplateService.deleteSchedule(s.id, serverId);
            setSchedules(prev => prev.filter(x => x.id !== s.id));
        } catch (err) {
            console.error('Failed to delete schedule', err);
            await dialog.alert({ title: 'Delete failed', message: 'Please try again.', tone: 'danger' });
        }
    };

    const handleSubmitForm = async () => {
        const startsAtUtc = new Date(form.startsAtLocal).toISOString();
        const endsAtUtc = new Date(form.endsAtLocal).toISOString();
        if (!form.templateId) { await dialog.alert({ title: 'Template required', message: 'Pick a template to schedule.', tone: 'danger' }); return; }
        if (!form.name.trim()) { await dialog.alert({ title: 'Name required', message: 'Give the schedule a short name like "Thanksgiving".', tone: 'danger' }); return; }
        if (new Date(endsAtUtc) <= new Date(startsAtUtc)) { await dialog.alert({ title: 'End must be after start', message: 'Pick an end time after the start time.', tone: 'danger' }); return; }

        const payload: CreateTemplateScheduleRequest = {
            templateId: form.templateId,
            name: form.name.trim(),
            startsAtUtc,
            endsAtUtc,
            priority: form.priority,
            enabled: form.enabled,
        };

        setSubmittingForm(true);
        try {
            if (form.id) {
                const updated = await clientTemplateService.updateSchedule(form.id, payload, serverId);
                setSchedules(prev => prev.map(s => s.id === updated.id ? updated : s));
            } else {
                const created = await clientTemplateService.createSchedule(payload, serverId);
                setSchedules(prev => [...prev, created]);
            }
            setIsFormOpen(false);
        } catch (err) {
            console.error('Failed to save schedule', err);
            await dialog.alert({ title: 'Could not save schedule', message: 'Please try again.', tone: 'danger' });
        } finally {
            setSubmittingForm(false);
        }
    };

    const now = useMemo(() => new Date(), [schedules]);
    const grouped = useMemo(() => {
        const groups: Record<'active' | 'upcoming' | 'past' | 'disabled', TemplateScheduleVM[]> = { active: [], upcoming: [], past: [], disabled: [] };
        for (const s of schedules) groups[bucket(s, now)].push(s);
        for (const key of ['active', 'upcoming', 'past', 'disabled'] as const) {
            groups[key].sort((a, b) => new Date(a.startsAtUtc).getTime() - new Date(b.startsAtUtc).getTime());
        }
        return groups;
    }, [schedules, now]);

    const renderScheduleCard = (s: TemplateScheduleVM, status: 'active' | 'upcoming' | 'past' | 'disabled') => {
        const tone = status === 'active' ? 'ok' : status === 'upcoming' ? 'info' : status === 'disabled' ? 'neutral' : 'neutral';
        const label = status === 'active' ? 'Active now' : status === 'upcoming' ? 'Upcoming' : status === 'past' ? 'Ended' : 'Disabled';
        const templateName = templates.find(t => t.id === s.templateId)?.name ?? s.templateId;
        return (
            <div key={s.id} className="vora-card p-5">
                <div className="mb-2 flex items-start justify-between gap-3">
                    <div>
                        <h3 className="m-0 text-base font-semibold" style={{ color: 'var(--vora-text-primary)' }}>{s.name}</h3>
                        <p className="m-0 mt-0.5 text-sm" style={{ color: 'var(--vora-text-muted)' }}>{templateName}</p>
                    </div>
                    <div className="flex flex-wrap gap-1.5">
                        <HealthBadge tone={tone}>{label}</HealthBadge>
                        {s.templateMissing && <HealthBadge tone="warn">Template missing</HealthBadge>}
                    </div>
                </div>
                <p className="m-0 text-xs" style={{ color: 'var(--vora-text-secondary)' }}>{formatRange(s.startsAtUtc, s.endsAtUtc)}</p>
                <p className="m-0 mt-1 text-xs" style={{ color: 'var(--vora-text-muted)' }}>Priority {s.priority}</p>
                <div className="mt-4 flex justify-end gap-2 border-t pt-3" style={{ borderColor: 'var(--vora-border-subtle)' }}>
                    <button type="button" onClick={() => openEdit(s)} className="vora-button-secondary cursor-pointer text-xs">Edit</button>
                    <button type="button" onClick={() => handleDelete(s)} className="vora-button-secondary cursor-pointer text-xs" style={{ color: 'var(--vora-danger-text)' }}>Delete</button>
                </div>
            </div>
        );
    };

    const headerActions = (
        <>
            <button type="button" onClick={handleRescan} disabled={rescanning} className="vora-button-secondary cursor-pointer disabled:opacity-50">
                {rescanning ? 'Rescanning…' : 'Rescan plugins'}
            </button>
            <button type="button" onClick={openCreate} className="vora-button-primary cursor-pointer">New schedule</button>
        </>
    );

    return (
        <div data-vora-page="" className="min-h-full pb-16">
            <PageHeader
                title="Client templates"
                description="Manage the default look of the client and schedule seasonal overrides."
                actions={headerActions}
            />

            <div className="px-8 pt-6">
                <section className="mb-8">
                    <h2 className="m-0 mb-1 text-base font-semibold" style={{ color: 'var(--vora-text-primary)' }}>Default template</h2>
                    <p className="m-0 mb-4 text-sm" style={{ color: 'var(--vora-text-muted)' }}>
                        The template applied to any profile that hasn't picked one of their own. Profiles can still override during an active schedule.
                    </p>
                    {isLoading ? (
                        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
                            {[1, 2, 3].map(i => <div key={i} className="vora-skeleton h-32" />)}
                        </div>
                    ) : (
                        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
                            {templates.map(t => {
                                const isDefault = t.id === defaultId;
                                return (
                                    <div
                                        key={t.id}
                                        className="vora-card overflow-hidden"
                                        style={isDefault ? { boxShadow: '0 0 0 2px var(--vora-accent-500)' } : undefined}
                                    >
                                        <TemplatePreview template={t} />
                                        <div className="p-5">
                                            <div className="mb-1 flex items-start justify-between gap-2">
                                                <h3 className="m-0 text-sm font-semibold" style={{ color: 'var(--vora-text-primary)' }}>{t.name}</h3>
                                                <div className="flex gap-1.5">
                                                    {t.isBuiltIn ? <HealthBadge tone="neutral" showDot={false}>Built-in</HealthBadge> : <HealthBadge tone="info" showDot={false}>Plugin</HealthBadge>}
                                                    {isDefault && <HealthBadge tone="ok">Default</HealthBadge>}
                                                </div>
                                            </div>
                                            {t.description && <p className="m-0 text-xs" style={{ color: 'var(--vora-text-muted)' }}>{t.description}</p>}
                                            <div className="mt-4 flex justify-end border-t pt-3" style={{ borderColor: 'var(--vora-border-subtle)' }}>
                                                {isDefault ? (
                                                    <span className="text-xs font-semibold" style={{ color: 'var(--vora-text-muted)' }}>Server default</span>
                                                ) : (
                                                    <button
                                                        type="button"
                                                        onClick={() => handleSetDefault(t.id)}
                                                        disabled={savingDefault === t.id}
                                                        className="vora-button-primary cursor-pointer text-xs disabled:opacity-50"
                                                    >
                                                        {savingDefault === t.id ? 'Applying…' : 'Set as default'}
                                                    </button>
                                                )}
                                            </div>
                                        </div>
                                    </div>
                                );
                            })}
                        </div>
                    )}
                </section>

                <section>
                    <h2 className="m-0 mb-4 text-base font-semibold" style={{ color: 'var(--vora-text-primary)' }}>Schedules</h2>
                    {isLoading ? (
                        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
                            {[1, 2, 3].map(i => <div key={i} className="vora-skeleton h-32" />)}
                        </div>
                    ) : schedules.length === 0 ? (
                        <EmptyState
                            title="No schedules yet"
                            description="Schedule a template for a date range — e.g. Thanksgiving week. Profiles can opt out by picking a different template; their choice persists until the schedule ends, then reverts to their default."
                            action={(
                                <button type="button" onClick={openCreate} className="vora-button-primary cursor-pointer">Create the first schedule</button>
                            )}
                        />
                    ) : (
                        <div className="space-y-8">
                            {grouped.active.length > 0 && (
                                <div>
                                    <h3 className="m-0 mb-3 text-xs font-semibold uppercase tracking-widest" style={{ color: 'var(--vora-success-text)' }}>Active now</h3>
                                    <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
                                        {grouped.active.map(s => renderScheduleCard(s, 'active'))}
                                    </div>
                                </div>
                            )}
                            {grouped.upcoming.length > 0 && (
                                <div>
                                    <h3 className="m-0 mb-3 text-xs font-semibold uppercase tracking-widest" style={{ color: 'var(--vora-info-text)' }}>Upcoming</h3>
                                    <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
                                        {grouped.upcoming.map(s => renderScheduleCard(s, 'upcoming'))}
                                    </div>
                                </div>
                            )}
                            {grouped.disabled.length > 0 && (
                                <div>
                                    <h3 className="m-0 mb-3 text-xs font-semibold uppercase tracking-widest" style={{ color: 'var(--vora-text-muted)' }}>Disabled</h3>
                                    <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
                                        {grouped.disabled.map(s => renderScheduleCard(s, 'disabled'))}
                                    </div>
                                </div>
                            )}
                            {grouped.past.length > 0 && (
                                <div>
                                    <h3 className="m-0 mb-3 text-xs font-semibold uppercase tracking-widest" style={{ color: 'var(--vora-text-muted)' }}>Past</h3>
                                    <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
                                        {grouped.past.map(s => renderScheduleCard(s, 'past'))}
                                    </div>
                                </div>
                            )}
                        </div>
                    )}
                </section>
            </div>

            <Modal
                isOpen={isFormOpen}
                onClose={() => { if (!submittingForm) setIsFormOpen(false); }}
                size="lg"
                surface="light"
                closeOnBackdropClick={!submittingForm}
            >
                <div className="p-6">
                    <h2 className="m-0 mb-1 text-xl font-semibold" style={{ color: 'var(--vora-text-primary)' }}>
                        {form.id ? 'Edit schedule' : 'New schedule'}
                    </h2>
                    <p className="m-0 mb-5 text-sm" style={{ color: 'var(--vora-text-muted)' }}>
                        Apply a template across all profiles for a window of time. Times are stored in UTC; the inputs below use your local time zone.
                    </p>

                    <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
                        <div className="md:col-span-2">
                            <label className="mb-1 block text-xs font-semibold uppercase tracking-wider" style={{ color: 'var(--vora-text-muted)' }}>Name</label>
                            <input
                                type="text"
                                value={form.name}
                                onChange={e => setForm({ ...form, name: e.target.value })}
                                placeholder='e.g. "Thanksgiving 2026"'
                                className="vora-input w-full"
                            />
                        </div>
                        <div>
                            <label className="mb-1 block text-xs font-semibold uppercase tracking-wider" style={{ color: 'var(--vora-text-muted)' }}>Template</label>
                            <select
                                value={form.templateId}
                                onChange={e => setForm({ ...form, templateId: e.target.value })}
                                className="vora-input w-full cursor-pointer"
                            >
                                {templates.map(t => <option key={t.id} value={t.id}>{t.name}</option>)}
                            </select>
                        </div>
                        <div>
                            <label className="mb-1 block text-xs font-semibold uppercase tracking-wider" style={{ color: 'var(--vora-text-muted)' }}>Priority</label>
                            <input
                                type="number"
                                value={form.priority}
                                onChange={e => setForm({ ...form, priority: parseInt(e.target.value, 10) || 0 })}
                                className="vora-input w-full"
                            />
                            <p className="m-0 mt-1 text-[11px]" style={{ color: 'var(--vora-text-muted)' }}>Higher priority wins when ranges overlap.</p>
                        </div>
                        <div>
                            <label className="mb-1 block text-xs font-semibold uppercase tracking-wider" style={{ color: 'var(--vora-text-muted)' }}>Starts</label>
                            <input
                                type="datetime-local"
                                value={form.startsAtLocal}
                                onChange={e => setForm({ ...form, startsAtLocal: e.target.value })}
                                className="vora-input w-full"
                            />
                        </div>
                        <div>
                            <label className="mb-1 block text-xs font-semibold uppercase tracking-wider" style={{ color: 'var(--vora-text-muted)' }}>Ends</label>
                            <input
                                type="datetime-local"
                                value={form.endsAtLocal}
                                onChange={e => setForm({ ...form, endsAtLocal: e.target.value })}
                                className="vora-input w-full"
                            />
                        </div>
                        <div className="md:col-span-2">
                            <label className="flex cursor-pointer items-center gap-3">
                                <input
                                    type="checkbox"
                                    checked={form.enabled}
                                    onChange={e => setForm({ ...form, enabled: e.target.checked })}
                                    className="h-4 w-4 cursor-pointer accent-[var(--vora-accent-500)]"
                                />
                                <span className="text-sm font-medium" style={{ color: 'var(--vora-text-primary)' }}>Enabled</span>
                                <span className="text-xs" style={{ color: 'var(--vora-text-muted)' }}>If off, this schedule never activates.</span>
                            </label>
                        </div>
                    </div>

                    <div className="mt-6 flex justify-end gap-2">
                        <button
                            type="button"
                            onClick={() => setIsFormOpen(false)}
                            disabled={submittingForm}
                            className="vora-button-secondary cursor-pointer disabled:opacity-50"
                        >
                            Cancel
                        </button>
                        <button
                            type="button"
                            onClick={handleSubmitForm}
                            disabled={submittingForm}
                            className="vora-button-primary cursor-pointer disabled:opacity-50"
                        >
                            {submittingForm ? 'Saving…' : form.id ? 'Save changes' : 'Create schedule'}
                        </button>
                    </div>
                </div>
            </Modal>
        </div>
    );
}
