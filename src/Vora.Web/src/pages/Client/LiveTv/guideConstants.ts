import { parseServerDate } from '../../../utils/serverTime';
export const ROW_HEIGHT = 80;
export const PX_PER_MINUTE = 6;
export const HOURS_TO_SHOW = 6;
export const CHANNEL_COLUMN_WIDTH = 256;

// A zone-less programme time is UTC. The guard this replaced tested
// `includes('-')`, which is true of every ISO date, so the Z it meant to add
// was never added and the tooltip disagreed with the bar it pointed at.
export const parseDate = (dateStr: string): Date => parseServerDate(dateStr) ?? new Date();
