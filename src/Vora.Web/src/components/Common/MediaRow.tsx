import { type ReactNode } from 'react';

type MediaRowVariant = 'home' | 'detail';
type MediaRowGap = '4' | '5' | '6';

interface MediaRowProps {
    title?: ReactNode;
    subtitle?: ReactNode;
    headerAction?: ReactNode;
    variant?: MediaRowVariant;
    gap?: MediaRowGap;
    titleClassName?: string;
    children: ReactNode;
}

const SCROLL_VARIANT: Record<MediaRowVariant, string> = {
    home: 'flex overflow-x-auto px-8 pb-4 snap-x hide-scrollbar',
    detail: 'flex overflow-x-auto pb-6 custom-scrollbar pr-12'
};

const HEADER_VARIANT: Record<MediaRowVariant, string> = {
    home: 'px-8 mb-4',
    detail: 'mb-6 border-b border-[var(--vora-border-subtle)] pb-2'
};

const GAP_CLASS: Record<MediaRowGap, string> = {
    '4': 'gap-4',
    '5': 'gap-5',
    '6': 'gap-6'
};

export default function MediaRow({
    title,
    subtitle,
    headerAction,
    variant = 'home',
    gap = '4',
    titleClassName,
    children
}: MediaRowProps) {
    const showHeader = title !== undefined || headerAction !== undefined;
    const headerWrapper = HEADER_VARIANT[variant];
    const titleStyle = titleClassName ?? (variant === 'home'
        ? 'text-xl font-bold text-[var(--vora-text-primary)] tracking-wide'
        : 'text-2xl font-bold text-[var(--vora-text-primary)]');
    const containerMargin = variant === 'home' ? 'mb-10' : 'mt-16';

    return (
        <div className={containerMargin}>
            {showHeader && (
                <div className={`${headerWrapper} flex items-center justify-between`}>
                    <div>
                        {title !== undefined && <h2 className={titleStyle}>{title}</h2>}
                        {subtitle !== undefined && <p className="text-sm text-[var(--vora-text-muted)] mt-1">{subtitle}</p>}
                    </div>
                    {headerAction}
                </div>
            )}
            <div className={`${SCROLL_VARIANT[variant]} ${GAP_CLASS[gap]}`}>
                {children}
            </div>
        </div>
    );
}
