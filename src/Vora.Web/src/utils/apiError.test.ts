import { describe, it, expect } from 'vitest';
import { AxiosError, AxiosHeaders } from 'axios';
import { errorDetail, resolveReason } from './apiError';

// Adding a collection poster by URL failed with a fixed "Failed to add URL"
// while the server had answered 400 with the exact reason in ProblemDetails.
// Diagnosing it meant reading the container log for something the response
// already carried.
const problem = (body: unknown, status = 400) => new AxiosError(
    'Request failed with status code 400',
    'ERR_BAD_REQUEST',
    undefined,
    null,
    { status, data: body, statusText: 'Bad Request', headers: new AxiosHeaders(), config: { headers: new AxiosHeaders() } },
);

describe('reading a reason off a failed request', () => {
    it('takes the detail the API sent', () => {
        expect(resolveReason(problem({ title: 'Invalid operation', detail: "Remote server returned 403." })))
            .toBe('Remote server returned 403.');
    });

    // "Invalid operation" is the status restated, but it beats saying nothing.
    it('falls back to the title when there is no detail', () => {
        expect(resolveReason(problem({ title: 'Invalid operation' }))).toBe('Invalid operation');
    });

    it('reads a plain-text body', () => {
        expect(resolveReason(problem('Collection not found'))).toBe('Collection not found');
    });

    // A proxy's HTML error page is noise, not an explanation.
    it('ignores an HTML body', () => {
        expect(resolveReason(problem('<!DOCTYPE html><title>502</title>'))).toBeUndefined();
    });

    it.each([{}, null, { status: 400 }, ''])('ignores a body with nothing to say: %s', body => {
        expect(resolveReason(problem(body))).toBeUndefined();
    });

    it('ignores a blank detail', () => {
        expect(resolveReason(problem({ detail: '   ' }))).toBeUndefined();
    });

    // A request that never reached the server has a cause too, and reporting it
    // as a server rejection sends the viewer looking in the wrong place.
    it('names a network failure', () => {
        expect(resolveReason(new AxiosError('Network Error', 'ERR_NETWORK'))).toBe('Network Error');
    });

    it('reads a plain Error', () => {
        expect(resolveReason(new Error('boom'))).toBe('boom');
    });

    it.each([undefined, null, 'a string', 42])('has no reason for %s', value => {
        expect(resolveReason(value)).toBeUndefined();
    });
});

describe('building the message shown to the viewer', () => {
    // The fallback carries the context the server cannot know — which action
    // failed — so it stays in front of the server's reason.
    it('keeps the local context and appends the reason', () => {
        expect(errorDetail(problem({ detail: 'The URL did not return an image.' }), 'Failed to add URL'))
            .toBe('Failed to add URL: The URL did not return an image.');
    });

    it('shows the fallback alone when nothing explains the failure', () => {
        expect(errorDetail(problem({}), 'Failed to add URL')).toBe('Failed to add URL');
    });

    it('does not append an empty reason as a dangling colon', () => {
        expect(errorDetail(undefined, 'Failed to add URL')).toBe('Failed to add URL');
    });
});
