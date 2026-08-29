import { defineConfig, devices } from '@playwright/test';

const isCi = /^(true|1)$/i.test(process.env.CI ?? '');
const localHost = '127.0.0.1';
const apiPort = process.env.TYRIAN_LEDGER_E2E_API_PORT ?? '5010';
const frontendPort = process.env.TYRIAN_LEDGER_E2E_FRONTEND_PORT ?? '5174';
const apiBaseUrl = `http://${localHost}:${apiPort}`;
const frontendBaseUrl = `http://${localHost}:${frontendPort}`;
const e2eSyntheticCredential = 'synthetic-gw2-api-credential-for-browser-boundary-audit';

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
  ],
  webServer: [
    {
      command: `dotnet run --project ../../src/Gw2Tp.Web/Gw2Tp.Web.csproj --no-launch-profile --urls ${apiBaseUrl}`,
      env: {
        ASPNETCORE_ENVIRONMENT: 'Testing',
        TYRIAN_LEDGER_GW2_API_KEY: e2eSyntheticCredential,
      },
      url: `${apiBaseUrl}/healthz`,
      reuseExistingServer: !isCi,
      timeout: 120_000,
    },
    {
      command: `npm --prefix ../../frontend run dev -- --host ${localHost} --port ${frontendPort} --strictPort`,
      env: { API_URL: apiBaseUrl },
      url: frontendBaseUrl,
      reuseExistingServer: !isCi,
      timeout: 120_000,
    },
  ],
});
