import { Pipe, PipeTransform, inject } from '@angular/core';
import { TranslateService } from '@ngx-translate/core';

export interface Labelled {
  key?: string | null;
  categoryKey?: string | null;
  name?: string | null;
  categoryName?: string | null;
}

/** Built-in categories are translated by key; custom categories show the user's own name. */
export function categoryLabel(i18n: TranslateService, c: Labelled | null): string {
  if (!c) return '—';
  const key = c.key ?? c.categoryKey;
  const name = c.name ?? c.categoryName ?? '';
  if (!key || key.startsWith('custom-')) return name;
  const translationKey = `categories.${key}`;
  const translated = i18n.instant(translationKey);
  return translated === translationKey ? name : translated;
}

@Pipe({ name: 'categoryLabel', pure: false })
export class CategoryLabelPipe implements PipeTransform {
  private readonly i18n = inject(TranslateService);

  transform(c: Labelled | null): string {
    return categoryLabel(this.i18n, c);
  }
}
