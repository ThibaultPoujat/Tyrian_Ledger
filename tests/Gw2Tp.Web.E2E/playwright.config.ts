import { defineConfig, devices } from '@playwright/test';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const isCi = /^(true|1)$/i.test(process.env.CI ?? '');
const localHost = '127.0.0.1';
const hostPort = process.env.TYRIAN_LEDGER_E2E_HOST_PORT ?? '5081';
const hostBaseUrl = `http://${localHost}:${hostPort}`;
const databasePath = join(tmpdir(), 'TyrianLedger.E2E', `${process.pid}-${Date.now()}`, 'tyrian-ledger.db');

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
      // Testing selects the environment-only source, and the explicitly blank
      // value guarantees this browser suite never reads a real OS credential.
      ASPNETCORE_ENVIRONMENT: 'Testing',
      TYRIAN_LEDGER_GW2_API_KEY: '',
      TyrianLedger__Host__Port: hostPort,
      TyrianLedger__Database__Path: databasePath,
    },
    url: `${hostBaseUrl}/api/health`,
    // Reusing a locally running host could bypass this suite's Testing
    // environment and accidentally reach a developer's credential vault.
    reuseExistingServer: false,
    timeout: 120_000,
  },
});
