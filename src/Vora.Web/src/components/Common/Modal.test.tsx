import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { Modal, ModalHeader, ModalBody, ModalFooter } from './Modal';

describe('Modal', () => {
    it('renders nothing when isOpen is false', () => {
        render(
            <Modal isOpen={false} onClose={() => { }}>
                <p>Hidden content</p>
            </Modal>
        );
        expect(screen.queryByText('Hidden content')).toBeNull();
    });

    it('renders children when isOpen is true', () => {
        render(
            <Modal isOpen={true} onClose={() => { }}>
                <p>Visible content</p>
            </Modal>
        );
        expect(screen.getByText('Visible content')).toBeInTheDocument();
    });

    it('sets role="dialog" and aria-modal="true" for accessibility', () => {
        render(
            <Modal isOpen={true} onClose={() => { }}>
                <p>x</p>
            </Modal>
        );
        const dialog = screen.getByRole('dialog');
        expect(dialog).toHaveAttribute('aria-modal', 'true');
    });

    it('fires onClose when backdrop is clicked and closeOnBackdropClick is true', () => {
        const onClose = vi.fn();
        const { container } = render(
            <Modal isOpen={true} onClose={onClose} closeOnBackdropClick>
                <p>Body</p>
            </Modal>
        );
        const overlay = container.firstChild as HTMLElement;
        fireEvent.click(overlay);
        expect(onClose).toHaveBeenCalledOnce();
    });

    it('does not fire onClose when backdrop is clicked and closeOnBackdropClick is false', () => {
        const onClose = vi.fn();
        const { container } = render(
            <Modal isOpen={true} onClose={onClose}>
                <p>Body</p>
            </Modal>
        );
        const overlay = container.firstChild as HTMLElement;
        fireEvent.click(overlay);
        expect(onClose).not.toHaveBeenCalled();
    });

    it('does not fire onClose when card body is clicked (event stopPropagation)', () => {
        const onClose = vi.fn();
        render(
            <Modal isOpen={true} onClose={onClose} closeOnBackdropClick>
                <p>Body</p>
            </Modal>
        );
        fireEvent.click(screen.getByText('Body'));
        expect(onClose).not.toHaveBeenCalled();
    });

    it('fires onClose when Escape pressed and closeOnEscape is true (default)', () => {
        const onClose = vi.fn();
        render(
            <Modal isOpen={true} onClose={onClose}>
                <p>x</p>
            </Modal>
        );
        fireEvent.keyDown(window, { key: 'Escape' });
        expect(onClose).toHaveBeenCalledOnce();
    });

    it('does not fire onClose on Escape when closeOnEscape is false', () => {
        const onClose = vi.fn();
        render(
            <Modal isOpen={true} onClose={onClose} closeOnEscape={false}>
                <p>x</p>
            </Modal>
        );
        fireEvent.keyDown(window, { key: 'Escape' });
        expect(onClose).not.toHaveBeenCalled();
    });

    it('does not fire onClose on non-Escape keys', () => {
        const onClose = vi.fn();
        render(
            <Modal isOpen={true} onClose={onClose}>
                <p>x</p>
            </Modal>
        );
        fireEvent.keyDown(window, { key: 'Enter' });
        fireEvent.keyDown(window, { key: 'a' });
        expect(onClose).not.toHaveBeenCalled();
    });

    it('applies the zIndex class to the overlay', () => {
        const { container } = render(
            <Modal isOpen={true} onClose={() => { }} zIndex="z-[210]">
                <p>x</p>
            </Modal>
        );
        const overlay = container.firstChild as HTMLElement;
        expect(overlay.className).toContain('z-[210]');
    });

    it('defaults zIndex to z-[200]', () => {
        const { container } = render(
            <Modal isOpen={true} onClose={() => { }}>
                <p>x</p>
            </Modal>
        );
        const overlay = container.firstChild as HTMLElement;
        expect(overlay.className).toContain('z-[200]');
    });
});

describe('ModalHeader', () => {
    it('renders title and subtitle', () => {
        render(<ModalHeader title="My Title" subtitle="My subtitle" onClose={() => { }} />);
        expect(screen.getByText('My Title')).toBeInTheDocument();
        expect(screen.getByText('My subtitle')).toBeInTheDocument();
    });

    it('fires onClose when close button clicked', () => {
        const onClose = vi.fn();
        render(<ModalHeader title="x" onClose={onClose} />);
        fireEvent.click(screen.getByLabelText('Close'));
        expect(onClose).toHaveBeenCalledOnce();
    });

    it('disables close button when closeDisabled is true', () => {
        render(<ModalHeader title="x" onClose={() => { }} closeDisabled />);
        expect(screen.getByLabelText('Close')).toBeDisabled();
    });

    it('renders tabs when provided', () => {
        render(<ModalHeader title="x" onClose={() => { }} tabs={<span>Tab A</span>} />);
        expect(screen.getByText('Tab A')).toBeInTheDocument();
    });
});

describe('ModalBody / ModalFooter', () => {
    it('ModalBody renders children', () => {
        render(<ModalBody>content</ModalBody>);
        expect(screen.getByText('content')).toBeInTheDocument();
    });

    it('ModalFooter renders children', () => {
        render(<ModalFooter>actions</ModalFooter>);
        expect(screen.getByText('actions')).toBeInTheDocument();
    });
});
