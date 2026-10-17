import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: '.',
  testMatch: '*.spec.ts',
  retries: process.env.CI ? 1 : 0,
  timeout: 60_000,
  use: { browserName: 'chromium', trace: 'retain-on-failure' },
  reporter: process.env.CI ? 'github' : 'list',
});
