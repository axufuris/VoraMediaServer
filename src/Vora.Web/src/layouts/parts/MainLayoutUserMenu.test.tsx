import { afterEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import MainLayoutUserMenu from './MainLayoutUserMenu';

vi.mock('../../components/Layout/SearchBar', () => ({ default: () => <div>Search</div> }));

const noop = () => { };

const renderBar = (path: string, homePath = '/') => render(
    <MemoryRouter initialEntries={['/', path]} initialIndex={1}>
        <MainLayoutUserMenu
            profileName="Andy"
            homePath={homePath}
            isProfileMenuOpen={false}
            isServerAdmin
            isPhoneNavOpen={false}
            onOpenPhoneNav={noop}
            onToggleMenu={noop}
            onCloseMenu={noop}
            onManageServers={noop}
            onServerSettings={noop}
            onClientSettings={noop}
            onAccountSettings={noop}
            onPlayHistory={noop}
            onSwitchProfile={noop}
            onSignOut={noop}
        />
    </MemoryRouter>,
);

describe('MainLayoutUserMenu Back button', () => {
    afterEach(() => {
        cleanup();
        vi.unstubAllGlobals();
    });

    it('sits first in the top bar of an installed app, before the navigation button', () => {
        vi.stubGlobal('matchMedia', () => ({ matches: true, addEventListener: () => { }, removeEventListener: () => { } }));

        renderBar('/media/1');

        const buttons = screen.getAllByRole('button');
        expect(buttons[0]).toHaveAccessibleName('Back');
        expect(buttons[1]).toHaveAccessibleName('Open navigation');
    });

    it('is left out of a browser tab', () => {
        vi.stubGlobal('matchMedia', () => ({ matches: false, addEventListener: () => { }, removeEventListener: () => { } }));

        renderBar('/media/1');

        expect(screen.queryByRole('button', { name: 'Back' })).toBeNull();
    });

    it('is left out of Home even in an installed app', () => {
        vi.stubGlobal('matchMedia', () => ({ matches: true, addEventListener: () => { }, removeEventListener: () => { } }));

        renderBar('/');

        expect(screen.queryByRole('button', { name: 'Back' })).toBeNull();
    });
});
