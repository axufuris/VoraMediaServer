export const liveStartRefusal = (err: unknown): string | undefined => {
    const response = (err as { response?: { status?: number; data?: { error?: unknown } } } | null)?.response;
    const reason = response?.data?.error;
    return response?.status === 503 && typeof reason === 'string' && reason.trim() ? reason : undefined;
};
