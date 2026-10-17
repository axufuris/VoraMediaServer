import { Component, type ErrorInfo, type ReactNode } from 'react';
import EmptyState from '../Client/Primitives/EmptyState';

interface PageErrorBoundaryProps {
    resetKey: string;
    children: ReactNode;
}

interface PageErrorBoundaryState {
    failed: boolean;
}

export default class PageErrorBoundary extends Component<PageErrorBoundaryProps, PageErrorBoundaryState> {
    state: PageErrorBoundaryState = { failed: false };

    static getDerivedStateFromError(): PageErrorBoundaryState {
        return { failed: true };
    }

    componentDidCatch(error: Error, info: ErrorInfo) {
        console.error('A page failed to show', error, info.componentStack);
    }

    componentDidUpdate(previous: PageErrorBoundaryProps) {
        if (this.state.failed && previous.resetKey !== this.props.resetKey) {
            this.setState({ failed: false });
        }
    }

    render() {
        if (!this.state.failed) return this.props.children;
        return (
            <div data-vora-client="" className="fixed inset-0 z-[200] flex items-center justify-center" style={{ background: 'var(--vora-bg-canvas)' }}>
                <EmptyState
                    title="This page couldn't be shown"
                    description="Vora may have been updated since this tab opened. Reloading picks up the new version."
                    action={(
                        <div className="flex gap-3">
                            <button type="button" className="vora-button-primary cursor-pointer" onClick={() => window.location.reload()}>Reload</button>
                            <button type="button" className="vora-button-secondary cursor-pointer" onClick={() => window.location.assign('/')}>Go to Home</button>
                        </div>
                    )}
                />
            </div>
        );
    }
}
