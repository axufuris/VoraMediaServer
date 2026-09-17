import { useEffect, useMemo, useState, type ReactNode } from 'react';
import { NavLink, useParams } from 'react-router-dom';
import { pluginAdminService } from '../../../api/System/pluginAdminService';
import StatusDot from '../Primitives/StatusDot';
import { ADMIN_NAV, Icons, resolveAdminPath, type AdminNavEntry, type IconName, type NavSection } from './adminNavData';

interface NavItemProps {
    to: string;
    label: string;
    icon: IconName;
    end?: boolean;
    statusTone?: 'ok' | 'warn' | 'error' | 'info' | 'neutral';
}

function NavItem({ to, label, icon, end, statusTone }: NavItemProps) {
    return (
        <NavLink
            to={to}
            end={end}
            className={({ isActive }) =>
                `relative group flex items-center gap-3 pl-4 pr-3 py-2 rounded-[var(--vora-radius-md)] text-sm font-medium transition-colors ${
                    isActive
                        ? 'bg-[var(--vora-accent-soft)] text-[var(--vora-accent-text)]'
                        : 'text-[var(--vora-text-secondary)] hover:bg-[var(--vora-bg-sunken)] hover:text-[var(--vora-text-primary)]'
                }`
            }
        >
            {({ isActive }) => (
                <>
                    {isActive && (
                        <span className="absolute left-0 top-1.5 bottom-1.5 w-0.5 bg-[var(--vora-accent-500)] rounded-full" />
                    )}
                    <svg className="w-4 h-4 shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">{Icons[icon]}</svg>
                    <span className="flex-1 truncate">{label}</span>
                    {statusTone && <StatusDot tone={statusTone} />}
                </>
            )}
        </NavLink>
    );
}

function SidebarSection({ title, children }: { title: string, children: ReactNode }) {
    return (
        <div>
            <h3 className="px-3 mb-1.5 text-[10px] font-bold text-[var(--vora-text-muted)] uppercase tracking-widest">{title}</h3>
            <div className="space-y-0.5">{children}</div>
        </div>
    );
}

export default function SidebarV2() {
    const { serverId } = useParams<{ serverId?: string }>();
    const [hasAiPlugin, setHasAiPlugin] = useState(false);
    const [pluginsTone, setPluginsTone] = useState<'ok' | 'warn' | 'error' | 'info' | 'neutral' | undefined>(undefined);

    useEffect(() => {
        pluginAdminService.getPlugins(serverId)
            .then(plugins => {
                setHasAiPlugin(plugins.some(p => p.isAiPlugin && p.isEnabled));
                // Wave 1: a soft warning indicator when 0 plugins are enabled — gives the dot a chance to exist.
                // Real health states come in a later wave.
                const enabledCount = plugins.filter(p => p.isEnabled).length;
                if (plugins.length > 0 && enabledCount === 0) setPluginsTone('warn');
            })
            .catch(console.error);
    }, [serverId]);

    // Filter the shared nav data by runtime conditions, then group by section
    // in their declared order. Sections render in the order their first entry
    // appears in ADMIN_NAV.
    const grouped = useMemo(() => {
        const visible = ADMIN_NAV.filter(e => {
            if (e.requires === 'ai' && !hasAiPlugin) return false;
            return true;
        });

        const sectionOrder: NavSection[] = [];
        const bySection = new Map<NavSection, AdminNavEntry[]>();
        for (const entry of visible) {
            if (!bySection.has(entry.section)) {
                sectionOrder.push(entry.section);
                bySection.set(entry.section, []);
            }
            bySection.get(entry.section)!.push(entry);
        }
        return sectionOrder.map(s => ({ section: s, entries: bySection.get(s)! }));
    }, [hasAiPlugin]);

    return (
        <aside
            className="w-[var(--vora-shell-sidebar-w)] shrink-0 bg-[var(--vora-bg-surface)] border-r border-[var(--vora-border-subtle)] flex flex-col"
        >
            <div className="flex-1 overflow-y-auto px-3 py-4 space-y-6">
                {grouped.map(({ section, entries }) => (
                    <SidebarSection key={section} title={section}>
                        {entries.map(entry => (
                            <NavItem
                                key={entry.pathTemplate}
                                to={resolveAdminPath(entry.pathTemplate, serverId)}
                                label={entry.label}
                                icon={entry.icon}
                                end={entry.end}
                                statusTone={entry.pathTemplate === '/admin/plugins' ? pluginsTone : undefined}
                            />
                        ))}
                    </SidebarSection>
                ))}
            </div>

            <div className="border-t border-[var(--vora-border-subtle)] px-3 py-3">
                <NavLink
                    to={serverId ? `/server/${serverId}` : '/'}
                    className="flex items-center gap-2 px-3 py-2 rounded-[var(--vora-radius-md)] text-xs font-medium text-[var(--vora-text-muted)] hover:text-[var(--vora-text-primary)] hover:bg-[var(--vora-bg-sunken)] transition-colors"
                >
                    <svg className="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15 19l-7-7 7-7" /></svg>
                    Back to Vora client
                </NavLink>
            </div>
        </aside>
    );
}
