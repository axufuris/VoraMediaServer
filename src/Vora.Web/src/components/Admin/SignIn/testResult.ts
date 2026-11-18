import type { LdapTestResult, ProviderTestResult } from '../../../api/Auth/signInProvidersAdminService';

export const isLdapTestResult = (result: ProviderTestResult): result is LdapTestResult => 'connected' in result;
