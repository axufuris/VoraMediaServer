import { useLocation, useNavigate } from 'react-router-dom';
import { useIsInstalledApp } from '../../hooks/useIsInstalledApp';
import { isAtHome } from '../../utils/homePath';

interface HistoryBackButtonProps {
    homePath: string;
}

// The top bar's Back, for where there is no browser Back to use (an installed
// web app, Vora Desktop). It steps back through the history, so the page comes
// back to the place it was left (useScrollRestoration). A page opened straight
// from a link has nothing behind it, so Back goes Home instead. Home itself
// has nowhere to go back to.
export default function HistoryBackButton({ homePath }: HistoryBackButtonProps) {
    const installed = useIsInstalledApp();
    const location = useLocation();
    const navigate = useNavigate();

    if (!installed || isAtHome(location.pathname, homePath)) return null;

    const goBack = () => {
        if (location.key === 'default') navigate(homePath, { replace: true });
        else navigate(-1);
    };

    return (
        <button
            type="button"
            onClick={goBack}
            aria-label="Back"
            title="Back"
            className="vora-icon-button flex h-10 w-10 shrink-0 cursor-pointer items-center justify-center rounded-full text-[var(--vora-text-secondary)] focus-visible:bg-[var(--vora-accent-500)]! focus-visible:text-[var(--vora-accent-contrast)]! focus-visible:outline-none! md:mr-4"
        >
            <svg className="h-6 w-6" fill="none" stroke="currentColor" strokeWidth={2} viewBox="0 0 24 24" aria-hidden="true">
                <path strokeLinecap="round" strokeLinejoin="round" d="M15 19l-7-7 7-7" />
            </svg>
        </button>
    );
}
