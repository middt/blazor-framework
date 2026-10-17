import type { FetchLike } from '@middt/ui-core';
import type { MiddtConfig } from './config';

/** Header the BFF requires on proxied API calls: a cross-site page cannot set it without a CORS preflight (CSRF protection). */
export const CSRF_HEADER = 'X-CSRF';

/** Login page URL that returns to the current location afterwards. */
export function loginUrl(config: MiddtConfig, returnUrl = location.pathname + location.search): string {
  return `${config.loginUrl}?returnUrl=${encodeURIComponent(returnUrl)}`;
}

/**
 * fetch for calls through the BFF: same-origin cookie, the CSRF header, and a full page redirect to the login page when
 * the session ended (401) – the BFF owns the tokens, the browser never sees them.
 */
export function bffFetch(config: MiddtConfig): FetchLike {
  return async (input, init) => {
    const headers = new Headers(init?.headers);
    headers.set(CSRF_HEADER, '1');
    const response = await fetch(input, { ...init, headers, credentials: 'same-origin' });
    if (response.status === 401) {
      location.assign(loginUrl(config));
    }

    return response;
  };
}
