import { defineConfig, devices } from '@playwright/test';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const isCi = /^(true|1)$/i.test(process.env.CI ?? '');
const localHost = '127.0.0.1';
const hostPort = process.env.TYRIAN_LEDGER_E2E_HOST_PORT ?? '5081';
const previewPort = process.env.TYRIAN_LEDGER_P05A_PORT ?? '5181';
const hostBaseUrl = 'http://' + localHost + ':' + hostPort;
const previewBaseUrl = 'http://' + localHost + ':' + previewPort;
const databasePath = join(tmpdir(), 'TyrianLedger.E2E', String(process.pid) + '-' + String(Date.now()), 'tyrian-ledger.db');

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
      testIgnore: '**/p05a-preview.spec.ts',
      use: { ...devices['Desktop Chrome'] },
    },
    {
      name: 'firefox',
      testIgnore: '**/p05a-preview.spec.ts',
      use: { ...devices['Desktop Firefox'] },
    },
    {
      name: 'webkit',
      testIgnore: '**/p05a-preview.spec.ts',
      use: { ...devices['Desktop Safari'] },
    },
    {
      name: 'p05a-preview',
      testMatch: '**/p05a-preview.spec.ts',
      use: { ...devices['Desktop Chrome'], baseURL: previewBaseUrl },
    },
  ],
  workers: 1,
  webServer: [
    {
      command: 'npm --prefix ../../frontend run build && dotnet run --project ../../src/Gw2Tp.Web/Gw2Tp.Web.csproj --configuration Release --no-launch-profile',
      env: {
        ASPNETCORE_ENVIRONMENT: 'Testing',
        TYRIAN_LEDGER_GW2_API_KEY: '',
        TyrianLedger__Host__Port: hostPort,
        TyrianLedger__Database__Path: databasePath,
      },
      url: hostBaseUrl + '/api/health',
      reuseExistingServer: false,
      timeout: 120_000,
    },
    {
      command: 'npm --prefix ../../frontend run preview:p05a -- --port ' + previewPort + ' --strictPort',
      url: previewBaseUrl + '/preview.html?scenario=normal',
      reuseExistingServer: false,
      timeout: 120_000,
    },
  ],
});
