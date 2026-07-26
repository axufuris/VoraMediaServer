export default function InLibraryBadge() {
    return (
        <span
            className="flex items-center gap-1 rounded px-1.5 py-0.5 text-[10px] font-bold uppercase tracking-wide shadow"
            style={{ background: 'var(--vora-accent-500)', color: 'var(--vora-on-accent, #fff)' }}
        >
            <svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="3">
                <path strokeLinecap="round" strokeLinejoin="round" d="M5 13l4 4L19 7" />
            </svg>
            In Library
        </span>
    );
}
