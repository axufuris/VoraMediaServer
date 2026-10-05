import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import EmailProviderGuide from './EmailProviderGuide';

describe('EmailProviderGuide', () => {
    it("shows a provider's settings and where to get the password, then fills them in", () => {
        const onApply = vi.fn();
        render(<EmailProviderGuide onApply={onApply} />);

        fireEvent.click(screen.getByRole('button', { name: 'Gmail' }));

        expect(screen.getByText('smtp.gmail.com')).toBeInTheDocument();
        expect(screen.getByRole('link', { name: /Create an app password/ })).toHaveAttribute('href', 'https://support.google.com/accounts/answer/185833');
        fireEvent.click(screen.getByRole('button', { name: 'Fill in server settings' }));
        expect(onApply).toHaveBeenCalledWith({ host: 'smtp.gmail.com', port: 587, security: 'startTls' });
        expect(screen.getByRole('status')).toHaveTextContent('Add the username and password below');
    });

    it('opens on the provider already in use', () => {
        render(<EmailProviderGuide initialProviderId="fastmail" onApply={vi.fn()} />);

        expect(screen.getByRole('button', { name: 'Fastmail' })).toHaveAttribute('aria-pressed', 'true');
        expect(screen.getByText('smtp.fastmail.com')).toBeInTheDocument();
    });

    it('explains why a personal Outlook.com address cannot be used and offers nothing to fill in', () => {
        render(<EmailProviderGuide onApply={vi.fn()} />);

        fireEvent.click(screen.getByRole('button', { name: 'Outlook.com / Hotmail' }));

        expect(screen.getByText(/Vora doesn't support it yet/)).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: 'Fill in server settings' })).not.toBeInTheDocument();
    });
});
