import { useCallback, useState, type ReactNode } from 'react';

interface NoticeState {
    isOpen: boolean;
    title: string;
    message: string;
    isError: boolean;
}

export type ShowNotice = (title: string, message: string, isError?: boolean) => void;

/**
 * The centred OK / error notice the settings tabs report a save or a failed test with.
 * Render `noticeModal` once at the end of the page and hand `showModal` to the tabs.
 */
export function useNoticeModal(): { noticeModal: ReactNode; showModal: ShowNotice } {
    const [notice, setNotice] = useState<NoticeState>({ isOpen: false, title: '', message: '', isError: false });

    const showModal = useCallback<ShowNotice>((title, message, isError = false) => {
        setNotice({ isOpen: true, title, message, isError });
    }, []);
    const closeModal = () => setNotice(prev => ({ ...prev, isOpen: false }));

    const noticeModal = notice.isOpen && (
        <div className="fixed inset-0 z-[200] flex items-center justify-center bg-[var(--vora-bg-overlay)] backdrop-blur-sm p-4" onClick={closeModal}>
            <div className="vora-card shadow-[var(--vora-shadow-overlay)] p-6 max-w-sm w-full text-center" onClick={e => e.stopPropagation()}>
                <div className={`w-14 h-14 rounded-full flex items-center justify-center mx-auto mb-4 ${notice.isError ? 'bg-[var(--vora-danger-soft)] text-[var(--vora-danger-text)]' : 'bg-[var(--vora-success-soft)] text-[var(--vora-success-text)]'}`}>
                    {notice.isError ? (
                        <svg className="w-7 h-7" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" /></svg>
                    ) : (
                        <svg className="w-7 h-7" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M5 13l4 4L19 7" /></svg>
                    )}
                </div>
                <h2 className="text-lg font-semibold text-[var(--vora-text-primary)] mb-2">{notice.title}</h2>
                <p className="text-sm text-[var(--vora-text-secondary)] mb-6 whitespace-pre-wrap">{notice.message}</p>
                <button type="button" onClick={closeModal} className="vora-button-primary w-full">OK</button>
            </div>
        </div>
    );

    return { noticeModal, showModal };
}
