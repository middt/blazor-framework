import { ApplicationConfig, LOCALE_ID, provideBrowserGlobalErrorListeners } from '@angular/core';
import { registerLocaleData } from '@angular/common';
import localeTr from '@angular/common/locales/tr';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { provideMiddt } from '@middt/angular/core';
import { routes } from './app.routes';

registerLocaleData(localeTr);

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes, withComponentInputBinding()),
    { provide: LOCALE_ID, useValue: 'tr-TR' },
    // BFF defaults: same-origin /api proxy, /bff/user, /Account/Login|Logout.
    provideMiddt({ locale: 'tr-TR', apiVersion: '1.0' }),
  ],
};
