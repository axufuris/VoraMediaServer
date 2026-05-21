import { useEffect, useState, useCallback } from 'react';
import { useParams } from 'react-router-dom';
import { taskService, type BackgroundTask } from '../../../api/System/taskService';
import { useSignalREvent } from '../../../hooks/useSignalREvent';
import PageHeader from '../../../components/Admin/Primitives/PageHeader';
import HealthBadge from '../../../components/Admin/Primitives/HealthBadge';
import EmptyState from '../../../components/Admin/Primitives/EmptyState';

export default function TaskDashboard() {
    const { serverId } = useParams<{ serverId?: string }>();
    const [tasks, setTasks] = useState<BackgroundTask[]>([]);

    const fetchTasks = useCallback(async () => {
        try {
            const data = await taskService.getTasks(serverId);
            setTasks(data);
        } catch (error) {
            console.error('Failed to fetch tasks', error);
        }
    }, [serverId]);

    useEffect(() => {
        let cancelled = false;
        taskService.getTasks(serverId)
            .then(data => { if (!cancelled) setTasks(data); })
            .catch(error => { if (!cancelled) console.error('Failed to fetch tasks', error); });
        return () => { cancelled = true; };
    }, [serverId]);

    useSignalREvent('TasksUpdated', useCallback(() => {
        fetchTasks();
    }, [fetchTasks]));

    const handleCancel = async (taskId: string) => {
        try {
            setTasks(tasks.filter(t => t.id !== taskId));
            await taskService.cancelTask(taskId, serverId);
        } catch (error) {
            console.error('Failed to cancel task', error);
            fetchTasks();
        }
    };

    const runningCount = tasks.filter(t => t.status === 'Running').length;

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
                            {runningCount} running · {tasks.length} total
                        </div>
                    )
                }
            />

            <div className="p-8 max-w-5xl mx-auto">
                {tasks.length === 0 ? (
                    <div className="vora-card">
                        <EmptyState
                            title="All caught up"
                            description="No background tasks are queued or running right now."
                            icon={<svg className="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z" /></svg>}
                        />
                    </div>
                ) : (
                    <div className="vora-card overflow-hidden">
                        <table className="w-full text-left">
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
                                            ) : (
                                                <HealthBadge tone="neutral">Pending</HealthBadge>
                                            )}
                                        </td>
                                        <td className="px-5 py-3 text-sm font-semibold text-[var(--vora-text-primary)]">{task.name}</td>
                                        <td className="px-5 py-3 text-right">
                                            <button
                                                type="button"
                                                onClick={() => handleCancel(task.id)}
                                                className="px-3 py-1 rounded-[var(--vora-radius-md)] text-xs font-semibold text-[var(--vora-danger-text)] bg-[var(--vora-danger-soft)] hover:bg-[var(--vora-danger-500)] hover:text-white transition-colors cursor-pointer"
                                            >
                                                Cancel
                                            </button>
                                        </td>
                                    </tr>
                                ))}
                            </tbody>
                        </table>
                    </div>
                )}
            </div>
        </div>
    );
}
