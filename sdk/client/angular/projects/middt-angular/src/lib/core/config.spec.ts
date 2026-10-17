import { describe, it, expect } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { DEFAULT_MIDDT_CONFIG, MIDDT_CONFIG, provideMiddt, UI_API_CLIENT, UI_DATA_CLIENT } from '../../../core/src/config';

describe('provideMiddt', () => {
  it('has Turkish defaults as per framework conventions', () => {
    expect(DEFAULT_MIDDT_CONFIG.locale).toBe('tr-TR');
    expect(DEFAULT_MIDDT_CONFIG.loginUrl).toBe('/Account/Login');
    expect(DEFAULT_MIDDT_CONFIG.logoutUrl).toBe('/Account/Logout');
    expect(DEFAULT_MIDDT_CONFIG.accessDeniedUrl).toBe('/Account/AccessDenied');
    expect(DEFAULT_MIDDT_CONFIG.uiAdminRole).toBe('ui-admin');
  });

  it('provides config and ui clients in TestBed', () => {
    TestBed.configureTestingModule({
      providers: [provideMiddt({ locale: 'en-US', apiVersion: '2.0' })],
    });

    const config = TestBed.inject(MIDDT_CONFIG);
    expect(config.locale).toBe('en-US');
    expect(config.apiVersion).toBe('2.0');
    expect(config.loginUrl).toBe('/Account/Login');

    const apiClient = TestBed.inject(UI_API_CLIENT);
    expect(apiClient).toBeDefined();

    const dataClient = TestBed.inject(UI_DATA_CLIENT);
    expect(dataClient).toBeDefined();
  });
});
