import { defineConfig, devices } from '@playwright/test';

const isCi = /^(true|1)$/i.test(process.env.CI ?? '');
const localHost = '127.0.0.1';
const hostPort = process.env.TYRIAN_LEDGER_E2E_HOST_PORT ?? '5081';
const hostBaseUrl = `http://${localHost}:${hostPort}`;

export default defineConfig({
  testDir: './tests',
  forbidOnly: isCi,
  retries: isCi ? 2 : 0,
  reporter: 'list',
  use: {
    baseURL: hostBaseUrl,
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
    command: 'npm --prefix ../../frontend run build && dotnet run --project ../../src/Gw2Tp.Web/Gw2Tp.Web.csproj --configuration Release --no-launch-profile',
    env: {
      ASPNETCORE_ENVIRONMENT: 'Production',
      TyrianLedger__Host__Port: hostPort,
    },
    url: `${hostBaseUrl}/api/health`,
    reuseExistingServer: !isCi,
    timeout: 120_000,
  },
});
