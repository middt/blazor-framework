import { inject } from '@angular/core';
import type { CanActivateFn } from '@angular/router';
import { AuthService } from './auth.service';

/** Requires a signed-in user; otherwise redirects to the BFF login page. */
export const middtAuthGuard: CanActivateFn = async (_route, state) => {
  const auth = inject(AuthService);
  const user = await auth.load();
  if (!user.isAuthenticated) {
    auth.login(state.url);
    return false;
  }

  return true;
};

/** Requires one of <paramref name="roles"/> (after authentication). */
export function middtRoleGuard(...roles: string[]): CanActivateFn {
  return async (route, state) => {
    const auth = inject(AuthService);
    const user = await auth.load();
    if (!user.isAuthenticated) {
      auth.login(state.url);
      return false;
    }

    return roles.some(role => user.roles.includes(role));
  };
}
