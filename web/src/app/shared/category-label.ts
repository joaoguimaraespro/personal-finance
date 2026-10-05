import { Injectable, Pipe, PipeTransform, effect, inject, signal } from '@angular/core';
import { TranslateService } from '@ngx-translate/core';
import { Api } from '../core/api';
import { DataEvents } from '../core/data-events';
import { BACKGROUND } from '../core/activity';
import { HttpContext } from '@angular/common/http';

export interface Labelled {
  key?: string | null;
  categoryKey?: string | null;
  name?: string | null;
  categoryName?: string | null;
  renamed?: boolean;
}

/**
 * Built-in categories the owner renamed, by key. Rows elsewhere (transactions, budgets, reports) carry only the
 * key and the stored name, so the label needs this to know a rename beats the translation.
 */
const renamedByKey = signal<ReadonlyMap<string, string>>(new Map());

/** Built-in categories are translated by key unless renamed; custom categories show the user's own name. */
export function categoryLabel(i18n: TranslateService, c: Labelled | null): string {
  if (!c) return '—';
  const key = c.key ?? c.categoryKey;
  const name = c.name ?? c.categoryName ?? '';
  if (!key || key.startsWith('custom-')) return name;
  if (c.renamed) return name;
  const renamed = renamedByKey().get(key);
  if (renamed) return renamed;
  const translationKey = `categories.${key}`;
  const translated = i18n.instant(translationKey);
  return translated === translationKey ? name : translated;
}

/** Keeps the renamed-category lookup current; started once by the shell. */
@Injectable({ providedIn: 'root' })
export class CategoryNames {
  private readonly api = inject(Api);
  private readonly events = inject(DataEvents);

  constructor() {
    effect(() => {
      this.events.version();
      this.api.categories(true, new HttpContext().set(BACKGROUND, true)).subscribe({
        next: (list) =>
          renamedByKey.set(new Map(list.filter((c) => c.renamed).map((c) => [c.key, c.name]))),
        error: () => undefined,
      });
    });
  }

  /** Called once by the shell so the lookup exists before any category is labelled. */
  start() {
    return this;
  }
}

@Pipe({ name: 'categoryLabel', pure: false })
export class CategoryLabelPipe implements PipeTransform {
  private readonly i18n = inject(TranslateService);

  transform(c: Labelled | null): string {
    return categoryLabel(this.i18n, c);
  }
}
