import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { TranslateService } from '@ngx-translate/core';

type Theme = 'light' | 'dark' | 'system';
export type Lang = 'en' | 'pt-PT';

function read(key: string): string | null {
  try {
    return localStorage.getItem(key);
  } catch {
    return null;
  }
}

function write(key: string, value: string) {
  try {
    localStorage.setItem(key, value);
  } catch {
    // Storage unavailable (private mode): preferences simply don't persist.
  }
}

/**
 * UI-only preferences. Never stores financial data in the browser — only theme, language and the last
 * account used for quick entry (an id, not a balance).
 */
@Injectable({ providedIn: 'root' })
export class Prefs {
  private readonly translate = inject(TranslateService);
  readonly theme = signal<Theme>((read('pf.theme') as Theme) ?? 'system');
  readonly lang = signal<Lang>((read('pf.lang') as Lang) ?? (navigator.language.startsWith('pt') ? 'pt-PT' : 'en'));
  readonly locale = computed(() => (this.lang() === 'pt-PT' ? 'pt-PT' : 'en-IE'));
  readonly lastAccountId = signal<string | null>(read('pf.lastAccount'));
  /** Bumps when a translation file finishes loading, so computed labels (charts) re-evaluate. */
  readonly translations = signal(0);

  constructor() {
    this.translate.onLangChange.subscribe(() => this.translations.update((v) => v + 1));
    effect(() => {
      const theme = this.theme();
      write('pf.theme', theme);
      const dark =
        theme === 'dark' || (theme === 'system' && window.matchMedia?.('(prefers-color-scheme: dark)').matches);
      document.documentElement.classList.toggle('dark', dark);
    });
    effect(() => {
      const lang = this.lang();
      write('pf.lang', lang);
      document.documentElement.lang = lang;
      this.translate.use(lang);
    });
    effect(() => {
      const id = this.lastAccountId();
      if (id) write('pf.lastAccount', id);
    });
  }
}
