import { computed, inject, Injectable, signal } from '@angular/core';
import { CSRF_HEADER, loginUrl } from './bff-fetch';
import { MIDDT_CONFIG } from './config';

/** The signed-in user as reported by the BFF (GET /bff/user); tokens stay on the server. */
export interface MiddtUser {
  isAuthenticated: boolean;
  id?: string | null;
  name?: string | null;
  roles: string[];
}

const ANONYMOUS: MiddtUser = { isAuthenticated: false, roles: [] };

@Injectable({ providedIn: 'root' })
export class AuthService {
  readonly #config = inject(MIDDT_CONFIG);
  readonly #user = signal<MiddtUser>(ANONYMOUS);
  #loading: Promise<MiddtUser> | null = null;

  readonly user = this.#user.asReadonly();
  readonly isAuthenticated = computed(() => this.#user().isAuthenticated);
  readonly name = computed(() => this.#user().name ?? '');

  /** Loads the user once (cached); call `reload()` to refresh. */
  load(): Promise<MiddtUser> {
    this.#loading ??= this.reload();
    return this.#loading;
  }

  async reload(): Promise<MiddtUser> {
    try {
      const response = await fetch(`${this.#config.bffBaseUrl}/user`, { credentials: 'same-origin', headers: { [CSRF_HEADER]: '1' } });
      const user = response.ok ? ((await response.json()) as MiddtUser) : ANONYMOUS;
      this.#user.set({ ...ANONYMOUS, ...user, roles: user.roles ?? [] });
    } catch {
      this.#user.set(ANONYMOUS);
    }

    this.#loading = Promise.resolve(this.#user());
    return this.#user();
  }

  hasRole(role: string): boolean {
    return this.#user().roles.includes(role);
  }

  /** Full page navigation to the BFF login page (server rendered form with antiforgery). */
  login(returnUrl?: string): void {
    location.assign(loginUrl(this.#config, returnUrl));
  }

  /**
   * Full page navigation to the BFF access denied page (signed in, role missing). Never the login page: with an identity
   * provider it signs in silently and would return to the forbidden page in a loop.
   */
  accessDenied(): void {
    location.assign(this.#config.accessDeniedUrl);
  }

  /** Full page navigation to the BFF logout page: ends the server session and revokes the refresh token. */
  logout(): void {
    location.assign(this.#config.logoutUrl);
  }
}
