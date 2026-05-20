import axios, { type AxiosInstance, type InternalAxiosRequestConfig, type AxiosRequestConfig } from 'axios';
import { serverVault, type VoraServer } from '../utils/serverVault';

export interface VoraRequestConfig extends AxiosRequestConfig {
    serverId?: string;
}

function detectOs(): string {
    const ua = navigator.userAgent || '';
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
}

export const createServerClient = (serverId?: string): AxiosInstance => {
    const pendingUrl = sessionStorage.getItem('pending_server_url');
    const pendingToken = sessionStorage.getItem('pending_user_token');

    let server: VoraServer | undefined;

    if (serverId) {
        server = serverVault.getServer(serverId);
    } else if (!pendingUrl) {
        server = serverVault.getActiveServer();
    }

    let baseUrl = import.meta.env.VITE_API_BASE_URL || 'http://localhost:5000/api';
    if (server) {
        baseUrl = `${server.url}/api`;
    } else if (pendingUrl) {
        baseUrl = `${pendingUrl}/api`;
    }

    let token = server?.token || pendingToken || localStorage.getItem('profile_token') || localStorage.getItem('account_token');

    if (token === 'undefined' || token === 'null') {
        token = null;
    }

    const instance = axios.create({
        baseURL: baseUrl,
        headers: {
            'Content-Type': 'application/json',
            ...(token ? { 'Authorization': `Bearer ${token}` } : {})
        }
    });

    instance.interceptors.request.use((config: InternalAxiosRequestConfig) => {
        const deviceId = localStorage.getItem('device_id');
        if (deviceId && config.headers) {
            config.headers['X-Vora-Device-Id'] = deviceId;
            config.headers['X-Vora-Client'] = 'Vora Web';
            config.headers['X-Vora-Device'] = 'Web Browser';
            config.headers['X-Vora-Device-Type'] = 'Browser';
            config.headers['X-Vora-OS'] = detectOs();
        }
        return config;
    });

    instance.interceptors.response.use(
        (response) => response,
        (error) => {
            if (error.response?.status === 401) {
                console.error(`Authentication failed for server: ${server?.name || 'Pending'}`);
            }
            return Promise.reject(error);
        }
    );

    return instance;
};

export const createDirectClient = (baseUrl: string, token?: string): AxiosInstance => {
    const cleanUrl = baseUrl.endsWith('/') ? baseUrl.slice(0, -1) : baseUrl;
    const apiBase = cleanUrl.endsWith('/api') ? cleanUrl : `${cleanUrl}/api`;

    return axios.create({
        baseURL: apiBase,
        headers: {
            'Content-Type': 'application/json',
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
