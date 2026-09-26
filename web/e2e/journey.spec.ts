import { expect, test } from '@playwright/test';
import { seedDemo } from './demo-data';
import { totp } from './totp';

const EMAIL = 'owner@example.test';
const PASSWORD = 'a long demo passphrase';
const SETUP_TOKEN = process.env['E2E_SETUP_TOKEN'] ?? 'dev-setup-token-change-me-0123456789';
const shots = process.env['E2E_SCREENSHOTS'];

test('first run → MFA → quick add → dashboards', async ({ page, context }) => {
  test.setTimeout(180_000);
  const year = new Date().getFullYear();
  const month = new Date().getMonth() + 1;

  // Any Content-Security-Policy violation (inline script/style/handler) fails the journey.
  const cspViolations: string[] = [];
  page.on('console', (msg) => {
    if (/Content Security Policy|Refused to (execute|apply|load)/i.test(msg.text())) cspViolations.push(msg.text());
  });

  // First run: the app sends us to owner setup.
  await page.goto('/');
  // Styles really applied (a blocked stylesheet leaves an unstyled but functional page).
  expect(await page.locator('.card').first().evaluate((e) => getComputedStyle(e).borderTopLeftRadius)).not.toBe('0px');
  await expect(page).toHaveURL(/\/setup$/);
  await page.getByLabel('Setup token').fill(SETUP_TOKEN);
  await page.getByLabel('Email').fill(EMAIL);
  await page.getByLabel('New password').fill(PASSWORD);
  await page.getByRole('button', { name: 'Create account' }).click();

  // MFA enrolment is mandatory before any financial page is reachable.
  await expect(page).toHaveURL(/\/mfa-setup$/);
  const key = (await page.locator('p.font-mono').innerText()).trim();
  if (shots) await page.screenshot({ path: `${shots}/mfa-setup.png` });
  await page.getByRole('textbox').last().fill(totp(key));
  await page.getByRole('button', { name: 'Enable two-factor authentication' }).click();
  await expect(page.getByText("I've saved them")).toBeVisible();
  await page.getByRole('button', { name: "I've saved them" }).click();
  await expect(page).toHaveURL(/\/dashboard$/);

  await seedDemo(page.request, context, year, month);

  // Quick add with the keyboard only: N, amount, category chip, Enter.
  await page.reload();
  await expect(page.getByRole('button', { name: /Add transaction/ })).toBeVisible();
  await page.keyboard.press('n');
  const amount = page.getByLabel('Amount');
  await expect(amount).toBeFocused();
  await amount.fill('45,90');
  const chip = page.locator('app-quick-add').getByRole('button', { name: 'Restaurants' }).first();
  await chip.click();
  await expect(chip).toHaveClass(/chip-active/);
  await page.getByLabel('Description').fill('Dinner with friends');
  if (shots) await page.screenshot({ path: `${shots}/quick-add.png` });
  await page.getByLabel('Description').press('Enter');
  await expect(page.getByText('Transaction saved')).toBeVisible();

  await page.goto('/dashboard');
  await expect(page.getByText('Monthly view')).toBeVisible();
  await page.waitForTimeout(800);
  if (shots) await page.screenshot({ path: `${shots}/dashboard.png`, fullPage: true });

  await page.goto(`/monthly?period=${year}-${String(month).padStart(2, '0')}`);
  await expect(page.getByText('Dinner with friends')).toHaveCount(0); // lives in the transactions list, not here
  await expect(page.getByRole('cell', { name: 'Restaurants' }).first()).toBeVisible();
  await page.waitForTimeout(800);
  if (shots) await page.screenshot({ path: `${shots}/monthly.png`, fullPage: true });

  await page.goto('/annual');
  await page.waitForTimeout(800);
  if (shots) await page.screenshot({ path: `${shots}/annual.png`, fullPage: true });

  await page.goto('/transactions');
  await expect(page.getByText('Dinner with friends')).toBeVisible();
  if (shots) await page.screenshot({ path: `${shots}/transactions.png` });

  await page.goto('/budgets');
  await expect(page.getByText('Income allocation')).toBeVisible();
  if (shots) await page.screenshot({ path: `${shots}/budgets.png` });

  // Read-only broker connections (fictitious demo broker): same ETF at two "brokers" is consolidated.
  await page.goto('/connections');
  for (const profile of ['a', 'b']) {
    await page.getByRole('button', { name: /Demo broker/ }).click();
    await page.getByLabel('Name').fill(`Demo broker ${profile.toUpperCase()}`);
    await page.getByLabel('Profile').fill(profile);
    await page.getByRole('button', { name: 'Save' }).click();
    await expect(page.getByText(`Demo broker ${profile.toUpperCase()}`, { exact: true })).toBeVisible();
  }
  await expect(page.getByText('Succeeded', { exact: false })).toHaveCount(2, { timeout: 60_000 });
  if (shots) await page.screenshot({ path: `${shots}/connections.png` });

  await page.goto('/portfolio');
  const vwce = page.getByRole('row').filter({ hasText: 'VWCE' }).first();
  await expect(vwce).toBeVisible();
  await expect(vwce.getByText('Demo broker A')).toBeVisible();
  await expect(vwce.getByText('Demo broker B')).toBeVisible();
  await expect(page.getByText('TWR')).toBeVisible();
  await page.waitForTimeout(1000);
  if (shots) await page.screenshot({ path: `${shots}/portfolio.png`, fullPage: true });

  await page.goto('/net-worth');
  await expect(page.getByText('Net worth over time')).toBeVisible();
  await page.waitForTimeout(800);
  if (shots) await page.screenshot({ path: `${shots}/net-worth.png`, fullPage: true });

  // AI access: a client with summary scopes only, used through the real MCP endpoint.
  await page.goto('/ai');
  await page.getByRole('button', { name: /New AI client/ }).click();
  await page.locator('#ai-name').fill('Claude Code');
  await page.getByRole('button', { name: 'Save' }).click();
  const tokenBlock = page.locator('dialog[open] pre').first();
  await expect(tokenBlock).toContainText('pf_');
  const token = (await tokenBlock.innerText()).trim();
  expect(token).toMatch(/^pf_[0-9a-f]{8}_/);
  if (shots) await page.screenshot({ path: `${shots}/ai-token.png` });
  await page.keyboard.press('Escape');

  const mcp = async (method: string, params: object = {}) => {
    const res = await page.request.post('/mcp', {
      headers: { Authorization: `Bearer ${token}`, Accept: 'application/json, text/event-stream', 'Content-Type': 'application/json' },
      data: { jsonrpc: '2.0', id: 1, method, params },
    });
    expect(res.status()).toBe(200);
    const text = await res.text();
    // Streamable HTTP may answer as a single SSE event.
    const json = text.startsWith('{') ? text : text.split('\n').find((l) => l.startsWith('data:'))!.slice(5);
    return JSON.parse(json).result;
  };
  await mcp('initialize', { protocolVersion: '2025-06-18', capabilities: {}, clientInfo: { name: 'e2e', version: '1' } });
  const tools = (await mcp('tools/list')).tools.map((t: { name: string }) => t.name).sort();
  expect(tools).toEqual(['get_budget_status', 'get_expense_summary', 'get_financial_overview', 'get_goals', 'get_income_summary', 'get_monthly_summary']);
  const call = await mcp('tools/call', { name: 'get_expense_summary', arguments: { category: 'restaurants' } });
  expect(call.content[0].text).toContain('"category":"Restaurants"');
  const denied = await page.request.post('/mcp', {
    headers: { Authorization: 'Bearer pf_00000000_invalid', Accept: 'application/json, text/event-stream' },
    data: { jsonrpc: '2.0', id: 1, method: 'tools/list' },
  });
  expect(denied.status()).toBe(401);

  await page.goto('/ai');
  await expect(page.getByRole('cell', { name: 'get_expense_summary' }).first()).toBeVisible();
  if (shots) await page.screenshot({ path: `${shots}/ai-access.png`, fullPage: true });

  // Language switch is instant and total.
  await page.goto('/dashboard');
  await page.getByLabel('Language').selectOption('pt-PT');
  await expect(page.getByText('Visão mensal')).toBeVisible();
  await page.waitForTimeout(800);
  if (shots) await page.screenshot({ path: `${shots}/dashboard-pt.png` });

  expect(cspViolations).toEqual([]);

  // Dark mode.
  await page.getByLabel('Language').selectOption('en');
  await page.goto('/monthly');
  await page.evaluate(() => localStorage.setItem('pf.theme', 'dark'));
  await page.reload();
  await page.waitForTimeout(1000);
  if (shots) await page.screenshot({ path: `${shots}/monthly-dark.png`, fullPage: true });
});
