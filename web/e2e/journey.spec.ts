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

  // First run: the app sends us to owner setup.
  await page.goto('/');
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

  // Language switch is instant and total.
  await page.goto('/dashboard');
  await page.getByLabel('Language').selectOption('pt-PT');
  await expect(page.getByText('Visão mensal')).toBeVisible();
  await page.waitForTimeout(800);
  if (shots) await page.screenshot({ path: `${shots}/dashboard-pt.png` });

  // Dark mode.
  await page.getByLabel('Language').selectOption('en');
  await page.goto('/monthly');
  await page.evaluate(() => localStorage.setItem('pf.theme', 'dark'));
  await page.reload();
  await page.waitForTimeout(1000);
  if (shots) await page.screenshot({ path: `${shots}/monthly-dark.png`, fullPage: true });
});
