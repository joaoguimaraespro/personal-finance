export type TransactionType =
  'Expense' | 'Income' | 'Transfer' | 'Savings' | 'InvestmentContribution' | 'InvestmentSale';
/** Day-to-day money vs investing vs moving money between own accounts — reported separately. */
export type TransactionFlow = 'Everyday' | 'Investment' | 'Movement';
export type InvestmentAssetKind = 'Stock' | 'Etf' | 'Crypto' | 'Fund' | 'Bond' | 'Other';
export type AssetPriceSource = 'Manual' | 'Trading212' | 'InteractiveBrokers' | 'Portfolio';
export type InstrumentProvider = 'Trading212' | 'InteractiveBrokers' | 'Portfolio';

export const FLOW_TYPES: Record<TransactionFlow, TransactionType[]> = {
  Everyday: ['Expense', 'Income'],
  Investment: ['InvestmentContribution', 'InvestmentSale'],
  Movement: ['Transfer', 'Savings'],
};

export function flowOf(type: TransactionType): TransactionFlow {
  if (type === 'Expense' || type === 'Income') return 'Everyday';
  if (type === 'InvestmentContribution' || type === 'InvestmentSale') return 'Investment';
  return 'Movement';
}

export interface InvestmentAsset {
  kind: InvestmentAssetKind;
  symbol: string;
  name: string | null;
  isin: string | null;
  quantity: number | null;
  unitPrice: number | null;
  priceSource: AssetPriceSource | null;
}

export interface InstrumentMatch {
  provider: InstrumentProvider;
  brokerSymbol: string;
  symbol: string;
  name: string;
  isin: string | null;
  currency: string;
  assetClass: AssetClass;
  exchange: string | null;
}

export interface ProviderState {
  provider: InstrumentProvider;
  configured: boolean;
  message: string | null;
}

export interface InstrumentSearchResult {
  items: InstrumentMatch[];
  providers: ProviderState[];
}

export interface InstrumentQuote {
  provider: InstrumentProvider;
  date: string;
  price: number;
  currency: string;
  basis: 'trade' | 'close' | 'current';
}

export interface InstrumentQuoteResult {
  quote: InstrumentQuote | null;
  message: string | null;
}
export type ExpenseNature = 'Fixed' | 'Variable';
export type AccountKind = 'Bank' | 'Cash' | 'CreditCard' | 'Savings' | 'Broker' | 'Loan' | 'Other';
export type DataSource =
  | 'Manual'
  | 'Recurring'
  | 'Xlsx'
  | 'Csv'
  | 'Json'
  | 'Trading212'
  | 'InteractiveBrokers'
  | 'InterestEstimate';
export type InterestPayout = 'Monthly' | 'Daily';
export type BucketGroup = 'Investment' | 'Savings';
export type AllocationStatus = 'Todo' | 'Done' | 'Partial' | 'NotApplicable';
export type BudgetTarget = 'Bucket' | 'ExpensePool' | 'Category';
export type BudgetMode = 'PercentOfIncome' | 'FixedAmount' | 'Remainder';
export type Frequency = 'Weekly' | 'Monthly' | 'Yearly';

export interface Account {
  id: string;
  name: string;
  kind: AccountKind;
  currency: string;
  institution: string | null;
  identifierMasked: string | null;
  openingBalance: number;
  openingBalanceOn: string;
  balance: number;
  isManual: boolean;
  isLiability: boolean;
  archived: boolean;
  /** Only on savings and bank accounts. */
  interest?: AccountInterest | null;
}

/** Interest picture of a savings/bank account; all amounts are server-calculated. */
export interface AccountInterest {
  annualRatePercent: number | null;
  withholdingPercent: number | null;
  rateEffectiveFrom: string | null;
  payout: InterestPayout;
  yearToDate: number;
  yearToDateEstimated: number;
  /** Part of the balance that is still an estimate. */
  estimatedInBalance: number;
}

export interface InterestRate {
  id: string;
  effectiveFrom: string;
  annualRatePercent: number;
  withholdingPercent: number;
}

export type InterestMonthStatus = 'Estimated' | 'Confirmed' | 'Corrected';

export interface InterestMonth {
  id: string;
  accountId: string;
  accountName: string;
  institution: string | null;
  /** yyyy-MM */
  month: string;
  currency: string;
  estimatedAmount: number;
  estimatedGross: number;
  status: InterestMonthStatus;
  actualAmount: number | null;
}

export interface Category {
  id: string;
  key: string;
  name: string;
  type: 'Expense' | 'Income';
  defaultNature: ExpenseNature | null;
  parentId: string | null;
  isSystem: boolean;
  color: string | null;
  icon: string | null;
  archived: boolean;
}

export interface Bucket {
  id: string;
  key: string;
  name: string;
  group: BucketGroup;
  isSystem: boolean;
  archived: boolean;
}

export interface Transaction {
  id: string;
  type: TransactionType;
  occurredOn: string;
  amount: number;
  currency: string;
  fxRate: number;
  baseAmount: number;
  accountId: string;
  accountName: string;
  counterAccountId: string | null;
  counterAccountName: string | null;
  categoryId: string | null;
  categoryKey: string | null;
  categoryName: string | null;
  nature: ExpenseNature | null;
  bucketId: string | null;
  bucketName: string | null;
  goalId: string | null;
  description: string | null;
  notes: string | null;
  source: DataSource;
  editable: boolean;
  createdAtUtc: string;
  updatedAtUtc: string;
  flow: TransactionFlow;
  asset: InvestmentAsset | null;
}

export interface TransactionRequest {
  type: TransactionType;
  occurredOn: string;
  amount: number;
  currency?: string | null;
  accountId: string;
  categoryId?: string | null;
  nature?: ExpenseNature | null;
  counterAccountId?: string | null;
  bucketId?: string | null;
  goalId?: string | null;
  fxRate?: number | null;
  description?: string | null;
  notes?: string | null;
  asset?: InvestmentAsset | null;
}

export interface Page<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

export interface QuickAddDefaults {
  accountId: string | null;
  recentExpenseCategories: string[];
  recentIncomeCategories: string[];
}

export interface AuditEntry {
  action: 'Created' | 'Updated' | 'Deleted' | 'Restored';
  atUtc: string;
  actor: string;
  changes: string;
}

export interface BucketLine {
  bucketId: string;
  name: string;
  isInvestment: boolean;
  target: number | null;
  actual: number;
  difference: number | null;
}

export interface MonthlySummary {
  period: { year: number; month: number };
  income: number;
  fixedExpenses: number;
  variableExpenses: number;
  totalExpenses: number;
  invested: number;
  saved: number;
  netBalance: number;
  freeCashFlow: number;
  savingsRate: number | null;
  investmentRate: number | null;
  savingsOnlyRate: number | null;
  expenseBudget: number | null;
  expenseBudgetBalance: number | null;
  investmentTarget: number | null;
  savingsTarget: number | null;
  unallocated: number | null;
  status: 'Positive' | 'Negative';
  buckets: BucketLine[];
  transactionCount: number;
}

export interface MonthlyAverage {
  months: number;
  income: number;
  totalExpenses: number;
  fixedExpenses: number;
  variableExpenses: number;
  invested: number;
  saved: number;
  netBalance: number;
  savingsRate: number | null;
}

export interface MonthlyComparison {
  current: MonthlySummary;
  previous: MonthlySummary;
  trailingAverage: MonthlyAverage;
}

export interface AnnualRow {
  month: MonthlySummary;
  cumulativeInvested: number;
  cumulativeSaved: number;
  hasIncome: boolean;
}

export interface AnnualSummary {
  year: number;
  months: AnnualRow[];
  totals: {
    income: number;
    expenseBudget: number;
    fixedExpenses: number;
    variableExpenses: number;
    totalExpenses: number;
    invested: number;
    saved: number;
    netBalance: number;
    freeCashFlow: number;
    averageMonthlySavingsRate: number | null;
    weightedSavingsRate: number | null;
    weightedInvestmentRate: number | null;
    weightedSavingsOnlyRate: number | null;
  };
}

export interface Overview {
  year: number;
  income: number;
  totalExpenses: number;
  invested: number;
  saved: number;
  netBalance: number;
  averageMonthlySavingsRate: number | null;
  weightedSavingsRate: number | null;
  pendingExpected: number;
  /** Day-to-day money only; never includes investments. */
  everyday: {
    income: number;
    expenses: number;
    fixedExpenses: number;
    variableExpenses: number;
    netBalance: number;
  };
  investments: {
    purchases: number;
    sales: number;
    netInvested: number;
    investmentRate: number | null;
  };
}

export interface CategoryLine {
  categoryId: string;
  key: string;
  name: string;
  parentId: string | null;
  color: string | null;
  nature: ExpenseNature | null;
  actual: number;
  budget: number | null;
  variance: number | null;
  status: 'none' | 'under' | 'near' | 'over';
  previousMonth: number;
  trailingAverage: number;
}

export interface TrendPoint {
  period: string;
  income: number;
  expenses: number;
  invested: number;
  saved: number;
  netBalance: number;
  savingsRate: number | null;
}

export interface BudgetItem {
  target: BudgetTarget;
  mode: BudgetMode;
  value: number;
  bucketId: string | null;
  categoryId: string | null;
}

export interface Budget {
  id: string;
  effectiveFrom: string;
  note: string | null;
  items: BudgetItem[];
}

export interface Goal {
  id: string;
  name: string;
  targetAmount: number;
  targetDate: string | null;
  startingAmount: number;
  manualCurrentAmount: number | null;
  currentAmount: number;
  progress: number;
  remaining: number;
  monthlyNeeded: number | null;
  icon: string | null;
  achieved: boolean;
  archived: boolean;
}

export interface Recurring {
  id: string;
  name: string;
  type: TransactionType;
  amount: number;
  currency: string;
  accountId: string;
  counterAccountId: string | null;
  categoryId: string | null;
  nature: ExpenseNature | null;
  bucketId: string | null;
  description: string | null;
  frequency: Frequency;
  interval: number;
  dayOfMonth: number | null;
  startOn: string;
  endOn: string | null;
  nextDueOn: string;
  isActive: boolean;
}

export interface Expected {
  id: string;
  recurringTransactionId: string;
  name: string;
  type: TransactionType;
  dueOn: string;
  amount: number;
  currency: string;
  status: 'Pending' | 'Confirmed' | 'Skipped';
  transactionId: string | null;
}

export interface AllocationCheck {
  bucketId: string;
  status: AllocationStatus;
}

export interface Me {
  authenticated: boolean;
  setupRequired: boolean;
  email: string | null;
  mfaEnabled: boolean;
  mfaSatisfied: boolean;
}

export interface ImportPreview {
  id: string;
  status: 'Previewed' | 'Committed' | 'RolledBack' | 'Failed';
  fileName: string;
  workbookCategories: { label: string; categoryId: string }[];
  preview: {
    mapping: {
      year: number;
      mainAccountId: string | null;
      investmentAccountId: string | null;
      savingsAccountId: string | null;
      categories: Record<string, string>;
      importBudget: boolean;
    };
    transactions: {
      externalId: string;
      month: number;
      type: TransactionType;
      occurredOn: string;
      amount: number;
      categoryId: string | null;
      nature: ExpenseNature | null;
      bucketId: string | null;
      description: string | null;
    }[];
    checks: { month: number; bucketId: string; status: AllocationStatus }[];
    budget: { stocks: number; crypto: number; travel: number; otherSavings: number } | null;
    reconciliation: {
      month: number;
      measure: string;
      workbook: number | null;
      imported: number;
      matches: boolean;
    }[];
    unmappedCategories: string[];
    errors: string[];
    warnings: string[];
    canCommit: boolean;
    reconciled: boolean;
  };
}

export interface ImportSummary {
  id: string;
  kind: string;
  fileName: string;
  status: ImportPreview['status'];
  created: number;
  skipped: number;
  createdAtUtc: string;
  committedAtUtc: string | null;
  rolledBackAtUtc: string | null;
}

// ---- Investments

export type AssetClass = 'Stock' | 'Etf' | 'Bond' | 'Fund' | 'Crypto' | 'Cash' | 'Other';
/** 'Manual' = coins entered by hand (an exchange account or wallet), priced from public quotes. */
export type Broker = 'Trading212' | 'InteractiveBrokers' | 'Demo' | 'Manual';

export interface CoinMatch {
  id: string;
  symbol: string;
  name: string;
}

export type RewardKind = 'Staking' | 'Earn' | 'Airdrop' | 'Other';

export interface HoldingReward {
  id: string;
  receivedOn: string;
  quantity: number;
  kind: RewardKind;
  note: string | null;
  /** EUR value on the day received (counted as income). */
  valueBase: number | null;
}

export interface ManualHolding {
  id: string;
  accountId: string;
  location: string;
  securityId: string;
  coinId: string;
  symbol: string;
  name: string;
  quantity: number;
  averagePrice: number;
  heldSince: string;
  notes: string | null;
  rewardQuantity: number;
  rewards: HoldingReward[];
}

export interface ManualHoldingsView {
  holdings: ManualHolding[];
  locations: string[];
  pricesEnabled: boolean;
}

export interface PortfolioSummary {
  totalValue: number;
  marketValue: number;
  cash: number;
  netContributions: number;
  totalReturn: number;
  totalReturnPercent: number | null;
  realizedPnl: number;
  unrealizedPnl: number;
  dividends: number;
  fees: number;
  positions: number;
  lastSyncUtc: string | null;
  accounts: AccountTotal[];
  dayChange: number | null;
  dayChangePercent: number | null;
}

/** One account of the portfolio (a broker account or a crypto wallet), as its own scoped summary would show it. */
export interface AccountTotal {
  accountId: string;
  name: string;
  broker: Broker;
  marketValue: number;
  cash: number;
  netContributions: number;
  totalReturn: number;
  totalReturnPercent: number | null;
  dayChange: number | null;
  dayChangePercent: number | null;
  positions: number;
}

export interface PositionLine {
  securityId: string;
  symbol: string;
  isin: string | null;
  name: string;
  currency: string;
  assetClass: AssetClass;
  quantity: number;
  averagePrice: number;
  lastPrice: number;
  marketValueBase: number;
  costBase: number;
  unrealizedPnlBase: number;
  unrealizedPnlPercent: number | null;
  portfolioWeight: number;
  holdings: {
    accountId: string;
    accountName: string;
    broker: Broker;
    quantity: number;
    averagePrice: number;
  }[];
  /** Change since the previous recorded close (EUR); null until prices from an earlier day exist. */
  dayChangeBase: number | null;
  dayChangePercent: number | null;
}

export interface AllocationLine {
  assetClass: AssetClass;
  value: number;
  actual: number;
  target: number | null;
  difference: number | null;
}

export interface DividendSummary {
  totalNetBase: number;
  byMonth: { period: string; amount: number }[];
  bySecurity: { symbol: string; name: string; amount: number }[];
  items: {
    paidOn: string;
    symbol: string;
    name: string;
    broker: Broker;
    gross: number;
    withholdingTax: number;
    net: number;
    currency: string;
    netBase: number;
    withholdingDerived: boolean;
  }[];
}

export interface PerformanceReport {
  from: string;
  to: string;
  timeWeightedReturn: number | null;
  moneyWeightedReturn: number | null;
  startValue: number;
  endValue: number;
  netFlows: number;
  gain: number;
  series: { date: string; value: number; netContributions: number }[];
  /** History before this day was reconstructed from transactions and public closing prices. */
  reconstructedBefore: string | null;
  /** Reconstructed days on which a holding was valued at a trade price instead of a closing price. */
  estimatedDays: number;
}

export type ManualAssetKind =
  'RealEstate' | 'Vehicle' | 'Crypto' | 'Pension' | 'Other' | 'Loan' | 'Mortgage' | 'OtherDebt';

export interface ManualAsset {
  id: string;
  name: string;
  kind: ManualAssetKind;
  currency: string;
  isLiability: boolean;
  currentValue: number | null;
  valuedOn: string | null;
  history: { date: string; value: number }[];
}

export interface NetWorthHistory {
  current: {
    date: string;
    assets: number;
    liabilities: number;
    netWorth: number;
    cash: number;
    investments: number;
    manualAssets: number;
    lines: {
      group: 'cash' | 'investments' | 'manual' | 'liability';
      name: string;
      value: number;
    }[];
  };
  changeSinceStart: number | null;
  changeSinceStartPercent: number | null;
  startDate: string | null;
  series: { date: string; assets: number; liabilities: number; netWorth: number }[];
}

// ---- Integrations

export interface ProviderInfo {
  kind: Broker;
  name: string;
  fields: { key: string; label: string; secret: boolean; required: boolean; hint: string | null }[];
  setupHint: string;
}

export interface SyncJob {
  id: string;
  trigger: 'Scheduled' | 'Manual' | 'CsvImport';
  outcome: 'Running' | 'Succeeded' | 'PartiallySucceeded' | 'Failed';
  startedAtUtc: string;
  finishedAtUtc: string | null;
  imported: number;
  updated: number;
  ignored: number;
  errors: string[];
}

export interface Connection {
  id: string;
  kind: Broker;
  displayName: string;
  accountId: string;
  status: 'Active' | 'NeedsAttention' | 'Disabled';
  lastError: string | null;
  lastSuccessfulSyncUtc: string | null;
  credentialsExpireOn: string | null;
  lastJob: SyncJob | null;
}
