import { useNavigate } from 'react-router-dom';

export default function LinkDeviceCard() {
    const navigate = useNavigate();

    return (
        <section className="bg-[var(--vora-bg-sunken)] border border-[var(--vora-border-subtle)] rounded-lg p-6">
            <h2 className="text-xl font-bold m-0 mb-1 text-[var(--vora-text-primary)]">Link a TV or app</h2>
            <p className="text-sm m-0 mb-4 text-[var(--vora-text-muted)]">
                Sign in on a TV, Roku, Apple TV or desktop app without typing your password on a remote. Choose to sign in with a code on the device, then enter the code it shows.
            </p>
            <button type="button" onClick={() => navigate('/link')} className="vora-button-secondary cursor-pointer max-md:w-full">
                Enter a code
            </button>
        </section>
    );
}
