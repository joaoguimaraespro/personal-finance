import { Injectable, signal } from '@angular/core';
import { Transaction } from './models';

/** Bumped after every mutation so views re-fetch server-calculated figures. */
@Injectable({ providedIn: 'root' })
export class DataEvents {
  readonly version = signal(0);
  bump() {
    this.version.update((v) => v + 1);
  }
}

/** Opens the global quick-add dialog, optionally to edit an existing transaction. */
@Injectable({ providedIn: 'root' })
export class QuickAdd {
  readonly open = signal(false);
  readonly editing = signal<Transaction | null>(null);

  add() {
    this.editing.set(null);
    this.open.set(true);
  }

  edit(t: Transaction) {
    this.editing.set(t);
    this.open.set(true);
  }

  close() {
    this.open.set(false);
    this.editing.set(null);
  }
}
