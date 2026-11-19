import { useCallback, useEffect, useState } from 'react';
import { useParams } from 'react-router-dom';
import { webhookAdminService, type WebhookDeliveryVM, type WebhookProviderVM } from '../../api/System/webhookAdminService';
import PageHeader from '../../components/Admin/Primitives/PageHeader';
import WebhookProviderCard from '../../components/Admin/LibraryUpdates/WebhookProviderCard';
import RecentDeliveries from '../../components/Admin/LibraryUpdates/RecentDeliveries';
import { useDialog } from '../../dialogs';

const DELIVERY_LIMIT = 100;

export default function LibraryUpdatesPage() {
    const { serverId } = useParams<{ serverId?: string }>();
    const dialog = useDialog();
    const [providers, setProviders] = useState<WebhookProviderVM[] | null>(null);
    const [deliveries, setDeliveries] = useState<WebhookDeliveryVM[] | null>(null);
    const [refreshing, setRefreshing] = useState(false);

    const showModal = useCallback((title: string, message: string, isError: boolean = false) => {
        dialog.alert({ title, message, tone: isError ? 'danger' : 'default' });
    }, [dialog]);

    useEffect(() => {
        let cancelled = false;
        webhookAdminService.getProviders(serverId)
            .then(loaded => { if (!cancelled) setProviders(loaded); })
            .catch(() => {
                if (cancelled) return;
                setProviders([]);
                dialog.alert('Failed to load the library update connections.');
            });
        webhookAdminService.getDeliveries(DELIVERY_LIMIT, serverId)
            .then(loaded => { if (!cancelled) setDeliveries(loaded); })
            .catch(() => {
                if (cancelled) return;
                setDeliveries([]);
                dialog.alert('Failed to load recent deliveries.');
            });
        return () => { cancelled = true; };
    }, [serverId, dialog]);

    const reloadProviders = useCallback(() => {
        webhookAdminService.getProviders(serverId)
            .then(setProviders)
            .catch(console.error);
    }, [serverId]);

    const refreshDeliveries = async () => {
        setRefreshing(true);
        try {
            setDeliveries(await webhookAdminService.getDeliveries(DELIVERY_LIMIT, serverId));
        } catch {
            await dialog.alert('Failed to refresh recent deliveries.');
        } finally {
            setRefreshing(false);
        }
    };

    return (
        <div data-vora-page="">
            <PageHeader
                title="Library Updates"
                description="Radarr, Sonarr and Lidarr tell Vora when a file is imported, upgraded, renamed or deleted, so the library updates at once. This lets a library's real-time watching be turned off."
            />

            <div className="mx-auto max-w-5xl space-y-6 px-8 pb-10 pt-6 max-md:px-4">
                {providers === null ? (
                    <div className="vora-skeleton h-48" />
                ) : providers.length === 0 ? (
                    <p className="text-sm text-[var(--vora-text-muted)]">No library update connections are available on this server.</p>
                ) : (
                    providers.map(provider => (
                        <WebhookProviderCard
                            key={provider.id}
                            provider={provider}
                            serverId={serverId}
                            showModal={showModal}
                            onChanged={reloadProviders}
                        />
                    ))
                )}

                <RecentDeliveries
                    deliveries={deliveries}
                    providers={providers ?? []}
                    refreshing={refreshing}
                    onRefresh={refreshDeliveries}
                />
            </div>
        </div>
    );
}
