import { describe, expect, it } from 'vitest';
import { EMAIL_PROVIDERS, guessEmailProvider } from './emailProviders';

describe('email providers', () => {
    it('recognizes the provider behind a saved SMTP server', () => {
        expect(guessEmailProvider('smtp.gmail.com')).toBe('gmail');
        expect(guessEmailProvider(' SMTP.Mail.Yahoo.com ')).toBe('yahoo');
        expect(guessEmailProvider('email-smtp.eu-west-1.amazonaws.com')).toBe('ses');
        expect(guessEmailProvider('smtppro.zoho.com')).toBe('zoho');
        expect(guessEmailProvider('smtp.zoho.eu')).toBe('zoho');
        expect(guessEmailProvider('smtp.eu.mailgun.org')).toBe('mailgun');
        expect(guessEmailProvider('mail.example.com')).toBe('other');
    });

    it('guesses nothing before a server is entered', () => {
        expect(guessEmailProvider(null)).toBeNull();
        expect(guessEmailProvider('  ')).toBeNull();
    });

    it('gives every usable provider server settings and only secure help links', () => {
        for (const provider of EMAIL_PROVIDERS) {
            provider.links.forEach(link => expect(link.url.startsWith('https://')).toBe(true));
            if (provider.unsupported || provider.kind === 'other') continue;
            expect(provider.smtp?.host).toBeTruthy();
            expect(provider.username).toBeTruthy();
            expect(provider.password).toBeTruthy();
            expect(provider.links.length).toBeGreaterThan(0);
        }
    });

    it('steers people away from personal Microsoft addresses, which only accept OAuth', () => {
        const outlook = EMAIL_PROVIDERS.find(p => p.id === 'outlook');
        expect(outlook?.smtp).toBeNull();
        expect(outlook?.unsupported).toMatch(/OAuth/);
    });
});
