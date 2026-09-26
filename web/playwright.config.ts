import { defineConfig, devices } from '@playwright/test';

/**
 * E2E against a running stack (API + web) with an EMPTY database — the journey starts at first-run setup.
 * Locally: `docker compose -f deploy/compose.dev.yml up -d`, `dotnet run --project src/Host.Api`, `npm start`.
 */
export default defineConfig({
  testDir: './e2e',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  reporter: [['list']],
  use: {
    baseURL: process.env['E2E_BASE_URL'] ?? 'http://localhost:4200',
    trace: 'retain-on-failure',
    // Local try-out mode uses Caddy's self-signed certificate.
    ignoreHTTPSErrors: true,
    viewport: { width: 1440, height: 900 },
    locale: 'en-IE',
    timezoneId: 'Europe/Lisbon',
    // Against the production stack, simulate the HTTPS terminator (tailscale serve) in front of Caddy.
    extraHTTPHeaders: process.env['E2E_FORWARDED_HTTPS'] ? { 'X-Forwarded-Proto': 'https' } : undefined,
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'], viewport: { width: 1440, height: 900 } } }],
});
