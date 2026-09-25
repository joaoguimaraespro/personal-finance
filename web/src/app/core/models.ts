export type TransactionType = 'Expense' | 'Income' | 'Transfer' | 'Savings' | 'InvestmentContribution';
export type ExpenseNature = 'Fixed' | 'Variable';
export type AccountKind = 'Bank' | 'Cash' | 'CreditCard' | 'Savings' | 'Broker' | 'Loan' | 'Other';
export type DataSource = 'Manual' | 'Recurring' | 'Xlsx' | 'Csv' | 'Json' | 'Trading212' | 'InteractiveBrokers';
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
    reconciliation: { month: number; measure: string; workbook: number | null; imported: number; matches: boolean }[];
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
