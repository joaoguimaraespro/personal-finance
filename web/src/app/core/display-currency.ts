import { HttpClient, HttpContext } from '@angular/common/http';
import { Injectable, effect, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { BACKGROUND } from './activity';
import { Prefs } from './prefs';

export type DisplayCurrency = 'EUR' | 'USD';

/**
 * The currency figures are shown in. Everything is calculated and stored in EUR; showing USD multiplies EUR amounts
 * by today's ECB rate (history included — it is a view, not a revaluation). Module-level so the money pipe and the
 * chart formatters (plain functions) read the same state.
 */
export const displayFx = signal<{ currency: DisplayCurrency; perEur: number }>({
  currency: 'EUR',
  perEur: 1,
});

/** An EUR amount in the display currency, with its code. Other currencies pass through unchanged. */
export function inDisplay(value: number, currency = 'EUR'): { value: number; currency: string } {
  const fx = displayFx();
  return currency === 'EUR' && fx.currency !== 'EUR'
    ? { value: value * fx.perEur, currency: fx.currency }
    : { value, currency };
}

/** EUR amount formatted in the display currency (charts, tooltips). */
export function formatMoney(
  locale: string,
  value: number,
  options: Intl.NumberFormatOptions = {},
): string {
  const d = inDisplay(value);
  return new Intl.NumberFormat(locale, {
    style: 'currency',
    currency: d.currency,
    ...options,
  }).format(d.value);
}

@Injectable({ providedIn: 'root' })
export class DisplayCurrencyService {
  private readonly http = inject(HttpClient);
  private readonly prefs = inject(Prefs);

  constructor() {
    effect(() => {
      const currency = this.prefs.displayCurrency();
      if (currency === 'EUR') {
        displayFx.set({ currency: 'EUR', perEur: 1 });
        return;
      }
      void this.load(currency);
    });
  }

  private async load(currency: DisplayCurrency) {
    try {
      const fx = await firstValueFrom(
        this.http.get<{ eurPerUnit: number }>(`/api/fx/${currency}`, {
          context: new HttpContext().set(BACKGROUND, true),
        }),
      );
      if (fx.eurPerUnit > 0) displayFx.set({ currency, perEur: 1 / fx.eurPerUnit });
    } catch {
      // No rate yet (offline, signed out): keep showing EUR.
      displayFx.set({ currency: 'EUR', perEur: 1 });
    }
  }
}
