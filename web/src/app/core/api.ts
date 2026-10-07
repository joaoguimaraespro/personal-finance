import { HttpClient, HttpContext, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { BACKGROUND } from './activity';
import {
  Account,
  AllocationLine,
  AssetClass,
  Broker,
  Connection,
  DividendSummary,
  CoinMatch,
  ManualAsset,
  ManualHoldingsView,
  NetWorthHistory,
  PerformanceReport,
  PortfolioSummary,
  ReturnPeriod,
  PositionLine,
  ProviderInfo,
  SyncJob,
  AllocationCheck,
  AllocationStatus,
  AnnualSummary,
  AuditEntry,
  Bucket,
  Budget,
  BudgetItem,
  Category,
  CategoryLine,
  Expected,
  InterestMonth,
  InterestRate,
  Goal,
  ImportPreview,
  ImportSummary,
  InstrumentProvider,
  InstrumentQuoteResult,
  InstrumentSearchResult,
  MonthlyComparison,
  Overview,
  Page,
  QuickAddDefaults,
  Recurring,
  Transaction,
  TransactionRequest,
  TrendPoint,
} from './models';

export interface TransactionFilter {
  from?: string;
  to?: string;
  type?: string[];
  flow?: string;
  accountId?: string;
  categoryId?: string;
  nature?: string;
  source?: string;
  search?: string;
  page?: number;
  pageSize?: number;
}

/** Thin typed client over /api. All calculations happen on the server; the UI only displays them. */
@Injectable({ providedIn: 'root' })
export class Api {
  private readonly http = inject(HttpClient);

  // Accounts
  accounts = (includeArchived = false) =>
    this.http.get<Account[]>('/api/accounts', { params: { includeArchived } });
  createAccount = (body: unknown) => this.http.post<{ id: string }>('/api/accounts', body);
  updateAccount = (id: string, body: unknown) => this.http.put<void>(`/api/accounts/${id}`, body);
  archiveAccount = (id: string) => this.http.post<void>(`/api/accounts/${id}/archive`, {});
  restoreAccount = (id: string) => this.http.post<void>(`/api/accounts/${id}/restore`, {});

  // Interest (TANB) on savings accounts
  interestRates = (accountId: string) =>
    this.http.get<InterestRate[]>(`/api/accounts/${accountId}/interest-rates`);
  addInterestRate = (
    accountId: string,
    body: { annualRatePercent: number; effectiveFrom: string; withholdingPercent: number | null },
  ) => this.http.post<{ id: string }>(`/api/accounts/${accountId}/interest-rates`, body);
  deleteInterestRate = (accountId: string, id: string) =>
    this.http.delete<void>(`/api/accounts/${accountId}/interest-rates/${id}`);
  interestPending = () => this.http.get<InterestMonth[]>('/api/interest/pending');
  /** Without an amount the estimate is confirmed; with one it is replaced (0 = nothing paid). */
  reconcileInterest = (id: string, body: { amount?: number } = {}) =>
    this.http.post<{ transactionId: string | null }>(`/api/interest/${id}/reconcile`, body);

  // Categories & buckets
  categories = (includeArchived = false, context?: HttpContext) =>
    this.http.get<Category[]>('/api/categories', { params: { includeArchived }, context });
  createCategory = (body: unknown) => this.http.post<{ id: string }>('/api/categories', body);
  updateCategory = (id: string, body: unknown) =>
    this.http.put<void>(`/api/categories/${id}`, body);
  archiveCategory = (id: string) => this.http.post<void>(`/api/categories/${id}/archive`, {});
  restoreCategory = (id: string) => this.http.post<void>(`/api/categories/${id}/restore`, {});
  resetCategoryName = (id: string) => this.http.post<void>(`/api/categories/${id}/reset-name`, {});
  buckets = () => this.http.get<Bucket[]>('/api/buckets');
  createBucket = (body: unknown) => this.http.post<{ id: string }>('/api/buckets', body);
  allocationChecks = (period: string) =>
    this.http.get<AllocationCheck[]>(`/api/allocation-checks/${period}`);
  setAllocationCheck = (period: string, bucketId: string, status: AllocationStatus) =>
    this.http.put<void>(`/api/allocation-checks/${period}`, { bucketId, status });
  /** Back to automatic: the status follows what was set aside against the target. */
  clearAllocationCheck = (period: string, bucketId: string) =>
    this.http.delete<void>(`/api/allocation-checks/${period}/${bucketId}`);

  // Transactions
  transactions(filter: TransactionFilter): Observable<Page<Transaction>> {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(filter)) {
      if (value === undefined || value === null || value === '') continue;
      if (Array.isArray(value)) value.forEach((v) => (params = params.append(key, v)));
      else params = params.set(key, String(value));
    }
    return this.http.get<Page<Transaction>>('/api/transactions', { params });
  }
  transaction = (id: string) => this.http.get<Transaction>(`/api/transactions/${id}`);
  quickAddDefaults = () => this.http.get<QuickAddDefaults>('/api/transactions/defaults');
  createTransaction = (body: TransactionRequest) =>
    this.http.post<{ id: string }>('/api/transactions', body);
  updateTransaction = (id: string, body: TransactionRequest) =>
    this.http.put<void>(`/api/transactions/${id}`, body);
  deleteTransaction = (id: string) => this.http.delete<void>(`/api/transactions/${id}`);
  restoreTransaction = (id: string) => this.http.post<void>(`/api/transactions/${id}/restore`, {});
  transactionHistory = (id: string) =>
    this.http.get<AuditEntry[]>(`/api/transactions/${id}/history`);

  // Instrument lookup for the add-transaction form (stocks/ETFs only; crypto is always manual)
  searchInstruments = (q: string) =>
    this.http.get<InstrumentSearchResult>('/api/instruments/search', { params: { q, limit: 10 } });
  instrumentQuote = (
    provider: InstrumentProvider,
    symbol: string,
    date: string,
    isin?: string | null,
  ) =>
    this.http.get<InstrumentQuoteResult>('/api/instruments/quote', {
      params: isin ? { provider, symbol, date, isin } : { provider, symbol, date },
    });

  // Recurring
  recurring = () => this.http.get<Recurring[]>('/api/recurring');
  createRecurring = (body: unknown) => this.http.post<{ id: string }>('/api/recurring', body);
  updateRecurring = (id: string, body: unknown) =>
    this.http.put<void>(`/api/recurring/${id}`, body);
  pauseRecurring = (id: string) => this.http.post<void>(`/api/recurring/${id}/pause`, {});
  resumeRecurring = (id: string) => this.http.post<void>(`/api/recurring/${id}/resume`, {});
  deleteRecurring = (id: string) => this.http.delete<void>(`/api/recurring/${id}`);
  expected = () => this.http.get<Expected[]>('/api/expected');
  confirmExpected = (id: string, body: { amount?: number; occurredOn?: string } = {}) =>
    this.http.post<{ transactionId: string }>(`/api/expected/${id}/confirm`, body);
  skipExpected = (id: string) => this.http.post<void>(`/api/expected/${id}/skip`, {});

  // Budgets & goals
  budgets = () => this.http.get<Budget[]>('/api/budgets');
  saveBudget = (period: string, body: { note: string | null; items: BudgetItem[] }) =>
    this.http.put<void>(`/api/budgets/${period}`, body);
  deleteBudget = (period: string) => this.http.delete<void>(`/api/budgets/${period}`);
  goals = () => this.http.get<Goal[]>('/api/goals');
  createGoal = (body: unknown) => this.http.post<{ id: string }>('/api/goals', body);
  updateGoal = (id: string, body: unknown) => this.http.put<void>(`/api/goals/${id}`, body);
  archiveGoal = (id: string) => this.http.post<void>(`/api/goals/${id}/archive`, {});

  // Reports
  overview = (year: number) => this.http.get<Overview>(`/api/reports/overview/${year}`);
  monthly = (period: string) => this.http.get<MonthlyComparison>(`/api/reports/monthly/${period}`);
  annual = (year: number) => this.http.get<AnnualSummary>(`/api/reports/annual/${year}`);
  categoryBreakdown = (period: string) =>
    this.http.get<CategoryLine[]>(`/api/reports/categories/${period}`);
  trends = (months = 12, to?: string) =>
    this.http.get<TrendPoint[]>('/api/reports/trends', {
      params: to ? { months, to } : { months },
    });

  // Imports
  imports = () => this.http.get<ImportSummary[]>('/api/imports');
  analyzeWorkbook(file: File, year: number) {
    const form = new FormData();
    form.append('file', file);
    return this.http.post<ImportPreview>('/api/imports/finance-tracker', form, {
      params: { year },
    });
  }
  importPreview = (id: string) => this.http.get<ImportPreview>(`/api/imports/${id}`);
  mapImport = (id: string, body: unknown) =>
    this.http.put<ImportPreview>(`/api/imports/${id}/mapping`, body);
  commitImport = (id: string) =>
    this.http.post<{ created: number; skipped: number; budgetCreated: boolean; checks: number }>(
      `/api/imports/${id}/commit`,
      {},
    );
  undoImport = (id: string) => this.http.post<{ removed: number }>(`/api/imports/${id}/undo`, {});

  // Portfolio (read-only broker data)
  /** `period` sets what `periodReturn` covers (ALL by default: total return since the first deposit). */
  portfolioSummary = (scope: PortfolioScope = {}, period: ReturnPeriod = 'ALL') =>
    this.http.get<PortfolioSummary>('/api/portfolio/summary', {
      params: period === 'ALL' ? scopeParams(scope) : { ...scopeParams(scope), period },
    });
  positions = (scope: PortfolioScope = {}) =>
    this.http.get<PositionLine[]>('/api/portfolio/positions', { params: scopeParams(scope) });
  allocation = (scope: PortfolioScope = {}) =>
    this.http.get<AllocationLine[]>('/api/portfolio/allocation', { params: scopeParams(scope) });
  dividends = (scope: PortfolioScope = {}) =>
    this.http.get<DividendSummary>('/api/portfolio/dividends', { params: scopeParams(scope) });
  /** The value chart and its returns over a period, measured as the summary's period return (up to the live value). */
  performance = (scope: PortfolioScope = {}, period: ReturnPeriod = 'ALL') =>
    this.http.get<PerformanceReport>('/api/portfolio/performance', {
      params: { ...scopeParams(scope), period },
    });
  targets = () =>
    this.http.get<{ assetClass: AssetClass; percent: number }[]>('/api/portfolio/targets');
  saveTargets = (items: { assetClass: AssetClass; percent: number }[]) =>
    this.http.put<void>('/api/portfolio/targets', items);
  overrideAssetClass = (securityId: string, assetClass: AssetClass | null) =>
    this.http.put<void>(`/api/portfolio/securities/${securityId}/asset-class`, null, {
      params: assetClass ? { assetClass } : {},
    });

  // Coins entered by hand (no exchange or wallet connection)
  manualHoldings = () => this.http.get<ManualHoldingsView>('/api/portfolio/manual');
  searchCoins = (q: string) =>
    this.http.get<CoinMatch[]>('/api/portfolio/manual/coins', { params: { q } });
  createManualHolding = (body: unknown) =>
    this.http.post<{ id: string }>('/api/portfolio/manual', body);
  updateManualHolding = (id: string, body: unknown) =>
    this.http.put<void>(`/api/portfolio/manual/${id}`, body);
  deleteManualHolding = (id: string) => this.http.delete<void>(`/api/portfolio/manual/${id}`);
  addReward = (id: string, body: unknown) =>
    this.http.post<{ id: string }>(`/api/portfolio/manual/${id}/rewards`, body);
  deleteReward = (id: string, rewardId: string) =>
    this.http.delete<void>(`/api/portfolio/manual/${id}/rewards/${rewardId}`);
  /** Fetches coin prices older than 15 minutes; `updated` says whether anything changed. */
  refreshCoinPrices = () =>
    this.http.post<{ updated: boolean }>(
      '/api/portfolio/manual/refresh-prices',
      {},
      { context: new HttpContext().set(BACKGROUND, true) },
    );

  // Net worth
  netWorth = () => this.http.get<NetWorthHistory>('/api/net-worth');
  manualAssets = () => this.http.get<ManualAsset[]>('/api/assets');
  createManualAsset = (body: unknown) => this.http.post<{ id: string }>('/api/assets', body);
  valueManualAsset = (id: string, value: number, on?: string) =>
    this.http.post<void>(`/api/assets/${id}/valuations`, { value, on });
  archiveManualAsset = (id: string) => this.http.post<void>(`/api/assets/${id}/archive`, {});
  fx = (currency: string, date?: string) =>
    this.http.get<{ currency: string; date: string; eurPerUnit: number }>(`/api/fx/${currency}`, {
      params: date ? { date } : {},
    });

  // Integrations
  providers = () => this.http.get<ProviderInfo[]>('/api/integrations/providers');
  /** `background` skips the global loading bar (used while polling a running sync). */
  connections = (background = false) =>
    this.http.get<Connection[]>('/api/integrations/connections', {
      context: new HttpContext().set(BACKGROUND, background),
    });
  createConnection = (body: unknown) =>
    this.http.post<{ id: string }>('/api/integrations/connections', body);
  updateCredentials = (id: string, body: unknown) =>
    this.http.put<void>(`/api/integrations/connections/${id}/credentials`, body);
  syncConnection = (id: string) =>
    this.http.post<void>(`/api/integrations/connections/${id}/sync`, {});
  setConnectionEnabled = (id: string, enabled: boolean) =>
    this.http.post<void>(
      `/api/integrations/connections/${id}/${enabled ? 'enable' : 'disable'}`,
      {},
    );
  deleteConnection = (id: string, purge: boolean) =>
    this.http.delete<void>(`/api/integrations/connections/${id}`, { params: { purge } });
  syncJobs = (id: string) => this.http.get<SyncJob[]>(`/api/integrations/connections/${id}/jobs`);
  importBrokerCsv(id: string, file: File) {
    const form = new FormData();
    form.append('file', file);
    return this.http.post<SyncJob>(`/api/integrations/connections/${id}/csv`, form);
  }
}

export interface PortfolioScope {
  broker?: Broker;
  accountId?: string;
}

function scopeParams(scope: PortfolioScope): Record<string, string> {
  const params: Record<string, string> = {};
  if (scope.broker) params['broker'] = scope.broker;
  if (scope.accountId) params['accountId'] = scope.accountId;
  return params;
}
