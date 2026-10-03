interface BrowseTileProps {
    name: string;
    artworkUrl?: string | null;
    detail?: string;
    onClick?: () => void;
    className?: string;
}

export default function BrowseTile({ name, artworkUrl, detail, onClick, className }: BrowseTileProps) {
    const body = (
        <>
            {artworkUrl && (
                <img src={artworkUrl} alt="" className="absolute inset-0 h-full w-full object-cover opacity-30 transition-opacity group-hover:opacity-40" />
            )}
            <span className="absolute inset-0 flex flex-col items-center justify-center p-3 text-center">
                <span className="font-bold drop-shadow-md" style={{ color: 'var(--vora-text-primary)', fontSize: 'clamp(1rem, 1.6vw, 1.5rem)', lineHeight: 1.15 }}>{name}</span>
                {detail && <span className="mt-1 text-xs" style={{ color: 'var(--vora-text-secondary)' }}>{detail}</span>}
            </span>
        </>
    );

    const frame = `group relative block aspect-square w-full overflow-hidden border ${className ?? ''}`;
    const style = {
        borderRadius: 'var(--vora-radius-md)',
        borderColor: 'var(--vora-border-subtle)',
        background: 'linear-gradient(135deg, var(--vora-accent-soft), var(--vora-bg-raised))',
    };

    return onClick ? (
        <button type="button" onClick={onClick} title={name} className={`${frame} cursor-pointer transition-colors hover:border-[var(--vora-accent-500)]`} style={style}>
            {body}
        </button>
    ) : (
        <div className={frame} style={style}>{body}</div>
    );
}
