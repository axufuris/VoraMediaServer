import axios, { type AxiosInstance, type InternalAxiosRequestConfig, type AxiosRequestConfig, isAxiosError } from 'axios';
import { serverVault, type VoraServer } from '../utils/serverVault';
import { StorageKeys, SessionKeys } from '../utils/storageKeys';
import { defaultApiBaseUrl } from '../utils/apiBase';
import { getDesktopBridge } from '../desktop/desktopPlayback';

export interface VoraRequestConfig extends AxiosRequestConfig {
    serverId?: string;
}

export function getResponseStatus(err: unknown): number | undefined {
    return isAxiosError(err) ? err.response?.status : undefined;
}

let unauthorizedRedirectInFlight = false;
function handleUnauthorized(): void {
    if (unauthorizedRedirectInFlight) return;
    unauthorizedRedirectInFlight = true;
    try {
        localStorage.removeItem(StorageKeys.profileToken);
        localStorage.removeItem(StorageKeys.accountToken);
        localStorage.removeItem(StorageKeys.isServerAdmin);
        localStorage.removeItem(StorageKeys.isProfileAdmin);
        clientCache.clear();
    } catch {
        // ignore storage clear failures (e.g. private mode quota)
    }
    if (typeof window !== 'undefined' && window.location.pathname !== '/login') {
        window.location.assign('/login');
    }
}

const DETECTED_OS: string = (() => {
    const ua = (typeof navigator !== 'undefined' && navigator.userAgent) || '';
    if (/Windows NT 10\.0/.test(ua)) return 'Windows 10/11';
    if (/Windows NT 6\.3/.test(ua)) return 'Windows 8.1';
    if (/Windows NT 6\.2/.test(ua)) return 'Windows 8';
    if (/Windows NT 6\.1/.test(ua)) return 'Windows 7';
    if (/Windows/.test(ua)) return 'Windows';
    if (/Mac OS X|Macintosh/.test(ua)) return 'macOS';
    if (/Android/.test(ua)) return 'Android';
    if (/iPhone|iPad|iPod/.test(ua)) return 'iOS';
    if (/CrOS/.test(ua)) return 'ChromeOS';
    if (/Linux/.test(ua)) return 'Linux';
    return 'Unknown OS';
})();

// The persistent per-browser device id (and its companion X-Vora-* headers) must
// ride along on every request — including the login/setup/register calls made
// through createDirectClient — so the server can stamp DeviceId on the refresh
// token it issues and admins can later revoke a specific device.
interface ClientIdentity {
    client: string;
    device: string;
    deviceType: string;
    os: string;
}

function clientIdentity(): ClientIdentity {
    const desktop = getDesktopBridge();
    if (!desktop) return { client: 'Vora Web', device: 'Web Browser', deviceType: 'Browser', os: DETECTED_OS };

    const os = desktop.platform === 'windows' ? 'Windows' : 'Linux';
    const client = desktop.channel === 'qa' ? 'Vora Desktop QA' : 'Vora Desktop';
    return {
        client,
        device: `${client} (${os})`,
        deviceType: 'Desktop',
        os,
    };
}

function deviceHeaders(): Record<string, string> {
    const deviceId = localStorage.getItem(StorageKeys.deviceId);
    if (!deviceId) return {};
    const identity = clientIdentity();
    return {
        'X-Vora-Device-Id': deviceId,
        'X-Vora-Client': identity.client,
        'X-Vora-Device': identity.device,
        'X-Vora-Device-Type': identity.deviceType,
        'X-Vora-OS': identity.os,
    };
}

const FALLBACK_BASE_URL: string = defaultApiBaseUrl();

const PENDING_KEY = '__pending__';

interface ClientResolution {
    cacheKey: string;
    baseUrl: string;
    server: VoraServer | undefined;
}

function resolveClient(serverId?: string): ClientResolution {
    const pendingUrl = sessionStorage.getItem(SessionKeys.pendingServerUrl);

    let server: VoraServer | undefined;
    if (serverId) {
        server = serverVault.getServer(serverId);
    } else if (!pendingUrl) {
        server = serverVault.getActiveServer();
    }

    let baseUrl = FALLBACK_BASE_URL;
    if (server) {
        baseUrl = `${server.url}/api`;
    } else if (pendingUrl) {
        baseUrl = `${pendingUrl}/api`;
    }

    let cacheKey: string;
    if (server) {
        cacheKey = `server:${server.id}`;
    } else if (pendingUrl) {
        cacheKey = `${PENDING_KEY}:${pendingUrl}`;
    } else {
        cacheKey = `fallback:${baseUrl}`;
    }

    return { cacheKey, baseUrl, server };
}

const clientCache = new Map<string, AxiosInstance>();

function buildAxiosInstance(baseUrl: string, server: VoraServer | undefined): AxiosInstance {
    const instance = axios.create({
        baseURL: baseUrl,
        headers: {
            'Content-Type': 'application/json',
        },
    });

    instance.interceptors.request.use((config: InternalAxiosRequestConfig) => {
        const pendingToken = sessionStorage.getItem(SessionKeys.pendingUserToken);
        let token = server?.token || pendingToken || localStorage.getItem(StorageKeys.profileToken) || localStorage.getItem(StorageKeys.accountToken);
        if (token === 'undefined' || token === 'null') {
            token = null;
        }
        if (config.headers) {
            if (token) {
                config.headers['Authorization'] = `Bearer ${token}`;
            } else {
                delete config.headers['Authorization'];
            }

            for (const [key, value] of Object.entries(deviceHeaders())) {
                config.headers[key] = value;
            }
        }
        return config;
    });

    instance.interceptors.response.use(
        (response) => response,
        (error) => {
            const status = getResponseStatus(error);
            if (status === 401) {
                console.error(`Authentication failed for server: ${server?.name || 'Pending'}`);
                handleUnauthorized();
            }
            return Promise.reject(error);
        }
    );

    return instance;
}

export const createServerClient = (serverId?: string): AxiosInstance => {
    const { cacheKey, baseUrl, server } = resolveClient(serverId);
    const cached = clientCache.get(cacheKey);
    if (cached) return cached;
    const instance = buildAxiosInstance(baseUrl, server);
    clientCache.set(cacheKey, instance);
    return instance;
};

export const clearApiClientCache = (): void => {
    clientCache.clear();
};

export const createDirectClient = (baseUrl: string, token?: string): AxiosInstance => {
    const cleanUrl = baseUrl.endsWith('/') ? baseUrl.slice(0, -1) : baseUrl;
    const apiBase = cleanUrl.endsWith('/api') ? cleanUrl : `${cleanUrl}/api`;

    return axios.create({
        baseURL: apiBase,
        headers: {
            'Content-Type': 'application/json',
            ...deviceHeaders(),
            ...(token ? { 'Authorization': `Bearer ${token}` } : {})
        }
    });
};

export const apiClient = {
    get: <T>(url: string, config?: VoraRequestConfig) => createServerClient(config?.serverId).get<T>(url, config),
    post: <T, D = unknown>(url: string, data?: D, config?: VoraRequestConfig) => createServerClient(config?.serverId).post<T>(url, data, config),
    put: <T, D = unknown>(url: string, data?: D, config?: VoraRequestConfig) => createServerClient(config?.serverId).put<T>(url, data, config),
    delete: <T>(url: string, config?: VoraRequestConfig) => createServerClient(config?.serverId).delete<T>(url, config),
};
