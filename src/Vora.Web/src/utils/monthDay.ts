export const MONTHS = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];

// 2000 is a leap year, so February offers the 29th; the server checks the same way.
export const daysInMonth = (month: number): number => new Date(2000, month, 0).getDate();
