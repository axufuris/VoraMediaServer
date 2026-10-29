import { MONTHS, daysInMonth } from '../../utils/monthDay';

interface MonthDayPickerProps {
    idPrefix: string;
    month: number;
    day: number;
    onChange: (month: number, day: number) => void;
    selectClassName?: string;
}

// A day of the year with no year: the month, then a day that month has.
export default function MonthDayPicker({ idPrefix, month, day, onChange, selectClassName = 'vora-input cursor-pointer' }: MonthDayPickerProps) {
    return (
        <div className="flex gap-2">
            <select id={`${idPrefix}-month`} aria-label="Month" value={month} onChange={e => { const m = Number(e.target.value); onChange(m, Math.min(day, daysInMonth(m))); }} className={selectClassName}>
                {MONTHS.map((name, i) => <option key={name} value={i + 1}>{name}</option>)}
            </select>
            <select id={`${idPrefix}-day`} aria-label="Day" value={day} onChange={e => onChange(month, Number(e.target.value))} className={`${selectClassName} w-24`}>
                {Array.from({ length: daysInMonth(month) }, (_, i) => i + 1).map(d => <option key={d} value={d}>{d}</option>)}
            </select>
        </div>
    );
}
