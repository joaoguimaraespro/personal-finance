import { Injectable } from '@angular/core';
import { toast } from '@spartan-ng/brain/sonner';

export interface ToastAction {
  label: string;
  run: () => void;
}

/** App-wide notifications, rendered by <hlm-toaster> in the root component. */
@Injectable({ providedIn: 'root' })
export class Toasts {
  show(
    message: string,
    kind: 'success' | 'error' | 'info' = 'success',
    action?: ToastAction,
    ms = 5000,
  ) {
    const options = {
      duration: ms,
      action: action ? { label: action.label, onClick: () => action.run() } : undefined,
    };
    if (kind === 'error') toast.error(message, options);
    else if (kind === 'info') toast.info(message, options);
    else toast.success(message, options);
  }

  error(err: unknown) {
    this.show(problemMessage(err), 'error', undefined, 8000);
  }
}

/** Extracts a readable message from an RFC 7807 problem response. */
export function problemMessage(err: unknown): string {
  const e = err as {
    error?: { detail?: string; title?: string; errors?: Record<string, string[]> };
    message?: string;
  };
  const errors = e?.error?.errors;
  if (errors) {
    const first = Object.values(errors)[0];
    if (first?.length) return first[0];
  }
  return e?.error?.detail ?? e?.error?.title ?? e?.message ?? 'Something went wrong';
}
