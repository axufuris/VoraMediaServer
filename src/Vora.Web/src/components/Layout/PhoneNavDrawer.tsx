import { useEffect, type MouseEvent, type ReactNode } from 'react';

interface PhoneNavDrawerProps {
    open: boolean;
    onClose: () => void;
    children: ReactNode;
}

export default function PhoneNavDrawer({ open, onClose, children }: PhoneNavDrawerProps) {
    useEffect(() => {
        if (!open) return;
        const closeOnEscape = (e: KeyboardEvent) => {
            if (e.key === 'Escape') onClose();
        };
        window.addEventListener('keydown', closeOnEscape);
        return () => window.removeEventListener('keydown', closeOnEscape);
    }, [open, onClose]);

    const closeOnLink = (e: MouseEvent<HTMLDivElement>) => {
        if (open && e.target instanceof Element && e.target.closest('a')) onClose();
    };

    return (
        <>
            {open && (
                <div
                    aria-hidden="true"
                    data-testid="phone-nav-scrim"
                    className="fixed inset-0 z-[140] bg-[var(--vora-bg-overlay)] md:hidden"
                    onClick={onClose}
                />
            )}
            <div
                data-testid="phone-nav-drawer"
                data-open={open}
                onClick={closeOnLink}
                className={`md:contents max-md:fixed max-md:inset-y-0 max-md:left-0 max-md:z-[150] max-md:flex max-md:max-w-[85vw] max-md:transition-[transform,visibility] max-md:duration-200 ${open ? '' : 'max-md:invisible max-md:-translate-x-full'}`}
            >
                {children}
            </div>
        </>
    );
}
