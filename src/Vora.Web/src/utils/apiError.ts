import axios from 'axios';

// The API's global exception handler already puts a usable sentence in
// ProblemDetails.detail for every 4xx — "Remote server returned 403.",
// "The URL returned 'text/html' and the content is not an image." Handlers that
// catch and then alert a fixed string throw that away, so a failure the server
// explained precisely reaches the viewer as "Failed to add URL" and the only way
// to find out why is to read the container log.
interface ProblemDetails {
    title?: string;
    detail?: string;
}

function isProblemDetails(value: unknown): value is ProblemDetails {
    if (typeof value !== 'object' || value === null) return false;
    const { title, detail } = value as Record<string, unknown>;
    return typeof title === 'string' || typeof detail === 'string';
}

function firstNonEmpty(...values: (string | undefined)[]): string | undefined {
    return values.find(value => typeof value === 'string' && value.trim().length > 0)?.trim();
}

// `fallback` still carries the context the server cannot know ("Failed to add
// URL"), so the result reads as "Failed to add URL: <reason>" when there is a
// reason and as the plain fallback when there is not.
export function errorDetail(error: unknown, fallback: string): string {
    const reason = resolveReason(error);
    return reason ? `${fallback}: ${reason}` : fallback;
}

export function resolveReason(error: unknown): string | undefined {
    if (axios.isAxiosError(error)) {
        const data: unknown = error.response?.data;

        if (isProblemDetails(data)) {
            // A bare title like "Invalid operation" is the status restated; the
            // detail is the part worth showing.
            const fromBody = firstNonEmpty(data.detail, data.title);
            if (fromBody) return fromBody;
        }

        if (typeof data === 'string') {
            const text = firstNonEmpty(data);
            // An HTML error page is not an explanation.
            if (text && !text.startsWith('<')) return text;
        }

        // No body at all — a timeout or a refused connection still has a cause
        // worth naming, otherwise this reads as though the server rejected it.
        if (!error.response) return firstNonEmpty(error.message);

        return undefined;
    }

    if (error instanceof Error) return firstNonEmpty(error.message);

    return undefined;
}
