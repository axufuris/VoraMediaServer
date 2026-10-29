import type { ReactNode } from 'react';
import MonthDayPicker from '../Common/MonthDayPicker';
import { withEveryYear, type SeasonalWindow } from './seasonalWindow';

interface SeasonalVisibilityFieldsProps {
    idPrefix: string;
    value: SeasonalWindow;
    onChange: (value: SeasonalWindow) => void;
    labelAddon?: (field: 'VisibleStartDate' | 'VisibleEndDate') => ReactNode;
}

const BOX = 'bg-[var(--vora-bg-canvas)] border border-[var(--vora-border-subtle)] rounded-md p-2 text-[var(--vora-text-primary)] outline-none';
const FIELD = `w-full ${BOX}`;
const PICKER = `${BOX} cursor-pointer`;
const LABEL = 'flex items-center text-sm font-medium text-[var(--vora-text-muted)] mb-1';

export default function SeasonalVisibilityFields({ idPrefix, value, onChange, labelAddon }: SeasonalVisibilityFieldsProps) {
    const update = (patch: Partial<SeasonalWindow>) => onChange({ ...value, ...patch });

    return (
        <div className="space-y-4">
            <div className="flex items-center gap-2">
                <input
                    type="checkbox"
                    id={`${idPrefix}-every-year`}
                    checked={value.everyYear}
                    onChange={e => onChange(withEveryYear(value, e.target.checked))}
                    className="w-4 h-4 accent-[var(--vora-accent-500)]"
                />
                <label htmlFor={`${idPrefix}-every-year`} className="text-sm text-[var(--vora-text-secondary)] font-medium cursor-pointer">
                    Repeat every year
                </label>
            </div>

            {value.everyYear ? (
                <div className="grid grid-cols-2 gap-4 max-md:grid-cols-1">
                    <div>
                        <label htmlFor={`${idPrefix}-start-month`} className={LABEL}>Shows from {labelAddon?.('VisibleStartDate')}</label>
                        <MonthDayPicker idPrefix={`${idPrefix}-start`} month={value.startMonth} day={value.startDay} onChange={(startMonth, startDay) => update({ startMonth, startDay })} selectClassName={PICKER} />
                    </div>
                    <div>
                        <label htmlFor={`${idPrefix}-end-month`} className={LABEL}>Through {labelAddon?.('VisibleEndDate')}</label>
                        <MonthDayPicker idPrefix={`${idPrefix}-end`} month={value.endMonth} day={value.endDay} onChange={(endMonth, endDay) => update({ endMonth, endDay })} selectClassName={PICKER} />
                    </div>
                </div>
            ) : (
                <div className="grid grid-cols-2 gap-4 max-md:grid-cols-1">
                    <div>
                        <label htmlFor={`${idPrefix}-start`} className={LABEL}>Visible Start Date {labelAddon?.('VisibleStartDate')}</label>
                        <input id={`${idPrefix}-start`} type="date" value={value.startDate} onChange={e => update({ startDate: e.target.value })} className={`${FIELD} color-scheme-dark`} />
                    </div>
                    <div>
                        <label htmlFor={`${idPrefix}-end`} className={LABEL}>Visible End Date {labelAddon?.('VisibleEndDate')}</label>
                        <input id={`${idPrefix}-end`} type="date" value={value.endDate} onChange={e => update({ endDate: e.target.value })} className={`${FIELD} color-scheme-dark`} />
                    </div>
                </div>
            )}

            <p className="text-xs text-[var(--vora-text-muted)] leading-relaxed">
                {value.everyYear
                    ? 'Shown from the first day through the last every year, and hidden the rest of the year. The window can run over New Year, e.g. December 20 through January 6.'
                    : 'Shown from the start date through the end date, and hidden before and after. Leave both blank to always show it.'}
                {' '}Its Home row follows the same window. Admins can still find it with Show hidden.
            </p>
        </div>
    );
}
