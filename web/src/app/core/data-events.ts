import { Injectable, signal } from '@angular/core';
import { Transaction, TransactionType } from './models';

/** Bumped after every mutation so views re-fetch server-calculated figures. */
@Injectable({ providedIn: 'root' })
export class DataEvents {
  readonly version = signal(0);
  bump() {
    this.version.update((v) => v + 1);
  }
}

/** What a new entry starts with when it answers something pending (e.g. a bucket's allocation this month). */
export interface QuickAddPrefill {
  type: TransactionType;
  amount?: number;
  bucketId?: string | null;
  occurredOn?: string;
  description?: string;
}

/** Opens the global quick-add dialog, optionally to edit an existing transaction or prefilled. */
@Injectable({ providedIn: 'root' })
export class QuickAdd {
  readonly open = signal(false);
  readonly editing = signal<Transaction | null>(null);
  readonly prefill = signal<QuickAddPrefill | null>(null);

  add() {
    this.editing.set(null);
    this.prefill.set(null);
    this.open.set(true);
  }

  addWith(prefill: QuickAddPrefill) {
    this.editing.set(null);
    this.prefill.set(prefill);
    this.open.set(true);
  }

  edit(t: Transaction) {
    this.prefill.set(null);
    this.editing.set(t);
    this.open.set(true);
  }

  close() {
    this.open.set(false);
    this.editing.set(null);
    this.prefill.set(null);
  }
}
