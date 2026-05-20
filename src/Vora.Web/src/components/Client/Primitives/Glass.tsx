import type { CSSProperties, ReactNode } from 'react';

interface GlassProps {
    children: ReactNode;
    className?: string;
    style?: CSSProperties;
    as?: 'div' | 'header' | 'section' | 'aside' | 'nav';
}

export default function Glass({ children, className, style, as: As = 'div' }: GlassProps) {
    return (
        <As className={`vora-glass ${className ?? ''}`} style={style}>
            {children}
        </As>
    );
}
