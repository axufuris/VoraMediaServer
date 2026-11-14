interface LockButtonProps {
    locked: boolean;
    onToggle: () => void;
}

export default function LockButton({ locked, onToggle }: LockButtonProps) {
    return (
        <button type="button" onClick={onToggle} className={`ml-2 focus:outline-none transition-colors cursor-pointer ${locked ? 'text-[var(--vora-accent-500)] hover:text-[var(--vora-accent-500)]' : 'text-[var(--vora-text-muted)] hover:text-[var(--vora-text-muted)]'}`}>
            {locked ? <svg className="w-4 h-4 inline-block" fill="currentColor" viewBox="0 0 20 20"><path fillRule="evenodd" d="M5 9V7a5 5 0 0110 0v2a2 2 0 012 2v5a2 2 0 01-2 2H5a2 2 0 01-2-2v-5a2 2 0 012-2zm8-2v2H7V7a3 3 0 016 0z" clipRule="evenodd" /></svg> : <svg className="w-4 h-4 inline-block" fill="currentColor" viewBox="0 0 20 20"><path fillRule="evenodd" d="M10 2a5 5 0 00-5 5v2a2 2 0 00-2 2v5a2 2 0 002 2h10a2 2 0 002-2v-5a2 2 0 00-2-2H7V7a3 3 0 015.905-.75 1 1 0 001.937-.5A5.002 5.002 0 0010 2z" clipRule="evenodd" /></svg>}
        </button>
    );
}
