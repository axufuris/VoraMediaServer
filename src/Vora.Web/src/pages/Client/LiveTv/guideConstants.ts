export const ROW_HEIGHT = 80;
export const PX_PER_MINUTE = 6;
export const HOURS_TO_SHOW = 6;
export const CHANNEL_COLUMN_WIDTH = 256;

export const parseDate = (dateStr: string): Date => {
    if (!dateStr) return new Date();
    if (!dateStr.endsWith('Z') && !dateStr.includes('+') && !dateStr.includes('-')) return new Date(dateStr + 'Z');
    return new Date(dateStr);
};
