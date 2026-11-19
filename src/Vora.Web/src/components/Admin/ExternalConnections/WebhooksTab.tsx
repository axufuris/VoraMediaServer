import { useCallback, useEffect, useState } from 'react';
import {
    webhookAdminService,
    type WebhookConnectionVM,
    type WebhookDeliveryVM,
    type WebhookTemplateVM
} from '../../../api/System/webhookAdminService';
import ConnectionModal from './ConnectionModal';
import ConnectionTiles from './ConnectionTiles';
import RecentDeliveries from './RecentDeliveries';
import { useDialog } from '../../../dialogs';

const DELIVERY_LIMIT = 100;
const MINUTE = 60_000;

type ModalState = { kind: 'add' } | { kind: 'edit'; id: string } | null;

interface WebhooksTabProps {
    serverId?: string;
}

export default function WebhooksTab({ serverId }: WebhooksTabProps) {
    const dialog = useDialog();
    const [templates, setTemplates] = useState<WebhookTemplateVM[] | null>(null);
    const [connections, setConnections] = useState<WebhookConnectionVM[] | null>(null);
    const [pickedFilter, setPickedFilter] = useState('');
    const [fetched, setFetched] = useState<{ key: string; list: WebhookDeliveryVM[] } | null>(null);
    const [refreshing, setRefreshing] = useState(false);
    const [modal, setModal] = useState<ModalState>(null);
    const [now, setNow] = useState(() => Date.now());

    const filter = connections?.some(connection => connection.id === pickedFilter) ? pickedFilter : '';
    const deliveriesKey = `${serverId ?? ''}|${filter}`;
    const deliveries = fetched?.key === deliveriesKey ? fetched.list : null;

    const reloadConnections = useCallback(() => webhookAdminService.getConnections(serverId)
        .then(list => {
            setConnections(list);
            setNow(Date.now());
        })
        .catch(console.error), [serverId]);

    useEffect(() => {
        let cancelled = false;
        webhookAdminService.getProviders(serverId)
            .then(list => { if (!cancelled) setTemplates(list); })
            .catch(() => {
                if (cancelled) return;
                setTemplates([]);
                dialog.alert('Failed to load the connection templates.');
            });
        webhookAdminService.getConnections(serverId)
            .then(list => {
                if (cancelled) return;
                setConnections(list);
                setNow(Date.now());
            })
            .catch(() => {
                if (cancelled) return;
                setConnections([]);
                dialog.alert('Failed to load the webhook connections.');
            });
        return () => { cancelled = true; };
    }, [serverId, dialog]);

    useEffect(() => {
        let cancelled = false;
        webhookAdminService.getDeliveries(DELIVERY_LIMIT, filter || undefined, serverId)
            .then(list => {
                if (cancelled) return;
                setFetched({ key: deliveriesKey, list });
                setNow(Date.now());
            })
            .catch(() => {
                if (cancelled) return;
                setFetched({ key: deliveriesKey, list: [] });
                dialog.alert('Failed to load recent deliveries.');
            });
        return () => { cancelled = true; };
    }, [serverId, filter, deliveriesKey, dialog]);

    useEffect(() => {
        const timer = window.setInterval(() => setNow(Date.now()), MINUTE);
        return () => window.clearInterval(timer);
    }, []);

    const refresh = async () => {
        setRefreshing(true);
        try {
            const [list] = await Promise.all([
                webhookAdminService.getDeliveries(DELIVERY_LIMIT, filter || undefined, serverId),
                reloadConnections()
            ]);
            setFetched({ key: deliveriesKey, list });
        } catch {
            await dialog.alert('Failed to refresh recent deliveries.');
        } finally {
            setRefreshing(false);
        }
    };

    const add = () => {
        if (!templates || templates.length === 0) {
            dialog.alert('This server has no connection templates.');
            return;
        }
        setModal({ kind: 'add' });
    };

    const editing = modal?.kind === 'edit' ? connections?.find(connection => connection.id === modal.id) ?? null : null;
    const closeModal = () => setModal(null);

    return (
        <>
            <div className="space-y-6">
                <p className="max-w-2xl text-sm text-[var(--vora-text-secondary)]">
                    These are incoming webhooks, sent to Vora by another app. Radarr, Sonarr, Lidarr or any system that can send a webhook tells Vora when a file is imported, upgraded, renamed or deleted, so the library updates at once. This lets a library's real-time watching be turned off.
                </p>

                <ConnectionTiles
                    connections={connections}
                    now={now}
                    onOpen={connection => setModal({ kind: 'edit', id: connection.id })}
                    onAdd={add}
                />

                <RecentDeliveries
                    deliveries={deliveries}
                    connections={connections ?? []}
                    filter={filter}
                    refreshing={refreshing}
                    onFilterChange={setPickedFilter}
                    onRefresh={refresh}
                />
            </div>

            {modal?.kind === 'add' && templates && (
                <ConnectionModal
                    key="add"
                    connection={null}
                    templates={templates}
                    serverId={serverId}
                    onChanged={reloadConnections}
                    onClose={closeModal}
                />
            )}
            {editing && templates && (
                <ConnectionModal
                    key={editing.id}
                    connection={editing}
                    templates={templates}
                    serverId={serverId}
                    onChanged={reloadConnections}
                    onClose={closeModal}
                />
            )}
        </>
    );
}
