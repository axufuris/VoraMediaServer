// navigator.clipboard only exists on a secure page (https, or localhost). On a
// server opened over plain http the copy buttons did nothing, so fall back to
// copying a selection, which works everywhere.
export async function copyText(text: string): Promise<boolean> {
    try {
        if (navigator.clipboard) {
            await navigator.clipboard.writeText(text);
            return true;
        }
    } catch {
        return copySelection(text);
    }
    return copySelection(text);
}

function copySelection(text: string): boolean {
    const field = document.createElement('textarea');
    field.value = text;
    field.setAttribute('readonly', '');
    field.style.position = 'fixed';
    field.style.opacity = '0';
    document.body.appendChild(field);
    field.select();
    try {
        return document.execCommand('copy');
    } catch {
        return false;
    } finally {
        field.remove();
    }
}
