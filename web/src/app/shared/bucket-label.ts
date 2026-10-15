import { Pipe, PipeTransform, inject } from '@angular/core';
import { TranslateService } from '@ngx-translate/core';

/** The built-in buckets' stored (English) names, by translation key. */
const BUILT_IN: Readonly<Record<string, string>> = {
  'Stocks / ETFs': 'stocks-etfs',
  Crypto: 'crypto',
  Bonds: 'bonds',
  'Travel fund': 'travel-fund',
  'Emergency fund': 'emergency-fund',
  'Other savings': 'other-savings',
};

/**
 * Built-in buckets are translated while they keep their original name; a renamed or custom bucket shows the owner's
 * own name. Rows elsewhere (monthly lines, transactions) carry only the name, so the name is what identifies them.
 */
export function bucketLabel(i18n: TranslateService, name: string | null | undefined): string {
  if (!name) return '';
  const key = BUILT_IN[name];
  if (!key) return name;
  const translationKey = `buckets.${key}`;
  const translated = i18n.instant(translationKey);
  return translated === translationKey ? name : translated;
}

@Pipe({ name: 'bucketLabel', pure: false })
export class BucketLabelPipe implements PipeTransform {
  private readonly i18n = inject(TranslateService);

  transform(name: string | null | undefined): string {
    return bucketLabel(this.i18n, name);
  }
}
