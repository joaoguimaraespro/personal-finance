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
  await expect(page.getByAltText('QR code for your authenticator app')).toBeVisible();
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
  const chip = page.getByRole('dialog').getByRole('button', { name: 'Restaurants' }).first();
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

  // Notification centre: the seeded subscription is due today and can be confirmed from the header bell.
  const bell = page.getByTestId('notification-bell');
  await expect(bell).toHaveAttribute('aria-label', /^Notifications, \d+ new$/);
  await bell.click();
  const centre = page.getByRole('dialog', { name: 'Notifications' });
  await expect(centre.getByText('Music streaming')).toBeVisible();
  await expect(bell).toHaveAttribute('aria-label', 'Notifications'); // opening marks everything as seen
  if (shots) await page.screenshot({ path: `${shots}/notifications.png` });
  await centre.getByRole('button', { name: 'Confirm · Music streaming' }).click();
  await expect(centre.getByText('Music streaming')).toHaveCount(0);
  await page.keyboard.press('Escape');
  await expect(centre).toHaveCount(0);

  await page.goto(`/monthly?period=${year}-${String(month).padStart(2, '0')}`);
  await expect(page.getByText('Dinner with friends')).toHaveCount(0); // lives in the transactions list, not here
  await expect(page.getByRole('cell', { name: 'Restaurants' }).first()).toBeVisible();
  await page.waitForTimeout(800);
  if (shots) await page.screenshot({ path: `${shots}/monthly.png`, fullPage: true });

  await page.goto('/annual');
  await page.waitForTimeout(800);
  if (shots) await page.screenshot({ path: `${shots}/annual.png`, fullPage: true });

  await page.goto('/transactions');
  // Responsive layout renders the row in both the desktop table and the
  // mobile card list (one hidden via CSS depending on viewport) — filter
  // to the one that is actually visible to avoid a strict-mode violation.
  await expect(page.getByText('Dinner with friends').and(page.locator(':visible'))).toBeVisible();
  if (shots) await page.screenshot({ path: `${shots}/transactions.png` });

  // One movement split over two categories: the remaining amount must reach zero before saving.
  await page.keyboard.press('n');
  await expect(page.getByLabel('Amount', { exact: true })).toBeFocused();
  await page.getByLabel('Amount', { exact: true }).fill('100');
  await page.getByRole('dialog').getByRole('button', { name: 'Groceries' }).first().click();
  await page.getByTestId('split-start').click();
  await page.locator('#qa-split-amt-0').fill('60');
  await page.locator('#qa-split-cat-1').click();
  await page.getByRole('option', { name: 'Restaurants' }).click();
  await expect(page.getByTestId('split-remaining')).toContainText('40');
  await page.getByRole('button', { name: 'Fill remaining' }).click();
  await expect(page.getByTestId('split-remaining')).toHaveText('Adds up to the total');
  await page.locator('#qa-desc').fill('Market and lunch');
  await page.locator('#qa-desc').press('Enter');
  await expect(page.getByText('Transaction saved')).toBeVisible();
  const splitRow = page.getByRole('row').filter({ hasText: 'Market and lunch' });
  await expect(splitRow.getByTestId('split-toggle')).toHaveText(/2 categories/);
  await splitRow.getByTestId('split-toggle').click();
  await expect(splitRow.getByTestId('split-list')).toContainText('Restaurants');

  await page.goto('/budgets');
  await expect(page.getByText('Income allocation')).toBeVisible();
  if (shots) await page.screenshot({ path: `${shots}/budgets.png` });

  // Read-only broker connections (fictitious demo broker): same ETF at two "brokers" is consolidated.
  await page.goto('/connections');
  for (const profile of ['a', 'b']) {
    // The empty state repeats the add buttons; use the page header's.
    await page.locator('app-page-header').getByRole('button', { name: /Demo broker/ }).click();
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
  // Same ISIN at two brokers: one consolidated line; expanding it lists each broker holding.
  await vwce.click();
  await expect(vwce).toHaveAttribute('aria-expanded', 'true');
  const holdings = page.getByRole('row').filter({ hasText: 'Demo broker A' }).filter({ hasText: 'Demo broker B' });
  await expect(holdings).toBeVisible();
  await expect(page.getByText('TWR')).toBeVisible();
  // All-time return states where it starts; one period control drives every return and the chart, and a
  // period's return is time-weighted and labelled so.
  const heroReturn = page.getByTestId('hero-return-label');
  await expect(heroReturn).toContainText(/Return since \d{2}\/\d{4}/);
  await page.locator('[data-period="1Y"]').click();
  await expect(heroReturn).toContainText('Return 1Y (TWR)');
  await expect(page.getByTestId('wallet-return-label').first()).toContainText('Return 1Y (TWR)');
  await page.locator('[data-period="ALL"]').click();
  await expect(heroReturn).toContainText('Return since');
  await page.waitForTimeout(1000);
  if (shots) await page.screenshot({ path: `${shots}/portfolio.png`, fullPage: true });
  // Each account is a wallet card at the top; picking one scopes the whole page to it.
  const walletA = page.getByRole('button', { name: /Demo broker A/ });
  await walletA.click();
  await expect(walletA).toHaveAttribute('aria-pressed', 'true');
  await expect(page.getByText(/Total value\s*· Demo broker A/)).toBeVisible();
  await page.locator('[data-wallet="all"]').click();
  await expect(walletA).toHaveAttribute('aria-pressed', 'false');

  await page.goto('/net-worth');
  await expect(page.getByText('Net worth over time')).toBeVisible();
  await page.waitForTimeout(800);
  if (shots) await page.screenshot({ path: `${shots}/net-worth.png`, fullPage: true });

  // AI access: a client with summary scopes only, used through the real MCP endpoint.
  await page.goto('/ai');
  await page.getByRole('button', { name: /New AI client/ }).click();
  await page.locator('#ai-name').fill('Claude Code');
  await page.getByRole('button', { name: 'Save' }).click();
  const tokenBlock = page.getByRole('dialog').locator('pre').first();
  await expect(tokenBlock).toContainText('pf_');
  const token = (await tokenBlock.innerText()).trim();
  expect(token).toMatch(/^pf_[0-9a-f]{8}_/);
  if (shots) await page.screenshot({ path: `${shots}/ai-token.png` });
  await page.keyboard.press('Escape');

  const mcpAs = (bearer: string) => async (method: string, params: object = {}) => {
    const res = await page.request.post('/mcp', {
      headers: { Authorization: `Bearer ${bearer}`, Accept: 'application/json, text/event-stream', 'Content-Type': 'application/json' },
      data: { jsonrpc: '2.0', id: 1, method, params },
    });
    expect(res.status()).toBe(200);
    const text = await res.text();
    // Streamable HTTP may answer as a single SSE event.
    const json = text.startsWith('{') ? text : text.split('\n').find((l) => l.startsWith('data:'))!.slice(5);
    return JSON.parse(json).result;
  };
  const mcp = mcpAs(token);
  await mcp('initialize', { protocolVersion: '2025-06-18', capabilities: {}, clientInfo: { name: 'e2e', version: '1' } });
  const tools = (await mcp('tools/list')).tools.map((t: { name: string }) => t.name).sort();
  expect(tools).toEqual([
    'get_budget_status',
    'get_expense_summary',
    'get_financial_overview',
    'get_goals',
    'get_income_summary',
    'get_monthly_summary',
    'get_year_breakdown', // overview.read
  ]);
  const call = await mcp('tools/call', { name: 'get_expense_summary', arguments: { category: 'restaurants' } });
  expect(call.content[0].text).toContain('"category":"Restaurants"');
  const denied = await page.request.post('/mcp', {
    headers: { Authorization: 'Bearer pf_00000000_invalid', Accept: 'application/json, text/event-stream' },
    data: { jsonrpc: '2.0', id: 1, method: 'tools/list' },
  });
  expect(denied.status()).toBe(401);

  // AI write access is opt-in per client (ADR-0008): ticking the write scopes asks for confirmation first.
  await page.goto('/ai');
  await page.getByRole('button', { name: /New AI client/ }).click();
  await page.locator('#ai-name').fill('Claude Code (writer)');
  await page.getByRole('button', { name: 'Select all read' }).click();
  await page.getByRole('button', { name: 'Select all write' }).click();
  await page.getByRole('button', { name: 'Allow writing' }).click();
  await expect(page.locator('#ai-writes')).toHaveValue('20');
  if (shots) await page.screenshot({ path: `${shots}/ai-write-scopes.png` });
  await page.getByRole('button', { name: 'Save' }).click();
  const writerBlock = page.getByRole('dialog').locator('pre').first();
  await expect(writerBlock).toContainText('pf_');
  const writer = mcpAs((await writerBlock.innerText()).trim());
  await page.keyboard.press('Escape');

  await writer('initialize', { protocolVersion: '2025-06-18', capabilities: {}, clientInfo: { name: 'e2e-writer', version: '1' } });
  type ListedTool = { name: string; annotations?: { readOnlyHint?: boolean; destructiveHint?: boolean } };
  const writerTools: ListedTool[] = (await writer('tools/list')).tools;
  expect(writerTools.find((t) => t.name === 'get_goals')?.annotations?.readOnlyHint).toBe(true);
  expect(writerTools.find((t) => t.name === 'create_transaction')?.annotations?.readOnlyHint).toBe(false);
  expect(writerTools.find((t) => t.name === 'delete_transaction')?.annotations?.destructiveHint).toBe(true);
  const text = (result: { content: { text: string }[] }) => JSON.parse(result.content[0].text).data;
  const accounts = text(await writer('tools/call', { name: 'get_accounts', arguments: {} })).items as { id?: string }[];
  const accountId = accounts.find((a) => a.id)!.id;
  const created = text(await writer('tools/call', {
    name: 'create_transaction',
    arguments: { type: 'expense', date: `${year}-${String(month).padStart(2, '0')}-01`, amount: 4.2, account_id: accountId, category: 'groceries', description: 'Coffee via AI' },
  }));
  expect(created.summary).toContain('4.20 EUR');
  const removed = await writer('tools/call', { name: 'delete_transaction', arguments: { transaction_id: created.id } });
  expect(removed.isError).toBeFalsy();

  // The deletion waits in the recycle bin; one click restores it.
  await page.goto('/ai');
  const recycled = page.locator('section[aria-labelledby="ai-recycle-title"]').getByRole('row').filter({ hasText: 'Coffee via AI' });
  await expect(recycled).toBeVisible();
  await expect(recycled).toContainText('30 day(s)');
  if (shots) await page.screenshot({ path: `${shots}/ai-recycle-bin.png`, fullPage: true });
  await recycled.getByRole('button', { name: /Restore/ }).click();
  await expect(page.getByText('Restored.')).toBeVisible();
  await expect(recycled).toHaveCount(0);
  await page.goto('/transactions');
  await expect(page.getByText('Coffee via AI').and(page.locator(':visible'))).toBeVisible();

  await page.goto('/ai');
  await expect(page.getByRole('cell', { name: 'get_expense_summary' }).first()).toBeVisible();
  if (shots) await page.screenshot({ path: `${shots}/ai-access.png`, fullPage: true });

  // Language switch is instant and total.
  await page.goto('/dashboard');
  await page.getByRole('group', { name: 'Language' }).getByRole('button', { name: 'PT', exact: true }).click();
  await expect(page.getByText('Visão mensal')).toBeVisible();
  await page.waitForTimeout(800);
  if (shots) await page.screenshot({ path: `${shots}/dashboard-pt.png` });

  expect(cspViolations).toEqual([]);

  // Dark mode.
  await page.getByRole('group', { name: 'Language' }).getByRole('button', { name: 'EN', exact: true }).click();
  await page.goto('/monthly');
  await page.evaluate(() => localStorage.setItem('pf.theme', 'dark'));
  await page.reload();
  await page.waitForTimeout(1000);
  if (shots) await page.screenshot({ path: `${shots}/monthly-dark.png`, fullPage: true });
});
