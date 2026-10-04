import { useEffect, useState } from 'react';
import { useLocation, useMatch, useNavigate } from 'react-router-dom';
import { setupGuideService, type SetupGuideVM } from '../../../api/System/setupGuideService';
import { Modal } from '../../Common/Modal';
import { resolveAdminPath } from '../Shell/adminNavData';
import { rememberPromptedThisSession, wasPromptedThisSession } from './setupGuideSession';

export default function SetupGuidePrompt() {
    const location = useLocation();
    const navigate = useNavigate();
    const serverId = useMatch('/admin/server/:serverId/*')?.params.serverId;
    const onGuidePage = /\/setup\/?$/.test(location.pathname);
    const [guide, setGuide] = useState<SetupGuideVM | null>(null);

    useEffect(() => {
        if (onGuidePage || wasPromptedThisSession(serverId)) return;
        let cancelled = false;
        setupGuideService.get(serverId)
            .then(g => { if (!cancelled && (g.status === 'NotStarted' || g.status === 'InProgress')) setGuide(g); })
            .catch(() => { });
        return () => { cancelled = true; };
    }, [serverId, onGuidePage]);

    const close = () => {
        rememberPromptedThisSession(serverId);
        setGuide(null);
    };

    const continueSetup = () => {
        close();
        navigate(resolveAdminPath('/admin/setup', serverId));
    };

    const skip = async () => {
        const current = guide;
        close();
        if (current) await setupGuideService.save({ ...current, status: 'Skipped' }, serverId).catch(() => { });
    };

    const resuming = guide?.status === 'InProgress';

    return (
        <Modal isOpen={!!guide} onClose={close} size="md" surface="light">
            <div className="flex flex-col gap-4 p-6">
                <h2 className="text-lg font-semibold text-[var(--vora-text-primary)]">{resuming ? 'Pick up where you left off?' : 'Set up your server'}</h2>
                <p className="text-sm text-[var(--vora-text-secondary)]">
                    {resuming
                        ? "You haven't finished the setup guide. Your answers so far are saved."
                        : 'A short guide sets up playback and connects the services that fetch posters, details and ratings. It takes about 10 minutes.'}
                </p>
                <div className="flex flex-wrap items-center gap-2">
                    <button type="button" onClick={skip} className="mr-auto cursor-pointer text-sm font-semibold text-[var(--vora-text-secondary)] hover:text-[var(--vora-text-primary)]">Skip setup</button>
                    <button type="button" onClick={close} className="vora-button-secondary">Not now</button>
                    <button type="button" onClick={continueSetup} className="vora-button-primary" autoFocus>{resuming ? 'Continue setup' : 'Start setup'}</button>
                </div>
            </div>
        </Modal>
    );
}
