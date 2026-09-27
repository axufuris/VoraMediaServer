import { type ReactNode, useEffect } from 'react';

export type ModalSize = 'sm' | 'md' | 'lg' | 'xl' | '2xl' | '3xl' | '4xl';
export type ModalZIndex = 'z-[200]' | 'z-[210]';
export type ModalPosition = 'fixed' | 'absolute';
/**
 * Surface variants:
 *   'gray-950' / 'gray-900' — dark surfaces. Default. Used by client-side modals.
 *   'light'                 — Vora Default light surface. Used by admin modals.
 */
export type ModalSurface = 'gray-950' | 'gray-900' | 'light';

interface ModalProps {
    isOpen: boolean;
    onClose: () => void;
    size?: ModalSize;
    zIndex?: ModalZIndex;
    position?: ModalPosition;
    surface?: ModalSurface;
    closeOnBackdropClick?: boolean;
    closeOnEscape?: boolean;
    overlayPadding?: string;
    cardClassName?: string;
    children: ReactNode;
}

const SIZE_CLASS: Record<ModalSize, string> = {
    'sm': 'max-w-sm',
    'md': 'max-w-md',
    'lg': 'max-w-lg',
    'xl': 'max-w-xl',
    '2xl': 'max-w-2xl',
    '3xl': 'max-w-3xl',
    '4xl': 'max-w-4xl'
};

const SURFACE_CLASS: Record<ModalSurface, string> = {
    'gray-950': 'bg-[var(--vora-bg-raised)] border-[var(--vora-border-subtle)] text-[var(--vora-text-primary)]',
    'gray-900': 'bg-[var(--vora-bg-sunken)] border-[var(--vora-border-strong)] text-[var(--vora-text-primary)]',
    'light': 'bg-[var(--vora-bg-surface)] border-[var(--vora-border-subtle)] text-[var(--vora-text-primary)]',
};

export function Modal({
    isOpen,
    onClose,
    size = 'md',
    zIndex = 'z-[200]',
    position = 'fixed',
    surface = 'gray-950',
    closeOnBackdropClick = false,
    closeOnEscape = true,
    overlayPadding = 'p-4',
    cardClassName = 'flex flex-col max-h-[90vh]',
    children
}: ModalProps) {
    useEffect(() => {
        if (!isOpen || !closeOnEscape) return;
        const onKeyDown = (e: KeyboardEvent) => {
            if (e.key === 'Escape') onClose();
        };
        window.addEventListener('keydown', onKeyDown);
        return () => window.removeEventListener('keydown', onKeyDown);
    }, [isOpen, closeOnEscape, onClose]);

    if (!isOpen) return null;

    const overlayBg = 'bg-[var(--vora-bg-overlay)]';
    const shadowClass = 'shadow-[var(--vora-shadow-overlay)]';
    const overlayClass = `${position} inset-0 ${zIndex} flex items-center justify-center ${overlayBg} backdrop-blur-sm ${overlayPadding}`;
    const cardClass = `${SURFACE_CLASS[surface]} rounded-xl border ${shadowClass} w-full ${SIZE_CLASS[size]} ${cardClassName}`;

    return (
        <div
            className={overlayClass}
            onClick={closeOnBackdropClick ? onClose : undefined}
        >
            <div
                className={cardClass}
                onClick={(e) => e.stopPropagation()}
                role="dialog"
                aria-modal="true"
            >
                {children}
            </div>
        </div>
    );
}

interface ModalHeaderProps {
    title: ReactNode;
    subtitle?: ReactNode;
    onClose: () => void;
    closeDisabled?: boolean;
    accent?: 'border' | 'background';
    bordered?: boolean;
    tabs?: ReactNode;
    surface?: ModalSurface;
}

export function ModalHeader({
    title,
    subtitle,
    onClose,
    closeDisabled = false,
    accent = 'border',
    bordered = true,
    tabs,
    surface = 'gray-950',
}: ModalHeaderProps) {
    const isLight = surface === 'light';
    const borderClass = bordered ? 'border-b border-[var(--vora-border-subtle)]' : '';
    const wrapperClass = accent === 'background'
        ? `px-6 pt-6 ${borderClass} bg-[var(--vora-bg-sunken)]`
        : `p-5 ${borderClass}`;
    const titleClass = isLight
        ? 'text-base font-semibold text-[var(--vora-text-primary)]'
        : 'text-xl font-bold text-[var(--vora-text-primary)]';
    const subtitleClass = 'text-xs text-[var(--vora-text-muted)] mt-1';
    const closeClass = 'text-[var(--vora-text-muted)] hover:text-[var(--vora-text-primary)] text-2xl leading-none cursor-pointer disabled:opacity-50';
    const tabsClass = 'text-[var(--vora-text-muted)]';

    return (
        <div className={wrapperClass}>
            <div className="flex justify-between items-center">
                <div>
                    <h2 className={titleClass}>{title}</h2>
                    {subtitle && <p className={subtitleClass}>{subtitle}</p>}
                </div>
                <button
                    onClick={onClose}
                    disabled={closeDisabled}
                    className={closeClass}
                    aria-label="Close"
                >
                    &times;
                </button>
            </div>
            {tabs && <div className={`flex gap-6 text-sm font-bold mt-4 ${tabsClass}`}>{tabs}</div>}
        </div>
    );
}

interface ModalBodyProps {
    children: ReactNode;
    className?: string;
    scrollable?: boolean;
}

export function ModalBody({
    children,
    className = '',
    scrollable = true,
}: ModalBodyProps) {
    const base = scrollable
        ? 'p-5 overflow-y-auto flex-1'
        : 'p-5';
    return <div className={`${base} ${className}`}>{children}</div>;
}

interface ModalFooterProps {
    children: ReactNode;
    className?: string;
    surface?: ModalSurface;
}

export function ModalFooter({ children, className = '' }: ModalFooterProps) {
    const baseClass = 'p-5 border-t border-[var(--vora-border-subtle)] bg-[var(--vora-bg-sunken)] rounded-b-xl';
    return <div className={`${baseClass} ${className}`}>{children}</div>;
}
