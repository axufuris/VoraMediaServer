// When the server is already encoding as many streams as its transcode limits allow, starting a
// channel that has to be encoded answers HTTP 503 with { error: "<why>" }. Returns that reason, or
// undefined for any other failure.
export const liveStartRefusal = (err: unknown): string | undefined => {
    const response = (err as { response?: { status?: number; data?: { error?: unknown } } } | null)?.response;
    const reason = response?.data?.error;
    return response?.status === 503 && typeof reason === 'string' && reason.trim() ? reason : undefined;
};
