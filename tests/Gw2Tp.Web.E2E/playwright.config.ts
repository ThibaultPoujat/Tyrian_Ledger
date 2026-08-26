import { defineConfig, devices } from '@playwright/test';

const isCi = /^(true|1)$/i.test(process.env.CI ?? '');

export default defineConfig({
  testDir: './tests',
  forbidOnly: isCi,
  retries: isCi ? 2 : 0,
  reporter: 'list',
  use: {
    baseURL: 'http://127.0.0.1:5173',
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
      command: 'dotnet run --project ../../src/Gw2Tp.Web/Gw2Tp.Web.csproj --no-launch-profile --urls http://127.0.0.1:5000',
      url: 'http://127.0.0.1:5000/healthz',
      reuseExistingServer: !isCi,
      timeout: 120_000,
    },
    {
      command: 'npm --prefix ../../frontend run dev -- --host 127.0.0.1 --port 5173 --strictPort',
      url: 'http://127.0.0.1:5173',
      reuseExistingServer: !isCi,
      timeout: 120_000,
    },
  ],
});
