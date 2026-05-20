import ServerSwitcher from './ServerSwitcher';
import Breadcrumb from './Breadcrumb';
import GlobalSearchTrigger from './GlobalSearchTrigger';
import ActivityPill from './ActivityPill';
import AccountMenu from './AccountMenu';
import AdminNotificationBell from '../AdminNotificationBell';

interface TopAppBarProps {
    onToggleSidebar?: () => void;
    onOpenSearch?: () => void;
}

export default function TopAppBar({ onToggleSidebar, onOpenSearch }: TopAppBarProps) {
    return (
        <header
            className="h-[var(--vora-shell-topbar-h)] shrink-0 bg-[var(--vora-bg-surface)] border-b border-[var(--vora-border-subtle)] flex items-center px-4 gap-3"
        >
            {onToggleSidebar && (
                <button
                    type="button"
                    onClick={onToggleSidebar}
                    className="p-2 rounded-[var(--vora-radius-md)] text-[var(--vora-text-secondary)] hover:text-[var(--vora-text-primary)] hover:bg-[var(--vora-bg-sunken)] cursor-pointer transition-colors lg:hidden"
                    title="Toggle navigation"
                >
                    <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 6h16M4 12h16M4 18h16" /></svg>
                </button>
            )}

            <ServerSwitcher />

            <div className="h-5 w-px bg-[var(--vora-border-subtle)] mx-1 hidden md:block" />

            <div className="flex-1 min-w-0 hidden md:flex">
                <Breadcrumb />
            </div>

            <div className="ml-auto flex items-center gap-2">
                <div className="hidden md:block">
                    <GlobalSearchTrigger onClick={onOpenSearch} />
                </div>
                <ActivityPill />
                <AdminNotificationBell />
                <AccountMenu />
            </div>
        </header>
    );
}
