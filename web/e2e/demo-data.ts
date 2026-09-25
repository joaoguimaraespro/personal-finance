import { APIRequestContext, BrowserContext } from '@playwright/test';

/** Fictitious data only — used for the E2E journey and README screenshots. */
export async function seedDemo(request: APIRequestContext, context: BrowserContext, year: number, throughMonth: number) {
  const xsrf = async () => (await context.cookies()).find((c) => c.name === 'XSRF-TOKEN')?.value ?? '';
  const post = async (url: string, data: unknown) => {
    const res = await request.post(url, { data, headers: { 'X-XSRF-TOKEN': await xsrf() } });
    if (!res.ok()) throw new Error(`${url}: ${res.status()} ${await res.text()}`);
    return res.status() === 204 ? null : res.json();
  };
  const put = async (url: string, data: unknown) => {
    const res = await request.put(url, { data, headers: { 'X-XSRF-TOKEN': await xsrf() } });
    if (!res.ok()) throw new Error(`${url}: ${res.status()} ${await res.text()}`);
  };

  const categories: { id: string; key: string }[] = await (await request.get('/api/categories')).json();
  const buckets: { id: string; key: string }[] = await (await request.get('/api/buckets')).json();
  const cat = (key: string) => categories.find((c) => c.key === key)!.id;
  const bucket = (key: string) => buckets.find((b) => b.key === key)!.id;

  const bank = (await post('/api/accounts', { name: 'Main bank', kind: 'Bank', currency: 'EUR', openingBalance: 3200, openingBalanceOn: `${year}-01-01`, institution: 'Demo Bank' })).id;
  const card = (await post('/api/accounts', { name: 'Revolut', kind: 'Bank', currency: 'EUR', openingBalance: 400, openingBalanceOn: `${year}-01-01`, institution: 'Revolut' })).id;
  const savings = (await post('/api/accounts', { name: 'Savings account', kind: 'Savings', currency: 'EUR', openingBalance: 6000, openingBalanceOn: `${year}-01-01` })).id;

  await put(`/api/budgets/${year}-01`, {
    note: 'Same split as the old spreadsheet',
    items: [
      { target: 'Bucket', mode: 'PercentOfIncome', value: 0.25, bucketId: bucket('stocks-etfs') },
      { target: 'Bucket', mode: 'PercentOfIncome', value: 0, bucketId: bucket('crypto') },
      { target: 'Bucket', mode: 'PercentOfIncome', value: 0.05, bucketId: bucket('travel-fund') },
      { target: 'Bucket', mode: 'PercentOfIncome', value: 0.05, bucketId: bucket('other-savings') },
      { target: 'ExpensePool', mode: 'Remainder', value: 0 },
      { target: 'Category', mode: 'FixedAmount', value: 150, categoryId: cat('restaurants') },
      { target: 'Category', mode: 'FixedAmount', value: 380, categoryId: cat('groceries') },
    ],
  });

  const emergency = (await post('/api/goals', { name: 'Emergency fund', targetAmount: 10000, startingAmount: 6000, targetDate: `${year + 1}-06-30` })).id;
  await post('/api/goals', { name: 'Japan trip', targetAmount: 4000, startingAmount: 900, targetDate: `${year + 1}-04-01` });

  // Deterministic "noise" so months differ without randomness.
  const wobble = (m: number, seed: number, spread: number) => Math.round(((Math.sin(m * 12.9898 + seed) + 1) / 2) * spread * 100) / 100;
  const day = (m: number, d: number) => `${year}-${String(m).padStart(2, '0')}-${String(d).padStart(2, '0')}`;
  const tx = (data: Record<string, unknown> & { amount: number }) =>
    post('/api/transactions', { ...data, amount: Math.round(data.amount * 100) / 100 });

  for (let m = 1; m <= throughMonth; m++) {
    const salary = m === 6 || m === 12 ? 4900 : 2450;
    await tx({ type: 'Income', occurredOn: day(m, 25), amount: salary, accountId: bank, categoryId: cat('salary'), description: m === 6 ? 'Salary + holiday bonus' : 'Salary' });
    if (m % 3 === 0) await tx({ type: 'Income', occurredOn: day(m, 15), amount: 320 + wobble(m, 1, 200), accountId: bank, categoryId: cat('freelance'), description: 'Side project' });
    await tx({ type: 'Expense', occurredOn: day(m, 1), amount: 750, accountId: bank, categoryId: cat('housing'), description: 'Rent' });
    await tx({ type: 'Expense', occurredOn: day(m, 8), amount: 38 + wobble(m, 2, 30), accountId: bank, categoryId: cat('electricity'), description: 'Electricity' });
    await tx({ type: 'Expense', occurredOn: day(m, 10), amount: 32.9, accountId: bank, categoryId: cat('internet'), description: 'Fibre' });
    await tx({ type: 'Expense', occurredOn: day(m, 12), amount: 10.99, accountId: card, categoryId: cat('subscriptions'), description: 'Music streaming' });
    await tx({ type: 'Expense', occurredOn: day(m, 3), amount: 30, accountId: bank, categoryId: cat('gym'), description: 'Gym' });
    for (const d of [4, 11, 18, 26]) await tx({ type: 'Expense', occurredOn: day(m, d), amount: 72 + wobble(m, d, 40), accountId: card, categoryId: cat('groceries'), description: 'Supermarket' });
    for (const d of [7, 21]) await tx({ type: 'Expense', occurredOn: day(m, d), amount: 28 + wobble(m, d + 5, 70), accountId: card, categoryId: cat('restaurants'), description: 'Dinner out' });
    await tx({ type: 'Expense', occurredOn: day(m, 14), amount: 45 + wobble(m, 7, 40), accountId: card, categoryId: cat('fuel'), description: 'Fuel' });
    if (m === 7) await tx({ type: 'Expense', occurredOn: day(m, 20), amount: 640, accountId: card, categoryId: cat('travel'), description: 'Summer trip' });
    await tx({ type: 'InvestmentContribution', occurredOn: day(m, 26), amount: Math.round(salary * 0.25), accountId: bank, bucketId: bucket('stocks-etfs'), description: 'Monthly ETF buy' });
    await tx({ type: 'Savings', occurredOn: day(m, 26), amount: 120, accountId: bank, counterAccountId: savings, bucketId: bucket('emergency-fund'), goalId: emergency, description: 'Emergency fund' });
    await tx({ type: 'Savings', occurredOn: day(m, 26), amount: 110, accountId: bank, counterAccountId: savings, bucketId: bucket('travel-fund'), description: 'Travel fund' });
  }

  await post('/api/recurring', { name: 'Music streaming', type: 'Expense', amount: 10.99, accountId: card, frequency: 'Monthly', startOn: day(throughMonth, 1), dayOfMonth: new Date().getDate(), categoryId: cat('subscriptions') });
}
