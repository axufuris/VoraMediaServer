import { BrowserRouter, Routes, Route, Navigate, useParams, Outlet, useLocation } from 'react-router-dom';
import { useEffect, Suspense, type ReactElement, type ReactNode } from 'react';
import { serverVault } from './utils/serverVault';
import { lazyPage } from './utils/newBuild';
import PageErrorBoundary from './components/Common/PageErrorBoundary';
import { StorageKeys, getProfileIdFromToken } from './utils/storageKeys';
import { getOrCreateDeviceId } from './utils/deviceId';
import { defaultApiBaseUrl } from './utils/apiBase';
import MainLayout from './layouts/MainLayout';
import AdminShell from './components/Admin/Shell/AdminShell';
import { PlayerProvider } from './contexts/PlayerContext';
import { DialogProvider } from './dialogs';
import { ThemeProvider } from './theme/ThemeProvider';
import { ClientTemplateProvider } from './theme/ClientTemplateProvider';

import SetupPage from './pages/Auth/SetupPage';
import LoginPage from './pages/Auth/LoginPage';
import RegisterPage from './pages/Auth/RegisterPage';
import ForgotPasswordPage from './pages/Auth/ForgotPasswordPage';
import ResetPasswordPage from './pages/Auth/ResetPasswordPage';
import ConfirmEmailPage from './pages/Auth/ConfirmEmailPage';
import ProfileSelectionPage from './pages/Profile/ProfileSelectionPage';

const ClientLibraryPage = lazyPage(() => import('./pages/Client/LibraryPage'));
const LibraryDashboard = lazyPage(() => import('./pages/Client/LibraryDashboard'));
const ManageLibrary = lazyPage(() => import('./pages/Admin/Libraries/ManageLibrary'));
const ClientMediaDetailsPage = lazyPage(() => import('./pages/Client/Media/MediaDetailsPage'));
const ClientCollectionDetailsPage = lazyPage(() => import('./pages/Client/Collections/CollectionDetailsPage'));
const TaskDashboard = lazyPage(() => import('./pages/Admin/Tasks/TaskDashboard'));
const ClientActorDetailsPage = lazyPage(() => import('./pages/Client/Media/ActorDetailsPage'));
const ClientHomePage = lazyPage(() => import('./pages/Client/HomePage'));
const AdminSmartListsPage = lazyPage(() => import('./pages/Admin/SmartLists/SmartListsPage'));
const AdminPluginsPage = lazyPage(() => import('./pages/Admin/PluginsPage'));
const AdminSettingsPage = lazyPage(() => import('./pages/Admin/SettingsPage'));
const CreateLibrary = lazyPage(() => import('./pages/Admin/Libraries/CreateLibrary'));
const AccountSettingsPage = lazyPage(() => import('./pages/Profile/AccountSettingsPage'));
const AdminUserManagementPage = lazyPage(() => import('./pages/Admin/UserManagementPage'));
const AdminInvitationsPage = lazyPage(() => import('./pages/Admin/InvitationsPage'));
const AuthorizedDevicesPage = lazyPage(() => import('./pages/Admin/AuthorizedDevicesPage'));
const AdminDashboardPage = lazyPage(() => import('./pages/Admin/DashboardPage'));
const AdminAppearancePage = lazyPage(() => import('./pages/Admin/AppearancePage'));
const AdminTemplateSchedulesPage = lazyPage(() => import('./pages/Admin/Templates/SchedulesPage'));
const AdminHistoryPage = lazyPage(() => import('./pages/Admin/HistoryPage'));
const ProfileHistoryPage = lazyPage(() => import('./pages/Client/ProfileHistoryPage'));
const ClientPlaylistsPage = lazyPage(() => import('./pages/Client/Playlists/PlaylistsPage'));
const ClientPlaylistDetailsPage = lazyPage(() => import('./pages/Client/Playlists/PlaylistDetailsPage'));
const SmartPlaylistDetailsPage = lazyPage(() => import('./pages/Client/Playlists/SmartPlaylistDetailsPage'));
const SearchPage = lazyPage(() => import('./pages/Client/SearchPage'));
const AdminDiscoveryPage = lazyPage(() => import('./pages/Admin/Discovery/DiscoveryPage'));
const ClientDiscoveryDetailsPage = lazyPage(() => import('./pages/Client/Discovery/DiscoveryDetailsPage'));
const ClientDiscoveryActorPage = lazyPage(() => import('./pages/Client/Discovery/DiscoveryActorPage'));
const ClientDiscoveryViewAllPage = lazyPage(() => import('./pages/Client/Discovery/DiscoveryViewAllPage'));
const AdminRequestsPage = lazyPage(() => import('./pages/Admin/RequestsPage'));
const AdminAiStatsPage = lazyPage(() => import('./pages/Admin/AiStatsPage'));
const AdminAiSettingsPage = lazyPage(() => import('./pages/Admin/AiSettingsPage'));
const AdminDedupePage = lazyPage(() => import('./pages/Admin/DedupePage'));
const AdminMediaTrashPage = lazyPage(() => import('./pages/Admin/MediaTrashPage'));
const OverlayEditor = lazyPage(() => import('./pages/Admin/Overlay/OverlayEditor'));
const AdminIptvPage = lazyPage(() => import('./pages/Admin/Iptv/IptvPage'));
const AdminForYouPage = lazyPage(() => import('./pages/Admin/Features/ForYouPage'));
const AdminReleaseCalendarPage = lazyPage(() => import('./pages/Admin/Features/ReleaseCalendarPage'));
const AdminDvrPage = lazyPage(() => import('./pages/Admin/Features/DvrPage'));
const AdminLiveTvPage = () => <AdminIptvPage kind="Tv" />;
const AdminInternetRadioPage = () => <AdminIptvPage kind="Radio" />;
const AdminPodcastsPage = lazyPage(() => import('./pages/Admin/Podcasts/PodcastsAdminPage'));
const AdminMusicHistoryPage = lazyPage(() => import('./pages/Admin/MusicHistoryPage'));
const AdminLogsPage = lazyPage(() => import('./pages/Admin/LogsPage'));
const AdminBackupsPage = lazyPage(() => import('./pages/Admin/BackupsPage'));
const AdminLibraryMigrationPage = lazyPage(() => import('./pages/Admin/LibraryMigrationPage'));
const AdminSetupGuidePage = lazyPage(() => import('./pages/Admin/SetupGuide/SetupGuidePage'));
const ClientSettingsPage = lazyPage(() => import('./pages/Client/SettingsPage'));
const MusicPage = lazyPage(() => import('./pages/Client/Audio/MusicPage'));
const PodcastsPage = lazyPage(() => import('./pages/Client/Audio/PodcastsPage'));
const RadioPage = lazyPage(() => import('./pages/Client/Audio/RadioPage'));
const ClientDiscoveryPage = lazyPage(() => import('./pages/Client/Discovery/DiscoveryPage'));
const ClientCalendarPage = lazyPage(() => import('./pages/Client/CalendarPage'));
const LiveTvHubPage = lazyPage(() => import('./pages/Client/LiveTv/LiveTvHubPage'));

getOrCreateDeviceId();

if (serverVault.getServers().length === 0 && localStorage.getItem(StorageKeys.profileToken)) {
    try {
        const token = localStorage.getItem(StorageKeys.profileToken);
        const profileId = token ? getProfileIdFromToken(token) : null;
        if (!token || !profileId) throw new Error('Missing profile token for legacy migration');
        const isAdmin = localStorage.getItem(StorageKeys.isServerAdmin) === 'true';

        const apiUrl = defaultApiBaseUrl();
        const baseUrl = apiUrl.endsWith('/api') ? apiUrl.slice(0, -4) : apiUrl;

        serverVault.addOrUpdateServer({
            id: 'legacy_server_migration',
            name: 'Vora Server',
            url: baseUrl,
            token: token,
            profileId: profileId,
            isAdmin: isAdmin
        });
        serverVault.setActiveServerId('legacy_server_migration');
    } catch (e) {
        console.error("Failed to migrate legacy auth data", e);
    }
}

const ServerContextWrapper = () => {
    const { serverId } = useParams<{ serverId: string }>();

    useEffect(() => {
        if (serverId && serverVault.getActiveServerId() !== serverId) {
            serverVault.setActiveServerId(serverId);
        }
    }, [serverId]);

    return <Outlet />;
};

const RequireAuth = ({ children }: { children: ReactElement }) => {
    const servers = serverVault.getServers();
    const activeServer = serverVault.getActiveServer();

    useEffect(() => {
        if (!activeServer && servers.length > 0) {
            serverVault.setActiveServerId(servers[0].id);
        }
    }, [activeServer, servers]);

    if (servers.length === 0) {
        return <Navigate to="/login" replace />;
    }

    const profileToken = localStorage.getItem(StorageKeys.profileToken);
    if (!profileToken) {
        return <Navigate to="/profiles" replace />;
    }

    return children;
};

const RouteErrorBoundary = ({ children }: { children: ReactNode }) => {
    const location = useLocation();
    return <PageErrorBoundary resetKey={location.pathname}>{children}</PageErrorBoundary>;
};

const RequireAdmin = ({ children }: { children: ReactElement }) => {
    const isServerAdmin = localStorage.getItem(StorageKeys.isServerAdmin) === 'true';
    if (!isServerAdmin) {
        return <Navigate to="/" replace />;
    }
    return children;
};

export default function App() {
    return (
        <DialogProvider>
            <PlayerProvider>
                <BrowserRouter>
                    <ThemeProvider>
                        <ClientTemplateProvider>
                        <RouteErrorBoundary>
                        <Suspense fallback={<div className="fixed inset-0 flex items-center justify-center" style={{ background: 'var(--vora-bg-canvas)' }}><div className="vora-skeleton h-12 w-48" /></div>}>
                        <Routes>
                    <Route path="/setup" element={<SetupPage />} />
                    <Route path="/login" element={<LoginPage />} />
                    <Route path="/register" element={<RegisterPage />} />
                    <Route path="/forgot-password" element={<ForgotPasswordPage />} />
                    <Route path="/reset-password" element={<ResetPasswordPage />} />
                    <Route path="/confirm-email" element={<ConfirmEmailPage />} />
                    <Route path="/profiles" element={<ProfileSelectionPage />} />

                    {/* CLIENT ROUTES */}
                    <Route path="/" element={<RequireAuth><MainLayout /></RequireAuth>}>
                        <Route index element={<ClientHomePage />} />
                        <Route path="account" element={<AccountSettingsPage />} />
                        <Route path="history" element={<ProfileHistoryPage />} />
                        <Route path="search" element={<SearchPage />} />

                        <Route path="library/:id" element={<ClientLibraryPage />} />
                        <Route path="playlists" element={<ClientPlaylistsPage />} />
                        <Route path="playlist/:id" element={<ClientPlaylistDetailsPage />} />
                        <Route path="smart-playlist/:id" element={<SmartPlaylistDetailsPage />} />
                        <Route path="media/:id" element={<ClientMediaDetailsPage />} />
                        <Route path="collection/:id" element={<ClientCollectionDetailsPage />} />
                        <Route path="/actor/:id" element={<ClientActorDetailsPage />} />
                        <Route path="discovery" element={<ClientDiscoveryPage />} />
                        <Route path="calendar" element={<ClientCalendarPage />} />
                        <Route path="livetv" element={<LiveTvHubPage />} />
                        <Route path="music" element={<MusicPage />} />
                        <Route path="podcasts" element={<PodcastsPage />} />
                        <Route path="radio" element={<RadioPage />} />
                        <Route path="settings" element={<ClientSettingsPage />} />
                        <Route path="discovery/:providerId/row/:rowId" element={<ClientDiscoveryViewAllPage />} />
                        <Route path="discovery/:providerId/:type/:externalId" element={<ClientDiscoveryDetailsPage />} />
                        <Route path="discovery/:providerId/actor/:externalId" element={<ClientDiscoveryActorPage />} />

                        <Route path="server/:serverId" element={<ServerContextWrapper />}>
                            <Route index element={<ClientHomePage />} />
                            <Route path="account" element={<AccountSettingsPage />} />
                            <Route path="history" element={<ProfileHistoryPage />} />
                            <Route path="search" element={<SearchPage />} />
                            <Route path="library/:id" element={<ClientLibraryPage />} />
                            <Route path="media/:id" element={<ClientMediaDetailsPage />} />
                            <Route path="collection/:id" element={<ClientCollectionDetailsPage />} />
                            <Route path="actor/:id" element={<ClientActorDetailsPage />} />
                            <Route path="playlists" element={<ClientPlaylistsPage />} />
                            <Route path="playlist/:id" element={<ClientPlaylistDetailsPage />} />
                            <Route path="smart-playlist/:id" element={<SmartPlaylistDetailsPage />} />
                            <Route path="discovery" element={<ClientDiscoveryPage />} />
                            <Route path="calendar" element={<ClientCalendarPage />} />
                            <Route path="livetv" element={<LiveTvHubPage />} />
                            <Route path="music" element={<MusicPage />} />
                            <Route path="podcasts" element={<PodcastsPage />} />
                            <Route path="radio" element={<RadioPage />} />
                            <Route path="settings" element={<ClientSettingsPage />} />
                            <Route path="discovery/:providerId/row/:rowId" element={<ClientDiscoveryViewAllPage />} />
                            <Route path="discovery/:providerId/:type/:externalId" element={<ClientDiscoveryDetailsPage />} />
                            <Route path="discovery/:providerId/actor/:externalId" element={<ClientDiscoveryActorPage />} />
                        </Route>
                    </Route>

                    {/* ADMIN ROUTES */}
                    <Route path="/admin" element={<RequireAuth><RequireAdmin><AdminShell /></RequireAdmin></RequireAuth>}>
                        <Route index element={<AdminDashboardPage />} />
                        <Route path="history" element={<AdminHistoryPage />} />
                        <Route path="libraries" element={<LibraryDashboard />} />
                        <Route path="dedupe" element={<AdminDedupePage />} />
                        <Route path="media-trash" element={<AdminMediaTrashPage />} />
                        <Route path="devices" element={<AuthorizedDevicesPage />} />
                        <Route path="users" element={<AdminUserManagementPage />} />
                        <Route path="invitations" element={<AdminInvitationsPage />} />
                        <Route path="libraries/:id/manage" element={<ManageLibrary />} />
                        <Route path="libraries/new" element={<CreateLibrary />} />
                        <Route path="tasks" element={<TaskDashboard />} />
                        <Route path="smart-lists" element={<AdminSmartListsPage />} />
                        <Route path="plugins" element={<AdminPluginsPage />} />
                        <Route path="settings" element={<AdminSettingsPage />} />
                        <Route path="discovery" element={<AdminDiscoveryPage />} />
                        <Route path="for-you" element={<AdminForYouPage />} />
                        <Route path="release-calendar" element={<AdminReleaseCalendarPage />} />
                        <Route path="dvr-settings" element={<AdminDvrPage />} />
                        <Route path="requests" element={<AdminRequestsPage />} />
                        <Route path="ai-stats" element={<AdminAiStatsPage />} />
                        <Route path="ai-settings" element={<AdminAiSettingsPage />} />
                        <Route path="overlays" element={<OverlayEditor />} />
                        <Route path="live-tv" element={<AdminLiveTvPage />} />
                        <Route path="internet-radio" element={<AdminInternetRadioPage />} />
                        <Route path="podcasts" element={<AdminPodcastsPage />} />
                        <Route path="music-history" element={<AdminMusicHistoryPage />} />
                        <Route path="appearance" element={<AdminAppearancePage />} />
                        <Route path="client-templates" element={<AdminTemplateSchedulesPage />} />
                        <Route path="logs" element={<AdminLogsPage />} />
                        <Route path="backups" element={<AdminBackupsPage />} />
                        <Route path="library-migration" element={<AdminLibraryMigrationPage />} />
                        <Route path="setup" element={<AdminSetupGuidePage />} />

                        <Route path="server/:serverId" element={<ServerContextWrapper />}>
                            <Route index element={<AdminDashboardPage />} />
                            <Route path="history" element={<AdminHistoryPage />} />
                            <Route path="libraries" element={<LibraryDashboard />} />
                            <Route path="dedupe" element={<AdminDedupePage />} />
                            <Route path="media-trash" element={<AdminMediaTrashPage />} />
                            <Route path="devices" element={<AuthorizedDevicesPage />} />
                            <Route path="users" element={<AdminUserManagementPage />} />
                            <Route path="invitations" element={<AdminInvitationsPage />} />
                            <Route path="libraries/:id/manage" element={<ManageLibrary />} />
                            <Route path="libraries/new" element={<CreateLibrary />} />
                            <Route path="tasks" element={<TaskDashboard />} />
                            <Route path="smart-lists" element={<AdminSmartListsPage />} />
                            <Route path="plugins" element={<AdminPluginsPage />} />
                            <Route path="settings" element={<AdminSettingsPage />} />
                            <Route path="discovery" element={<AdminDiscoveryPage />} />
                        <Route path="for-you" element={<AdminForYouPage />} />
                        <Route path="release-calendar" element={<AdminReleaseCalendarPage />} />
                        <Route path="dvr-settings" element={<AdminDvrPage />} />
                            <Route path="requests" element={<AdminRequestsPage />} />
                            <Route path="ai-stats" element={<AdminAiStatsPage />} />
                            <Route path="ai-settings" element={<AdminAiSettingsPage />} />
                            <Route path="overlays" element={<OverlayEditor />} />
                            <Route path="live-tv" element={<AdminLiveTvPage />} />
                        <Route path="internet-radio" element={<AdminInternetRadioPage />} />
                        <Route path="podcasts" element={<AdminPodcastsPage />} />
                        <Route path="music-history" element={<AdminMusicHistoryPage />} />
                            <Route path="appearance" element={<AdminAppearancePage />} />
                            <Route path="client-templates" element={<AdminTemplateSchedulesPage />} />
                            <Route path="logs" element={<AdminLogsPage />} />
                            <Route path="backups" element={<AdminBackupsPage />} />
                            <Route path="library-migration" element={<AdminLibraryMigrationPage />} />
                            <Route path="setup" element={<AdminSetupGuidePage />} />
                        </Route>
                    </Route>

                        <Route path="*" element={<Navigate to="/" replace />} />
                        </Routes>
                        </Suspense>
                        </RouteErrorBoundary>
                        </ClientTemplateProvider>
                    </ThemeProvider>
                </BrowserRouter>
            </PlayerProvider>
        </DialogProvider>
    );
}
