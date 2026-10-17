import { EnvironmentProviders, InjectionToken, makeEnvironmentProviders } from '@angular/core';
import { MatPaginatorIntl } from '@angular/material/paginator';
import { UiApiClient, UiDataClient } from '@middt/ui-core';
import { UiPaginatorIntl } from './paginator-intl';
import { bffFetch } from './bff-fetch';

/** Settings of the Middt Angular frontend; the defaults match a host built with Middt.Framework.Bff. */
export interface MiddtConfig {
  /** Base URL of API calls; '' = same origin (the BFF proxies /api/** with the session's token). */
  apiBaseUrl: string;
  /** API version used in CRUD routes (api/v{version}/{entity}/…), like ApiSettings:Version. */
  apiVersion: string;
  /** BFF endpoints (GET {bffBaseUrl}/user). */
  bffBaseUrl: string;
  /** Server rendered login page of the BFF; `returnUrl` is appended. */
  loginUrl: string;
  /** Server rendered logout page of the BFF (POST with antiforgery). */
  logoutUrl: string;
  /** Server rendered page for signed-in users without a required role (SecuritySettings.AccessDeniedPageURL). */
  accessDeniedUrl: string;
  /** Locale for number/date formats of layouts. */
  locale: string;
  /** Role allowed to edit layouts at runtime (MiddtUiOptions.AdminRole). */
  uiAdminRole: string;
}

export const DEFAULT_MIDDT_CONFIG: MiddtConfig = {
  apiBaseUrl: '',
  apiVersion: '1.0',
  bffBaseUrl: '/bff',
  loginUrl: '/Account/Login',
  logoutUrl: '/Account/Logout',
  accessDeniedUrl: '/Account/AccessDenied',
  locale: 'tr-TR',
  uiAdminRole: 'ui-admin',
};

export const MIDDT_CONFIG = new InjectionToken<MiddtConfig>('MIDDT_CONFIG', { factory: () => DEFAULT_MIDDT_CONFIG });

/** Clients of @middt/ui-core configured for the BFF (CSRF header, cookie, login redirect on 401). */
export const UI_API_CLIENT = new InjectionToken<UiApiClient>('UI_API_CLIENT');
export const UI_DATA_CLIENT = new InjectionToken<UiDataClient>('UI_DATA_CLIENT');

/**
 * Registers the Middt frontend services:
 * <code>bootstrapApplication(App, { providers: [provideMiddt({ locale: 'tr-TR' }), …] })</code>
 */
export function provideMiddt(config: Partial<MiddtConfig> = {}): EnvironmentProviders {
  const settings: MiddtConfig = { ...DEFAULT_MIDDT_CONFIG, ...config };
  const options = { baseUrl: settings.apiBaseUrl, fetch: bffFetch(settings), credentials: 'same-origin' as const };
  return makeEnvironmentProviders([
    { provide: MIDDT_CONFIG, useValue: settings },
    { provide: UI_API_CLIENT, useFactory: () => new UiApiClient(options) },
    { provide: UI_DATA_CLIENT, useFactory: () => new UiDataClient({ ...options, apiVersion: settings.apiVersion }) },
    // Turkish paginator texts for every table (grids, workflow lists).
    { provide: MatPaginatorIntl, useClass: UiPaginatorIntl },
  ]);
}
