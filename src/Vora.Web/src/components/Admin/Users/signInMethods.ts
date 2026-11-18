import type { UserVM } from '../../../api/Users/userService';

export function signInMethods(user: Pick<UserVM, 'hasPassword' | 'signInProviders'>): string[] {
    const methods = (user.hasPassword ?? true) ? ['Password'] : [];
    return [...methods, ...(user.signInProviders ?? [])];
}

export function signInMethodsLine(user: Pick<UserVM, 'hasPassword' | 'signInProviders'>): string {
    const methods = signInMethods(user);
    return methods.length > 0 ? `Signs in with ${methods.join(' · ')}` : 'Waiting for their first provider sign-in';
}
