import { useEffect, useRef, useState } from 'react';
import { Modal, ModalBody, ModalFooter, ModalHeader } from '../Common/Modal';
import {
    libraryMigrationService,
    type LibrarySyncPinStatus,
    type LibrarySyncPinStatusVM,
    type LibrarySyncPinVM,
    type LibrarySyncTokenVM
} from '../../api/LibraryMigration/libraryMigrationService';

const POLL_INTERVAL_MS = 2000;

interface LibrarySyncPinModalProps {
    isOpen: boolean;
    providerId: string;
    providerName: string;
    serverId?: string;
    createPin?: (providerId: string, serverId?: string) => Promise<LibrarySyncPinVM>;
    pollPin?: (providerId: string, pinId: string, serverId?: string) => Promise<LibrarySyncPinStatusVM>;
    onClose: () => void;
    onAuthorized: (token: LibrarySyncTokenVM) => void;
}

type Phase = 'idle' | 'requesting' | 'awaiting' | 'success' | 'expired' | 'error';

export function LibrarySyncPinModal({
    isOpen,
    providerId,
    providerName,
    serverId,
    createPin = libraryMigrationService.createPin,
    pollPin = libraryMigrationService.pollPin,
    onClose,
    onAuthorized
}: LibrarySyncPinModalProps) {
    const [phase, setPhase] = useState<Phase>('idle');
    const [pin, setPin] = useState<LibrarySyncPinVM | null>(null);
    const [errorMessage, setErrorMessage] = useState<string | null>(null);
    const cancelledRef = useRef(false);

    const startFlow = async () => {
        cancelledRef.current = false;
        setPhase('requesting');
        setPin(null);
        setErrorMessage(null);
        try {
            const created = await createPin(providerId, serverId);
            if (cancelledRef.current) return;
            setPin(created);
            setPhase('awaiting');
        } catch (err) {
            if (cancelledRef.current) return;
            const message = err instanceof Error ? err.message : 'Failed to start the authorization flow.';
            setErrorMessage(message);
            setPhase('error');
        }
    };

    const startFlowRef = useRef(startFlow);
    useEffect(() => {
        startFlowRef.current = startFlow;
    });

    useEffect(() => {
        if (!isOpen) return;
        void startFlowRef.current();
        return () => {
            cancelledRef.current = true;
        };
    }, [isOpen, providerId, serverId]);

    useEffect(() => {
        if (phase !== 'awaiting' || !pin) return;

        let timer: ReturnType<typeof setTimeout> | null = null;

        const poll = async () => {
            try {
                const status = await pollPin(providerId, pin.pinId, serverId);
                if (cancelledRef.current) return;
                const resolved: LibrarySyncPinStatus = status.status;
                if (resolved === 'Authorized' && status.token) {
                    setPhase('success');
                    onAuthorized(status.token);
                    return;
                }
                if (resolved === 'Expired') {
                    setPhase('expired');
                    return;
                }
                timer = setTimeout(poll, POLL_INTERVAL_MS);
            } catch (err) {
                if (cancelledRef.current) return;
                const message = err instanceof Error ? err.message : 'Failed to poll for authorization.';
                setErrorMessage(message);
                setPhase('error');
            }
        };

        timer = setTimeout(poll, POLL_INTERVAL_MS);
        return () => {
            if (timer) clearTimeout(timer);
        };
    }, [phase, pin, providerId, serverId, onAuthorized]);

    const handleClose = () => {
        cancelledRef.current = true;
        setPhase('idle');
        setPin(null);
        setErrorMessage(null);
        onClose();
    };

    const codeDisplay = pin?.code ?? '----';

    return (
        <Modal isOpen={isOpen} onClose={handleClose} size="md" closeOnEscape>
            <ModalHeader
                title={`Connect ${providerName}`}
                subtitle="Authorize Vora to read watch state and ratings from your account."
                onClose={handleClose}
            />
            <ModalBody>
                {phase === 'requesting' && (
                    <p className="text-sm text-gray-300">Generating authorization code...</p>
                )}

                {phase === 'awaiting' && pin && (
                    <div className="space-y-4">
                        <ol className="text-sm text-gray-300 list-decimal list-inside space-y-1">
                            <li>Open <a href={pin.verificationUrl} target="_blank" rel="noopener noreferrer" className="text-[var(--vora-accent-text)] hover:underline">{pin.verificationUrl}</a></li>
                            <li>Sign in to your {providerName} account if asked.</li>
                            <li>Enter the code below and submit.</li>
                        </ol>
                        <div className="flex justify-center">
                            <div className="font-mono text-4xl tracking-[0.4em] px-6 py-4 rounded-lg border border-gray-700 bg-gray-900 text-white select-all">
                                {codeDisplay}
                            </div>
                        </div>
                        <p className="text-xs text-gray-500 text-center">Waiting for you to authorize the code...</p>
                    </div>
                )}

                {phase === 'success' && (
                    <p className="text-sm text-green-400">Authorized. You can close this window.</p>
                )}

                {phase === 'expired' && (
                    <p className="text-sm text-yellow-400">The authorization code expired before it was used. Generate a new one to try again.</p>
                )}

                {phase === 'error' && (
                    <p className="text-sm text-red-400">{errorMessage ?? 'Something went wrong.'}</p>
                )}
            </ModalBody>
            <ModalFooter>
                <div className="flex justify-between items-center">
                    <button
                        type="button"
                        onClick={handleClose}
                        className="px-4 py-2 text-sm text-gray-300 hover:text-white"
                    >
                        {phase === 'success' ? 'Done' : 'Cancel'}
                    </button>
                    {(phase === 'expired' || phase === 'error') && (
                        <button
                            type="button"
                            onClick={startFlow}
                            className="px-4 py-2 text-sm rounded-md bg-[var(--vora-accent-500)] text-white hover:opacity-90"
                        >
                            Try Again
                        </button>
                    )}
                </div>
            </ModalFooter>
        </Modal>
    );
}
