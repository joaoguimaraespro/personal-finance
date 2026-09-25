import { Injectable, signal } from '@angular/core';

export interface Toast {
  id: number;
  message: string;
  kind: 'success' | 'error' | 'info';
  action?: { label: string; run: () => void };
}

@Injectable({ providedIn: 'root' })
export class Toasts {
  readonly items = signal<Toast[]>([]);
  private next = 1;

  show(message: string, kind: Toast['kind'] = 'success', action?: Toast['action'], ms = 5000) {
    const toast = { id: this.next++, message, kind, action };
    this.items.update((list) => [...list, toast]);
    setTimeout(() => this.dismiss(toast.id), ms);
  }

  error(err: unknown) {
    this.show(problemMessage(err), 'error', undefined, 8000);
  }

  dismiss(id: number) {
    this.items.update((list) => list.filter((t) => t.id !== id));
  }
}

/** Extracts a readable message from an RFC 7807 problem response. */
export function problemMessage(err: unknown): string {
  const e = err as { error?: { detail?: string; title?: string; errors?: Record<string, string[]> }; message?: string };
  const errors = e?.error?.errors;
  if (errors) {
    const first = Object.values(errors)[0];
    if (first?.length) return first[0];
  }
  return e?.error?.detail ?? e?.error?.title ?? e?.message ?? 'Something went wrong';
}
