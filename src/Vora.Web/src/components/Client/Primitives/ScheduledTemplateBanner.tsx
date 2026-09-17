import type { TemplateScheduleVM } from '../../../api/System/clientTemplateService';

interface ScheduledTemplateBannerProps {
    schedule: TemplateScheduleVM;
    isOverridden: boolean;
    onRevert?: () => void;
    onApplySchedule?: () => void;
}

function formatLocal(iso: string): string {
    try {
        return new Date(iso).toLocaleString(undefined, {
            weekday: 'short',
            month: 'short',
            day: 'numeric',
            hour: 'numeric',
            minute: '2-digit',
        });
    } catch {
        return iso;
    }
}

export default function ScheduledTemplateBanner({ schedule, isOverridden, onRevert, onApplySchedule }: ScheduledTemplateBannerProps) {
    return (
        <div
            className="relative flex items-center gap-5 overflow-hidden rounded-2xl px-6 py-5"
            style={{
                background: 'linear-gradient(135deg, color-mix(in srgb, var(--vora-accent-500) 30%, var(--vora-bg-surface)), var(--vora-bg-surface))',
                border: '1px solid var(--vora-accent-soft-hover)',
            }}
        >
            <div
                className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl"
                style={{ background: 'var(--vora-accent-soft)', color: 'var(--vora-accent-text)' }}
            >
                <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8">
                    <path d="M8 2v4" />
                    <path d="M16 2v4" />
                    <rect x="3" y="6" width="18" height="15" rx="2" />
                    <path d="M3 10h18" />
                </svg>
            </div>
            <div className="flex-1">
                <h3 className="m-0 text-base font-semibold" style={{ color: 'var(--vora-text-primary)' }}>
                    {isOverridden ? `Your override is active during "${schedule.name}"` : `${schedule.name} template is active`}
                </h3>
                <p className="mt-1 text-sm" style={{ color: 'var(--vora-text-secondary)' }}>
                    {isOverridden
                        ? `Your choice stays until ${formatLocal(schedule.endsAtUtc)}, then your default returns.`
                        : `Your server admin scheduled this template until ${formatLocal(schedule.endsAtUtc)}. Pick a different one and your choice will stick until the schedule ends.`}
                </p>
            </div>
            <div className="flex items-center gap-2 shrink-0">
                {isOverridden && onApplySchedule && (
                    <button type="button" className="vora-button-secondary cursor-pointer" onClick={onApplySchedule}>
                        Use scheduled
                    </button>
                )}
                {onRevert && (
                    <button type="button" className="vora-button-secondary cursor-pointer" onClick={onRevert}>
                        Revert to my default
                    </button>
                )}
            </div>
        </div>
    );
}
