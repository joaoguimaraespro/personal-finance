import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  Account,
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
  Goal,
  ImportPreview,
  ImportSummary,
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

  // Categories & buckets
  categories = (includeArchived = false) =>
    this.http.get<Category[]>('/api/categories', { params: { includeArchived } });
  createCategory = (body: unknown) => this.http.post<{ id: string }>('/api/categories', body);
  updateCategory = (id: string, body: unknown) => this.http.put<void>(`/api/categories/${id}`, body);
  archiveCategory = (id: string) => this.http.post<void>(`/api/categories/${id}/archive`, {});
  restoreCategory = (id: string) => this.http.post<void>(`/api/categories/${id}/restore`, {});
  buckets = () => this.http.get<Bucket[]>('/api/buckets');
  createBucket = (body: unknown) => this.http.post<{ id: string }>('/api/buckets', body);
  allocationChecks = (period: string) => this.http.get<AllocationCheck[]>(`/api/allocation-checks/${period}`);
  setAllocationCheck = (period: string, bucketId: string, status: AllocationStatus) =>
    this.http.put<void>(`/api/allocation-checks/${period}`, { bucketId, status });

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
  createTransaction = (body: TransactionRequest) => this.http.post<{ id: string }>('/api/transactions', body);
  updateTransaction = (id: string, body: TransactionRequest) => this.http.put<void>(`/api/transactions/${id}`, body);
  deleteTransaction = (id: string) => this.http.delete<void>(`/api/transactions/${id}`);
  restoreTransaction = (id: string) => this.http.post<void>(`/api/transactions/${id}/restore`, {});
  transactionHistory = (id: string) => this.http.get<AuditEntry[]>(`/api/transactions/${id}/history`);

  // Recurring
  recurring = () => this.http.get<Recurring[]>('/api/recurring');
  createRecurring = (body: unknown) => this.http.post<{ id: string }>('/api/recurring', body);
  updateRecurring = (id: string, body: unknown) => this.http.put<void>(`/api/recurring/${id}`, body);
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
  categoryBreakdown = (period: string) => this.http.get<CategoryLine[]>(`/api/reports/categories/${period}`);
  trends = (months = 12, to?: string) =>
    this.http.get<TrendPoint[]>('/api/reports/trends', { params: to ? { months, to } : { months } });

  // Imports
  imports = () => this.http.get<ImportSummary[]>('/api/imports');
  analyzeWorkbook(file: File, year: number) {
    const form = new FormData();
    form.append('file', file);
    return this.http.post<ImportPreview>('/api/imports/finance-tracker', form, { params: { year } });
  }
  importPreview = (id: string) => this.http.get<ImportPreview>(`/api/imports/${id}`);
  mapImport = (id: string, body: unknown) => this.http.put<ImportPreview>(`/api/imports/${id}/mapping`, body);
  commitImport = (id: string) =>
    this.http.post<{ created: number; skipped: number; budgetCreated: boolean; checks: number }>(
      `/api/imports/${id}/commit`,
      {},
    );
  undoImport = (id: string) => this.http.post<{ removed: number }>(`/api/imports/${id}/undo`, {});
}
