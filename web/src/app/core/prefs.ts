import { Injectable, computed, effect, inject, signal, untracked } from '@angular/core';
import { TranslateService } from '@ngx-translate/core';
import { injectBrnCalendarI18n } from '@spartan-ng/brain/calendar';
import type { ReturnPeriod } from './models';

type Theme = 'light' | 'dark' | 'system';
export type Lang = 'en' | 'pt-PT';

function read(key: string): string | null {
  try {
    return localStorage.getItem(key);
  } catch {
    return null;
  }
}

function readPeriod(): ReturnPeriod {
  const stored = read('pf.portfolioPeriod');
  return stored === '1M' || stored === 'YTD' || stored === '1Y' ? stored : 'ALL';
}

function write(key: string, value: string) {
  try {
    localStorage.setItem(key, value);
  } catch {
    // Storage unavailable (private mode): preferences simply don't persist.
  }
}

/**
 * UI-only preferences. Never stores financial data in the browser — only theme, language, the last
 * account used for quick entry (an id, not a balance) and the period the portfolio's returns are shown for.
 */
@Injectable({ providedIn: 'root' })
export class Prefs {
  private readonly translate = inject(TranslateService);
  private readonly calendarI18n = injectBrnCalendarI18n();
  readonly theme = signal<Theme>((read('pf.theme') as Theme) ?? 'system');
  readonly lang = signal<Lang>(
    (read('pf.lang') as Lang) ?? (navigator.language.startsWith('pt') ? 'pt-PT' : 'en'),
  );
  readonly locale = computed(() => (this.lang() === 'pt-PT' ? 'pt-PT' : 'en-IE'));
  readonly lastAccountId = signal<string | null>(read('pf.lastAccount'));
  /** Period of the returns on the portfolio page (1M, YTD, 1Y or ALL). */
  readonly portfolioPeriod = signal<ReturnPeriod>(readPeriod());
  /** Currency figures are shown in (all data stays in EUR). */
  readonly displayCurrency = signal<'EUR' | 'USD'>(read('pf.currency') === 'USD' ? 'USD' : 'EUR');
  /** Bumps when a translation file finishes loading, so computed labels (charts) re-evaluate. */
  readonly translations = signal(0);

  constructor() {
    this.translate.onLangChange.subscribe(() => this.translations.update((v) => v + 1));
    effect(() => {
      const theme = this.theme();
      write('pf.theme', theme);
      const dark =
        theme === 'dark' ||
        (theme === 'system' && window.matchMedia?.('(prefers-color-scheme: dark)').matches);
      document.documentElement.classList.toggle('dark', dark);
    });
    effect(() => {
      const lang = this.lang();
      write('pf.lang', lang);
      document.documentElement.lang = lang;
      this.translate.use(lang);
      // untracked: the calendar service reads its own config signal while updating it.
      untracked(() => this.localiseCalendar(lang === 'pt-PT' ? 'pt-PT' : 'en-IE'));
    });
    effect(() => {
      const id = this.lastAccountId();
      if (id) write('pf.lastAccount', id);
    });
    effect(() => write('pf.portfolioPeriod', this.portfolioPeriod()));
    effect(() => write('pf.currency', this.displayCurrency()));
  }

  /** Calendar vocabulary from Intl, so pickers match the rest of the app's date formatting. Weeks start on Monday. */
  private localiseCalendar(locale: string) {
    const month = (m: number, style: 'long' | 'short') =>
      new Intl.DateTimeFormat(locale, { month: style })
        .format(new Date(2024, m, 1))
        .replace('.', '');
    // 2024-01-07 was a Sunday: index 0 = Sunday, as the calendar expects.
    const weekday = (i: number, style: 'long' | 'short') =>
      new Intl.DateTimeFormat(locale, { weekday: style })
        .format(new Date(2024, 0, 7 + i))
        .replace('.', '');
    const months = Array.from({ length: 12 }, (_, m) => month(m, 'long'));
    const pt = locale.startsWith('pt');
    this.calendarI18n.use({
      formatWeekdayName: (i) => weekday(i, 'short').slice(0, 2),
      labelWeekday: (i) => weekday(i, 'long'),
      formatHeader: (m, y) =>
        new Intl.DateTimeFormat(locale, { month: 'long', year: 'numeric' }).format(
          new Date(y, m, 1),
        ),
      formatMonth: (m) => month(m, 'short'),
      months: () => months as never,
      firstDayOfWeek: () => 1,
      labelPrevious: () => (pt ? 'Mês anterior' : 'Previous month'),
      labelNext: () => (pt ? 'Mês seguinte' : 'Next month'),
    });
  }
}
