import { useEffect, useState, useMemo } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { calendarService, type CalendarEventVM } from '../../api/Discovery/calendarService';
import PageHeader from '../../components/Client/Primitives/PageHeader';
import { StorageKeys } from '../../utils/storageKeys';
import {
    CalendarViewModes,
    daysInRange,
    isCalendarViewMode,
    rangeTitle,
    step,
    visibleRange,
    type CalendarViewMode,
} from '../../utils/calendarRange';

const VIEW_LABELS: Record<CalendarViewMode, string> = {
    month: 'Month',
    week: 'Week',
    day: 'Day',
};

const readSavedViewMode = (): CalendarViewMode => {
    const saved = localStorage.getItem(StorageKeys.calendarViewMode);
    return isCalendarViewMode(saved) ? saved : 'month';
};

interface EventTheme {
    background: string;
    border: string;
    text: string;
}

const getEventTheme = (mediaType: string): EventTheme => {
    switch (mediaType) {
        case 'Movie':
            return {
                background: 'color-mix(in srgb, #a855f7 18%, transparent)',
                border: 'color-mix(in srgb, #a855f7 45%, transparent)',
                text: '#c4b5fd',
            };
        case 'Track':
        case 'Album':
            return {
                background: 'color-mix(in srgb, #ec4899 18%, transparent)',
                border: 'color-mix(in srgb, #ec4899 45%, transparent)',
                text: '#f9a8d4',
            };
        case 'TvShow':
        case 'Episode':
        default:
            return {
                background: 'color-mix(in srgb, var(--vora-success-500) 18%, transparent)',
                border: 'color-mix(in srgb, var(--vora-success-500) 45%, transparent)',
                text: 'var(--vora-success-text)',
            };
    }
};

function DayAgenda({ day, events, onEventClick }: {
    day: Date | undefined;
    events: CalendarEventVM[];
    onEventClick: (ev: CalendarEventVM) => void;
}) {
    if (!day) return null;

    if (events.length === 0) {
        return (
            <div className="flex flex-1 items-center justify-center p-10 text-sm" style={{ color: 'var(--vora-text-muted)' }}>
                Nothing releases on this day.
            </div>
        );
    }

    return (
        <div className="flex-1 space-y-2 overflow-y-auto p-4" style={{ background: 'var(--vora-bg-canvas)' }}>
            {events.map(ev => {
                const theme = getEventTheme(ev.mediaType);
                return (
                    <button
                        key={ev.id}
                        type="button"
                        onClick={() => onEventClick(ev)}
                        className="flex w-full cursor-pointer items-center gap-3 rounded-lg p-3 text-left transition-colors"
                        style={{ background: theme.background, border: `1px solid ${theme.border}` }}
                    >
                        <span className="w-24 shrink-0 text-xs font-bold uppercase" style={{ color: theme.text }}>
                            {ev.mediaType === 'Movie'
                                ? (ev.releaseType === 'Theatrical' ? 'Theatrical' : 'Digital')
                                : (ev.airTime ? new Date(ev.releaseDate).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' }) : 'TV')}
                        </span>
                        <span className="min-w-0 flex-1">
                            <span className="block truncate text-sm font-semibold" style={{ color: 'var(--vora-text-primary)' }}>{ev.title}</span>
                            {ev.subTitle && <span className="block truncate text-xs" style={{ color: 'var(--vora-text-muted)' }}>{ev.subTitle}</span>}
                        </span>
                        <span className="flex shrink-0 items-center gap-1.5">
                            {ev.isWatchlisted && <span className="h-2 w-2 rounded-full" title="On Watchlist" style={{ background: 'var(--vora-info-500)' }} />}
                            {ev.isInLibrary && <span className="h-2 w-2 rounded-full" title="In Library" style={{ background: 'var(--vora-success-500)' }} />}
                        </span>
                    </button>
                );
            })}
        </div>
    );
}

interface CalendarPageProps {
    embedded?: boolean;
}

export default function CalendarPage({ embedded = false }: CalendarPageProps = {}) {
    const { serverId } = useParams<{ serverId?: string }>();
    const navigate = useNavigate();

    const [events, setEvents] = useState<CalendarEventVM[]>([]);
    const [isLoading, setIsLoading] = useState(true);
    const [currentDate, setCurrentDate] = useState(new Date());
    const [viewMode, setViewMode] = useState<CalendarViewMode>(readSavedViewMode);

    const { startDate, endDate, daysInGrid } = useMemo(() => {
        const { start, end } = visibleRange(viewMode, currentDate);
        return { startDate: start, endDate: end, daysInGrid: daysInRange(start, end) };
    }, [viewMode, currentDate]);

    useEffect(() => {
        const fetchEvents = async () => {
            setIsLoading(true);
            try {
                const data = await calendarService.getEvents(startDate, endDate, serverId);
                setEvents(data);
            } catch (error) {
                console.error('Failed to fetch calendar events', error);
            } finally {
                setIsLoading(false);
            }
        };
        fetchEvents();
    }, [startDate, endDate, serverId]);

    const handlePrevious = () => setCurrentDate(step(viewMode, currentDate, -1));
    const handleNext = () => setCurrentDate(step(viewMode, currentDate, 1));
    const handleToday = () => setCurrentDate(new Date());

    const handleViewModeChange = (mode: CalendarViewMode) => {
        setViewMode(mode);
        localStorage.setItem(StorageKeys.calendarViewMode, mode);
    };

    const handleEventClick = (ev: CalendarEventVM) => {
        const baseRoute = serverId ? `/server/${serverId}` : '';
        if (ev.libraryItemId) {
            navigate(`${baseRoute}/media/${ev.libraryItemId}`);
        } else if (ev.externalId && ev.externalProviderId && ev.externalId !== '0') {
            const discType = ev.mediaType === 'Episode' ? 'TvShow' : ev.mediaType;
            navigate(`${baseRoute}/discovery/${ev.externalProviderId}/${discType}/${ev.externalId}`);
        }
    };

    const rangeLabel = rangeTitle(viewMode, currentDate);

    const eventsOn = (day: Date | undefined) => day
        ? events.filter(e => new Date(e.releaseDate).toDateString() === day.toDateString())
        : [];

    const viewToggle = (
        <div
            className="flex items-center gap-1 rounded-full p-1"
            style={{ background: 'var(--vora-bg-surface)', border: '1px solid var(--vora-border-subtle)' }}
            role="group"
            aria-label="Calendar view"
        >
            {CalendarViewModes.map(mode => {
                const isActive = mode === viewMode;
                return (
                    <button
                        key={mode}
                        type="button"
                        onClick={() => handleViewModeChange(mode)}
                        aria-pressed={isActive}
                        className="cursor-pointer rounded-full px-3 py-1 text-xs font-semibold transition-colors"
                        style={isActive
                            ? { background: 'var(--vora-accent-500)', color: 'var(--vora-accent-contrast)' }
                            : { color: 'var(--vora-text-secondary)' }}
                    >
                        {VIEW_LABELS[mode]}
                    </button>
                );
            })}
        </div>
    );

    const rangeControls = (
        <div
            className="flex items-center gap-2 rounded-full p-1.5"
            style={{ background: 'var(--vora-bg-surface)', border: '1px solid var(--vora-border-subtle)' }}
        >
            <button
                type="button"
                onClick={handlePrevious}
                aria-label={`Previous ${viewMode}`}
                className="inline-flex h-8 w-8 cursor-pointer items-center justify-center rounded-full transition-colors hover:bg-white/5"
                style={{ color: 'var(--vora-text-secondary)' }}
            >
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><polyline points="15 18 9 12 15 6" /></svg>
            </button>
            <button
                type="button"
                onClick={handleToday}
                className="cursor-pointer rounded-full px-3 py-1 text-xs font-semibold transition-colors"
                style={{ background: 'var(--vora-accent-soft)', color: 'var(--vora-accent-text)', border: '1px solid var(--vora-accent-soft-hover)' }}
            >
                Today
            </button>
            <h2 className="m-0 w-44 text-center text-sm font-semibold" style={{ color: 'var(--vora-text-primary)' }}>{rangeLabel}</h2>
            <button
                type="button"
                onClick={handleNext}
                aria-label={`Next ${viewMode}`}
                className="inline-flex h-8 w-8 cursor-pointer items-center justify-center rounded-full transition-colors hover:bg-white/5"
                style={{ color: 'var(--vora-text-secondary)' }}
            >
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><polyline points="9 18 15 12 9 6" /></svg>
            </button>
        </div>
    );

    const navAction = (
        <div className="flex flex-wrap items-center gap-2">
            {viewToggle}
            {rangeControls}
        </div>
    );

    return (
        <div className="flex h-full min-h-0 flex-col pb-6">
            {embedded ? (
                <div className="flex justify-end px-8 pt-4">
                    {navAction}
                </div>
            ) : (
                <PageHeader
                    title="Release Calendar"
                    subtitle="Track upcoming movies, episodes, and watchlist drops."
                    actions={navAction}
                />
            )}

            <div className="relative flex min-h-0 flex-1 flex-col px-8">
                {isLoading && (
                    <div
                        className="pointer-events-none absolute inset-x-8 inset-y-0 z-10 flex items-center justify-center rounded-2xl text-sm font-semibold backdrop-blur-sm"
                        style={{ background: 'color-mix(in srgb, var(--vora-bg-canvas) 50%, transparent)', color: 'var(--vora-accent-text)' }}
                    >
                        Loading events…
                    </div>
                )}

                <div
                    className="flex min-h-[500px] flex-1 flex-col overflow-hidden rounded-2xl"
                    style={{ background: 'var(--vora-bg-surface)', border: '1px solid var(--vora-border-subtle)' }}
                >
                    {viewMode === 'day' ? (
                        <DayAgenda
                            day={daysInGrid[0]}
                            events={eventsOn(daysInGrid[0])}
                            onEventClick={handleEventClick}
                        />
                    ) : (<>
                    <div
                        className="grid shrink-0 grid-cols-7 gap-px"
                        style={{ background: 'var(--vora-border-subtle)', borderBottom: '1px solid var(--vora-border-subtle)' }}
                    >
                        {['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'].map(day => (
                            <div
                                key={day}
                                className="py-2 text-center text-xs font-semibold uppercase tracking-widest"
                                style={{ background: 'var(--vora-bg-canvas)', color: 'var(--vora-text-muted)' }}
                            >
                                {day}
                            </div>
                        ))}
                    </div>

                    <div
                        className="grid min-h-0 flex-1 grid-cols-7 gap-px"
                        style={{ background: 'var(--vora-border-subtle)', gridTemplateRows: `repeat(${daysInGrid.length / 7}, minmax(0, 1fr))` }}
                    >
                        {daysInGrid.map((day, idx) => {
                            const isCurrentMonth = viewMode === 'week' || day.getMonth() === currentDate.getMonth();
                            const isToday = new Date().toDateString() === day.toDateString();
                            const dayEvents = eventsOn(day);

                            return (
                                <div
                                    key={idx}
                                    className="flex min-h-0 flex-col p-2 transition-colors"
                                    style={{
                                        background: isCurrentMonth ? 'var(--vora-bg-canvas)' : 'color-mix(in srgb, var(--vora-bg-canvas) 65%, transparent)',
                                        opacity: isCurrentMonth ? 1 : 0.5,
                                    }}
                                >
                                    <div className="mb-1 flex shrink-0 items-start justify-between">
                                        <span
                                            className="flex h-6 w-6 items-center justify-center rounded-full text-xs font-semibold"
                                            style={isToday
                                                ? { background: 'var(--vora-accent-500)', color: 'var(--vora-accent-contrast)', boxShadow: '0 0 0 2px var(--vora-accent-soft)' }
                                                : { color: 'var(--vora-text-muted)' }}
                                        >
                                            {day.getDate()}
                                        </span>
                                    </div>

                                    <div className="flex-1 space-y-1.5 overflow-y-auto pr-0.5">
                                        {dayEvents.map(ev => {
                                            const theme = getEventTheme(ev.mediaType);
                                            return (
                                                <button
                                                    key={ev.id}
                                                    type="button"
                                                    onClick={() => handleEventClick(ev)}
                                                    className="block w-full cursor-pointer rounded-md p-1.5 text-left transition-all hover:scale-[1.02]"
                                                    style={{
                                                        background: theme.background,
                                                        border: `1px solid ${theme.border}`,
                                                    }}
                                                >
                                                    <div className="mb-0.5 flex items-center justify-between gap-1">
                                                        <span className="truncate text-[10px] font-bold uppercase" style={{ color: theme.text }}>
                                                            {ev.mediaType === 'Movie'
                                                                ? (ev.releaseType === 'Theatrical' ? 'Theatrical' : 'Digital')
                                                                : (ev.airTime ? new Date(ev.releaseDate).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' }) : 'TV')}
                                                        </span>
                                                        <div className="flex shrink-0 items-center gap-1">
                                                            {ev.isWatchlisted && <span className="h-1.5 w-1.5 rounded-full" title="On Watchlist" style={{ background: 'var(--vora-info-500)' }} />}
                                                            {ev.isInLibrary && <span className="h-1.5 w-1.5 rounded-full" title="In Library" style={{ background: 'var(--vora-success-500)' }} />}
                                                        </div>
                                                    </div>
                                                    <div className="truncate text-xs font-semibold" style={{ color: 'var(--vora-text-primary)' }}>{ev.title}</div>
                                                    {ev.subTitle && <div className="truncate text-[10px]" style={{ color: 'var(--vora-text-muted)' }}>{ev.subTitle}</div>}
                                                </button>
                                            );
                                        })}
                                    </div>
                                </div>
                            );
                        })}
                    </div>
                    </>)}
                </div>

                <div className="mt-4 flex shrink-0 flex-wrap justify-center gap-x-8 gap-y-2 text-[11px] font-medium" style={{ color: 'var(--vora-text-muted)' }}>
                    <div className="flex items-center gap-2">
                        <span className="h-2.5 w-2.5 rounded" style={{ background: 'color-mix(in srgb, #a855f7 30%, transparent)', border: '1px solid color-mix(in srgb, #a855f7 50%, transparent)' }} />
                        Movies
                    </div>
                    <div className="flex items-center gap-2">
                        <span className="h-2.5 w-2.5 rounded" style={{ background: 'color-mix(in srgb, var(--vora-success-500) 30%, transparent)', border: '1px solid color-mix(in srgb, var(--vora-success-500) 50%, transparent)' }} />
                        TV Shows
                    </div>
                    <div className="flex items-center gap-2">
                        <span className="h-2.5 w-2.5 rounded" style={{ background: 'color-mix(in srgb, #ec4899 30%, transparent)', border: '1px solid color-mix(in srgb, #ec4899 50%, transparent)' }} />
                        Music
                    </div>
                    <div className="hidden h-3 w-px sm:block" style={{ background: 'var(--vora-border-subtle)' }} />
                    <div className="flex items-center gap-2">
                        <span className="h-1.5 w-1.5 rounded-full" style={{ background: 'var(--vora-info-500)' }} />
                        On Watchlist
                    </div>
                    <div className="flex items-center gap-2">
                        <span className="h-1.5 w-1.5 rounded-full" style={{ background: 'var(--vora-success-500)' }} />
                        In Library
                    </div>
                </div>
            </div>
        </div>
    );
}
