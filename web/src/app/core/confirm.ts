import { Injectable, signal } from '@angular/core';

export interface ConfirmRequest {
  message: string;
  confirmLabel?: string;
  cancelLabel?: string;
  destructive?: boolean;
  resolve: (ok: boolean) => void;
}

/** Promise-based confirmation, rendered by <app-confirm-dialog> in the root component. */
@Injectable({ providedIn: 'root' })
export class Confirm {
  readonly pending = signal<ConfirmRequest | null>(null);

  ask(
    message: string,
    options: Omit<ConfirmRequest, 'message' | 'resolve'> = {},
  ): Promise<boolean> {
    this.pending()?.resolve(false);
    return new Promise((resolve) => this.pending.set({ message, ...options, resolve }));
  }

  answer(ok: boolean) {
    const req = this.pending();
    this.pending.set(null);
    req?.resolve(ok);
  }
}
