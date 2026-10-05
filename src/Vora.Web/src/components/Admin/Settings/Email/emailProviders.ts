import type { TlsMode } from './smtpSettings';

export interface EmailProviderLink {
    label: string;
    url: string;
}

export interface EmailProviderSmtp {
    host: string;
    port: number;
    security: TlsMode;
}

export interface EmailProvider {
    id: string;
    name: string;
    kind: 'mailbox' | 'service' | 'other';
    smtp: EmailProviderSmtp | null;
    username: string;
    password: string;
    notes: string[];
    links: EmailProviderLink[];
    unsupported?: string;
}

export const EMAIL_PROVIDERS: EmailProvider[] = [
    {
        id: 'gmail',
        name: 'Gmail',
        kind: 'mailbox',
        smtp: { host: 'smtp.gmail.com', port: 587, security: 'startTls' },
        username: 'Your full Gmail or Google Workspace address.',
        password: 'A 16-character app password, not your Google password. Google only offers app passwords once 2-Step Verification is on.',
        notes: [
            'Send from that same address, or an alias you have added in Gmail. Gmail replaces any other From address with your own.',
            'If App passwords is missing from your Google Account, 2-Step Verification is off, or your Workspace admin has turned app passwords off.',
        ],
        links: [
            { label: 'Create an app password', url: 'https://support.google.com/accounts/answer/185833' },
            { label: "Gmail's server settings", url: 'https://support.google.com/mail/answer/7126229' },
        ],
    },
    {
        id: 'microsoft365',
        name: 'Microsoft 365',
        kind: 'mailbox',
        smtp: { host: 'smtp.office365.com', port: 587, security: 'startTls' },
        username: 'The full address of the work or school mailbox Vora sends from.',
        password: "That mailbox's password.",
        notes: [
            'An administrator has to turn on Authenticated SMTP for the mailbox: Microsoft 365 admin center → Users → Active users → the user → Mail → Manage email apps. Organizations with security defaults on block it entirely.',
            'Microsoft is retiring password sign-in for SMTP. From the end of December 2026 it is off by default for existing organizations (an admin can still turn it back on), and it will be removed later. A sending service is the longer-term choice.',
        ],
        links: [
            { label: 'Turn on Authenticated SMTP', url: 'https://learn.microsoft.com/en-us/exchange/clients-and-mobile-in-exchange-online/authenticated-client-smtp-submission' },
        ],
    },
    {
        id: 'outlook',
        name: 'Outlook.com / Hotmail',
        kind: 'mailbox',
        smtp: null,
        username: '',
        password: '',
        notes: [],
        links: [
            { label: "Microsoft's announcement", url: 'https://support.microsoft.com/en-us/office/modern-authentication-methods-now-needed-to-continue-syncing-outlook-email-in-non-microsoft-email-apps-c5d65390-9676-4763-b41f-d7986499a90d' },
        ],
        unsupported: "Microsoft no longer lets apps send from personal Outlook.com, Hotmail, Live or MSN addresses with a password, app passwords included. Only Microsoft's own sign-in (OAuth) works, and Vora doesn't support it yet. Use another address or one of the sending services.",
    },
    {
        id: 'yahoo',
        name: 'Yahoo Mail',
        kind: 'mailbox',
        smtp: { host: 'smtp.mail.yahoo.com', port: 465, security: 'implicitSsl' },
        username: 'Your full Yahoo address.',
        password: 'An app password from Account security, not your Yahoo password.',
        notes: [],
        links: [
            { label: 'Create an app password', url: 'https://help.yahoo.com/kb/SLN15241.html' },
        ],
    },
    {
        id: 'icloud',
        name: 'iCloud Mail',
        kind: 'mailbox',
        smtp: { host: 'smtp.mail.me.com', port: 587, security: 'startTls' },
        username: 'Your full iCloud Mail address, for example name@icloud.com.',
        password: 'An app-specific password from your Apple Account. Two-factor authentication has to be on to make one.',
        notes: ['Send from your iCloud address or one of its aliases.'],
        links: [
            { label: 'Create an app-specific password', url: 'https://support.apple.com/en-us/102654' },
            { label: 'iCloud Mail server settings', url: 'https://support.apple.com/en-us/102525' },
        ],
    },
    {
        id: 'zoho',
        name: 'Zoho Mail',
        kind: 'mailbox',
        smtp: { host: 'smtp.zoho.com', port: 465, security: 'implicitSsl' },
        username: 'Your full Zoho Mail address.',
        password: 'Your Zoho password, or an application-specific password if two-factor authentication is on.',
        notes: [
            'Paid plans on your own domain use smtppro.zoho.com instead, and accounts in another Zoho region use that region (for example smtp.zoho.eu). Zoho Mail shows the exact server for your account in its settings.',
        ],
        links: [
            { label: "Zoho's SMTP settings", url: 'https://www.zoho.com/mail/help/zoho-smtp.html' },
        ],
    },
    {
        id: 'fastmail',
        name: 'Fastmail',
        kind: 'mailbox',
        smtp: { host: 'smtp.fastmail.com', port: 465, security: 'implicitSsl' },
        username: 'Your full Fastmail address.',
        password: 'An app password with SMTP access, not your Fastmail password.',
        notes: [],
        links: [
            { label: 'Create an app password', url: 'https://www.fastmail.help/hc/en-us/articles/360058752854' },
            { label: 'Server names and ports', url: 'https://www.fastmail.help/hc/en-us/articles/1500000278342' },
        ],
    },
    {
        id: 'proton',
        name: 'Proton Mail',
        kind: 'mailbox',
        smtp: { host: 'smtp.protonmail.ch', port: 587, security: 'startTls' },
        username: 'An address on your own domain in Proton Mail.',
        password: 'An SMTP token generated in Proton Mail settings. It is shown once, so copy it straight away.',
        notes: ['Only some paid plans can send this way (Proton for Business, Visionary and Family), and only from a custom-domain address.'],
        links: [
            { label: 'Set up SMTP submission', url: 'https://proton.me/support/smtp-submission' },
        ],
    },
    {
        id: 'sendgrid',
        name: 'SendGrid',
        kind: 'service',
        smtp: { host: 'smtp.sendgrid.net', port: 587, security: 'startTls' },
        username: 'The word apikey, exactly as written.',
        password: 'A SendGrid API key with Mail Send permission.',
        notes: ['Verify the From address or your domain in SendGrid first, or it refuses the mail.'],
        links: [
            { label: "SendGrid's SMTP guide", url: 'https://www.twilio.com/docs/sendgrid/for-developers/sending-email/integrating-with-the-smtp-api' },
        ],
    },
    {
        id: 'mailgun',
        name: 'Mailgun',
        kind: 'service',
        smtp: { host: 'smtp.mailgun.org', port: 587, security: 'startTls' },
        username: "The SMTP login from your sending domain's settings in Mailgun.",
        password: 'The password of that SMTP login, not your Mailgun account password.',
        notes: ["Domains in Mailgun's EU region use smtp.eu.mailgun.org."],
        links: [
            { label: "Mailgun's SMTP guide", url: 'https://documentation.mailgun.com/docs/mailgun/user-manual/sending-messages/send-smtp' },
        ],
    },
    {
        id: 'ses',
        name: 'Amazon SES',
        kind: 'service',
        smtp: { host: 'email-smtp.us-east-1.amazonaws.com', port: 587, security: 'startTls' },
        username: 'The SMTP user name you create in the SES console.',
        password: 'The SMTP password created with it. These are not your AWS access keys, and they only work in the region they were made in.',
        notes: [
            'Change us-east-1 in the server name to your SES region.',
            'New SES accounts start in the sandbox, where mail only reaches verified addresses. Ask Amazon for production access before inviting people.',
        ],
        links: [
            { label: 'Create SMTP credentials', url: 'https://docs.aws.amazon.com/ses/latest/dg/smtp-credentials.html' },
            { label: 'SES server names by region', url: 'https://docs.aws.amazon.com/ses/latest/dg/smtp-connect.html' },
        ],
    },
    {
        id: 'brevo',
        name: 'Brevo',
        kind: 'service',
        smtp: { host: 'smtp-relay.brevo.com', port: 587, security: 'startTls' },
        username: "The SMTP login shown on Brevo's SMTP & API page.",
        password: 'An SMTP key from the same page. An API key will not work.',
        notes: [],
        links: [
            { label: "Brevo's SMTP guide", url: 'https://help.brevo.com/hc/en-us/articles/7924908994450' },
        ],
    },
    {
        id: 'other',
        name: 'Something else',
        kind: 'other',
        smtp: null,
        username: 'Usually your full email address.',
        password: 'Usually an app password. Most providers no longer accept your normal password from other apps.',
        notes: [
            "Search your provider's help for its SMTP or outgoing mail server. It gives a server name, a port and the security: port 587 usually means STARTTLS, port 465 means SSL.",
        ],
        links: [],
    },
];

export const findEmailProvider = (id: string): EmailProvider | undefined => EMAIL_PROVIDERS.find(p => p.id === id);

const REGIONAL_HOSTS: Partial<Record<string, (host: string) => boolean>> = {
    ses: host => host.startsWith('email-smtp.') && host.endsWith('.amazonaws.com'),
    zoho: host => /^smtp(pro)?\.zoho\./.test(host),
    mailgun: host => host.endsWith('.mailgun.org'),
};

export function guessEmailProvider(host: string | null): string | null {
    const normalized = (host ?? '').trim().toLowerCase();
    if (!normalized) return null;
    const match = EMAIL_PROVIDERS.find(p => p.smtp?.host === normalized || REGIONAL_HOSTS[p.id]?.(normalized));
    return match?.id ?? 'other';
}
