import { useState } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import type { IptvConnectionTestVM } from '../../../api/Iptv/iptvAdminService';
import IptvSourceFields from './IptvSourceFields';
import { emptySourceDraft, type IptvSourceDraft } from './iptvSourceDraft';

function Harness({ initial, passwordIsSet = false, onTest, onDraft }: { initial?: Partial<IptvSourceDraft>; passwordIsSet?: boolean; onTest?: () => Promise<IptvConnectionTestVM>; onDraft?: (draft: IptvSourceDraft) => void }) {
    const [draft, setDraft] = useState<IptvSourceDraft>({ ...emptySourceDraft(), ...initial });
    return (
        <IptvSourceFields
            idPrefix="t"
            kind="Tv"
            draft={draft}
            passwordIsSet={passwordIsSet}
            onChange={patch => setDraft(current => {
                const next = { ...current, ...patch };
                onDraft?.(next);
                return next;
            })}
            onTest={onTest ?? (() => Promise.resolve({ succeeded: true, channelCount: 0 }))}
        />
    );
}

describe('IptvSourceFields', () => {
    it('switches to an Xtream Codes account and asks for its server, username and password', () => {
        render(<Harness />);

        fireEvent.click(screen.getByRole('radio', { name: 'Xtream Codes account' }));

        expect(screen.getByRole('radio', { name: 'Xtream Codes account' })).toHaveAttribute('aria-checked', 'true');
        expect(screen.getByLabelText('Server address')).toBeInTheDocument();
        expect(screen.getByLabelText('Username')).toBeInTheDocument();
        expect(screen.getByLabelText('Password')).toHaveAttribute('type', 'password');
        expect(screen.getByRole('checkbox', { name: /Use the provider's guide/ })).toBeChecked();
        expect(screen.queryByLabelText('M3U URL')).not.toBeInTheDocument();
    });

    it('never shows a saved password and lets the admin change or clear it', () => {
        const drafts: IptvSourceDraft[] = [];
        render(<Harness initial={{ sourceType: 'Xtream', serverUrl: 'http://provider.example.com:8080', username: 'viewer' }} passwordIsSet onDraft={d => drafts.push(d)} />);

        expect(screen.getByText('A password is saved.')).toBeInTheDocument();
        expect(screen.queryByLabelText('Password')).not.toBeInTheDocument();

        fireEvent.click(screen.getByRole('button', { name: 'Clear' }));
        expect(screen.getByText('Will be cleared on save.')).toBeInTheDocument();
        expect(drafts.at(-1)?.password.clear).toBe(true);

        fireEvent.click(screen.getByRole('button', { name: 'Undo' }));
        fireEvent.click(screen.getByRole('button', { name: 'Change' }));
        expect(screen.getByLabelText('Password')).toHaveValue('');
    });

    it('keeps the HTTP login and headers of an M3U playlist under Advanced', () => {
        render(<Harness initial={{ m3uUrl: 'https://iptv.example.com/list.m3u' }} />);

        expect(screen.queryByLabelText('HTTP username')).not.toBeInTheDocument();
        fireEvent.click(screen.getByRole('button', { name: 'Advanced' }));

        expect(screen.getByLabelText('HTTP username')).toBeInTheDocument();
        expect(screen.getByLabelText('HTTP password')).toHaveAttribute('type', 'password');
        expect(screen.getByLabelText('User-Agent')).toBeInTheDocument();
        expect(screen.getByLabelText('Referer')).toBeInTheDocument();
    });

    it('opens Advanced when headers are already set', () => {
        render(<Harness initial={{ m3uUrl: 'https://iptv.example.com/list.m3u', userAgent: 'VLC/3.0' }} />);

        expect(screen.getByLabelText('User-Agent')).toHaveValue('VLC/3.0');
    });

    it('tests the connection and reports the account', async () => {
        const onTest = vi.fn(() => Promise.resolve({ succeeded: true, accountStatus: 'Active', accountMaxConnections: 1, channelCount: 120 }));
        render(<Harness initial={{ sourceType: 'Xtream', serverUrl: 'provider.example.com', username: 'viewer' }} passwordIsSet onTest={onTest} />);

        fireEvent.click(screen.getByRole('button', { name: 'Test connection' }));

        await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('Connected · account active · 1 connection · 120 channels'));
        expect(onTest).toHaveBeenCalledOnce();
    });

    it('cannot be tested until the account is complete', () => {
        render(<Harness initial={{ sourceType: 'Xtream', serverUrl: 'provider.example.com', username: 'viewer' }} />);

        expect(screen.getByRole('button', { name: 'Test connection' })).toBeDisabled();
        expect(screen.getByText('Enter the password.')).toBeInTheDocument();
    });
});
