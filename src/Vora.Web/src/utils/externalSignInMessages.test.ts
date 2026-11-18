import { describe, it, expect } from 'vitest';
import { AxiosError, AxiosHeaders } from 'axios';
import { pendingSignInHandle } from './externalSignInMessages';

const failure = (status: number, data: unknown) => new AxiosError('failed', String(status), undefined, undefined, {
    status, statusText: '', headers: {}, config: { headers: new AxiosHeaders() }, data,
});

describe('pendingSignInHandle', () => {
    it('reads the handle from a 409 that carries one', () => {
        expect(pendingSignInHandle(failure(409, { title: 'More is needed', pending: 'abc', outcome: 'NeedsLinkConfirmation' }))).toBe('abc');
    });

    it.each([
        [failure(409, { title: 'Conflict' })],
        [failure(409, { pending: '' })],
        [failure(409, { pending: 12 })],
        [failure(409, 'plain text')],
        [failure(409, null)],
        [failure(401, { pending: 'abc' })],
        [failure(403, { pending: 'abc' })],
        [new Error('network')],
        ['nope'],
        [null],
    ])('is null for %#', error => {
        expect(pendingSignInHandle(error)).toBeNull();
    });
});
