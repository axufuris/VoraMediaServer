export const LINK_CODE_LENGTH = 8;

export function normalizeLinkCode(raw: string): string {
    return raw.replace(/[^a-z0-9]/gi, '').toUpperCase().slice(0, LINK_CODE_LENGTH);
}

export function formatLinkCode(raw: string): string {
    const code = normalizeLinkCode(raw);
    return code.length > 4 ? `${code.slice(0, 4)}-${code.slice(4)}` : code;
}

export function isCompleteLinkCode(raw: string): boolean {
    return normalizeLinkCode(raw).length === LINK_CODE_LENGTH;
}
