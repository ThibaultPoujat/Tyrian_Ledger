import { defineConfig, devices } from '@playwright/test';

const isCi = /^(true|1)$/i.test(process.env.CI ?? '');
const localHost = '127.0.0.1';
const frontendPort = process.env.TYRIAN_LEDGER_E2E_FRONTEND_PORT ?? '5174';
const frontendBaseUrl = `http://${localHost}:${frontendPort}`;

export default defineConfig({
  testDir: './tests',
  forbidOnly: isCi,
  retries: isCi ? 2 : 0,
  reporter: 'list',
  use: {
    baseURL: frontendBaseUrl,
    trace: 'retain-on-failure',
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
    {
      name: 'firefox',
      use: { ...devices['Desktop Firefox'] },
    },
    {
      name: 'webkit',
      use: { ...devices['Desktop Safari'] },
    },
  ],
  workers: 1,
  webServer: {
    command: `npm --prefix ../../frontend run build && npm --prefix ../../frontend run preview -- --host ${localHost} --port ${frontendPort} --strictPort`,
    url: frontendBaseUrl,
    reuseExistingServer: !isCi,
    timeout: 120_000,
  },
});
