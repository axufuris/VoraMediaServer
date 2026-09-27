import { useEffect, useState, useCallback } from 'react';
import { useParams } from 'react-router-dom';
import { taskService, type BackgroundTask } from '../../../api/System/taskService';
import { useSignalREvent } from '../../../hooks/useSignalREvent';
import PageHeader from '../../../components/Admin/Primitives/PageHeader';
import HealthBadge from '../../../components/Admin/Primitives/HealthBadge';
import EmptyState from '../../../components/Admin/Primitives/EmptyState';

// One screen of rows. A first scan can queue tens of thousands of files, and
// rendering them all made the page unusable — and re-rendered the lot on every
// progress event.
const PageSize = 25;

export default function TaskDashboard() {
    const { serverId } = useParams<{ serverId?: string }>();
    const [tasks, setTasks] = useState<BackgroundTask[]>([]);
    const [total, setTotal] = useState(0);
    const [runningCount, setRunningCount] = useState(0);
    const [page, setPage] = useState(0);

    const pageCount = Math.max(1, Math.ceil(total / PageSize));
    // The queue drains while the page is open, so a page that no longer exists
    // lands the viewer on the last one rather than on an empty table.
    const currentPage = Math.min(page, pageCount - 1);

    const fetchTasks = useCallback(() => {
        taskService.getTasks(currentPage * PageSize, PageSize, serverId)
            .then(result => {
                setTasks(result.items);
                setTotal(result.total);
                setRunningCount(result.running);
            })
            .catch(error => console.error('Failed to fetch tasks', error));
    }, [currentPage, serverId]);

    // SignalR pushes TasksUpdated for instant updates, but a single event missed
    // during a connection blip (or after auto-reconnect gives up) would otherwise
    // leave the page stale until a manual refresh — the task label only changes
    // once per show, so the next event can be ~45 min away. Poll on a short
    // interval as a freshness backstop so progress advances on its own.
    useEffect(() => {
        fetchTasks();
        const interval = setInterval(fetchTasks, 5000);
        return () => clearInterval(interval);
    }, [fetchTasks]);

    useSignalREvent('TasksUpdated', useCallback(() => {
        fetchTasks();
    }, [fetchTasks]));

    const handleCancel = async (taskId: string) => {
        try {
            setTasks(tasks.map(t => t.id === taskId ? { ...t, status: 'Cancelling' } : t));
            await taskService.cancelTask(taskId, serverId);
        } catch (error) {
            console.error('Failed to cancel task', error);
            fetchTasks();
        }
    };

    return (
        <div data-vora-page="">
            <PageHeader
                title="Background Tasks"
                description="Queued and currently-running server jobs."
                actions={
                    tasks.length > 0 && (
                        <div className="flex items-center gap-2 text-xs text-[var(--vora-text-muted)]">
                            <span className="relative flex h-2 w-2">
                                <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-[var(--vora-success-500)] opacity-75"></span>
                                <span className="relative inline-flex rounded-full h-2 w-2 bg-[var(--vora-success-500)]"></span>
                            </span>
                            {runningCount} running · {total} total
                        </div>
                    )
                }
            />

            <div className="p-8 max-w-5xl mx-auto">
                {total === 0 ? (
                    <div className="vora-card">
                        <EmptyState
                            title="All caught up"
                            description="No background tasks are queued or running right now."
                            icon={<svg className="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z" /></svg>}
                        />
                    </div>
                ) : (
                    <div className="vora-card overflow-hidden">
                        <table className="w-full text-left table-fixed">
                            <thead>
                                <tr className="bg-[var(--vora-bg-sunken)] border-b border-[var(--vora-border-subtle)] text-xs uppercase tracking-wider text-[var(--vora-text-muted)]">
                                    <th className="px-5 py-3 font-semibold w-32">Status</th>
                                    <th className="px-5 py-3 font-semibold">Task</th>
                                    <th className="px-5 py-3 font-semibold text-right w-32">Actions</th>
                                </tr>
                            </thead>
                            <tbody className="divide-y divide-[var(--vora-border-subtle)]">
                                {tasks.map(task => (
                                    <tr key={task.id} className="hover:bg-[var(--vora-bg-sunken)]/50 transition-colors">
                                        <td className="px-5 py-3 align-middle">
                                            {task.status === 'Running' ? (
                                                <HealthBadge tone="ok">
                                                    <span className="relative flex h-1.5 w-1.5 mr-0.5">
                                                        <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-[var(--vora-success-500)] opacity-75"></span>
                                                        <span className="relative inline-flex rounded-full h-1.5 w-1.5 bg-[var(--vora-success-500)]"></span>
                                                    </span>
                                                    Running
                                                </HealthBadge>
                                            ) : task.status === 'Cancelling' ? (
                                                <HealthBadge tone="warn">Cancelling…</HealthBadge>
                                            ) : (
                                                <HealthBadge tone="neutral">Pending</HealthBadge>
                                            )}
                                        </td>
                                        <td className="px-5 py-3">
                                            <div className="text-sm font-semibold text-[var(--vora-text-primary)] truncate">{task.name}</div>
                                            {task.progress && (
                                                <div className="mt-0.5 text-xs text-[var(--vora-text-muted)] font-normal truncate">{task.progress}</div>
                                            )}
                                        </td>
                                        <td className="px-5 py-3 text-right">
                                            {task.status === 'Cancelling' ? (
                                                <span className="px-3 py-1 text-xs font-semibold text-[var(--vora-text-muted)]">Cancelling…</span>
                                            ) : (
                                                <button
                                                    type="button"
                                                    onClick={() => handleCancel(task.id)}
                                                    className="px-3 py-1 rounded-[var(--vora-radius-md)] text-xs font-semibold text-[var(--vora-danger-text)] bg-[var(--vora-danger-soft)] hover:bg-[var(--vora-danger-500)] hover:text-[var(--vora-text-primary)] transition-colors cursor-pointer"
                                                >
                                                    Cancel
                                                </button>
                                            )}
                                        </td>
                                    </tr>
                                ))}
                            </tbody>
                        </table>

                        {pageCount > 1 && (
                            <nav aria-label="Task pages" className="flex items-center justify-between gap-3 border-t border-[var(--vora-border-subtle)] px-5 py-3">
                                <span className="text-xs text-[var(--vora-text-muted)]">
                                    Showing {currentPage * PageSize + 1}–{Math.min((currentPage + 1) * PageSize, total)} of {total}
                                </span>
                                <span className="flex items-center gap-2">
                                    <button
                                        type="button"
                                        onClick={() => setPage(p => Math.max(0, p - 1))}
                                        disabled={currentPage === 0}
                                        className="px-3 py-1 rounded-[var(--vora-radius-md)] text-xs font-semibold text-[var(--vora-text-secondary)] bg-[var(--vora-bg-sunken)] hover:text-[var(--vora-text-primary)] disabled:opacity-40 disabled:cursor-default transition-colors cursor-pointer"
                                    >
                                        Previous
                                    </button>
                                    <span className="text-xs text-[var(--vora-text-muted)]">Page {currentPage + 1} of {pageCount}</span>
                                    <button
                                        type="button"
                                        onClick={() => setPage(p => Math.min(pageCount - 1, p + 1))}
                                        disabled={currentPage >= pageCount - 1}
                                        className="px-3 py-1 rounded-[var(--vora-radius-md)] text-xs font-semibold text-[var(--vora-text-secondary)] bg-[var(--vora-bg-sunken)] hover:text-[var(--vora-text-primary)] disabled:opacity-40 disabled:cursor-default transition-colors cursor-pointer"
                                    >
                                        Next
                                    </button>
                                </span>
                            </nav>
                        )}
                    </div>
                )}
            </div>
        </div>
    );
}
