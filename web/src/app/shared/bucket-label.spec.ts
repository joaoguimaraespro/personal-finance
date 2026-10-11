import { TranslateService } from '@ngx-translate/core';
import { bucketLabel } from './bucket-label';

describe('bucketLabel', () => {
  const i18n = {
    instant: (key: string) => (key === 'buckets.travel-fund' ? 'Fundo de viagem' : key),
  } as unknown as TranslateService;

  it('translates a built-in bucket that keeps its original name', () => {
    expect(bucketLabel(i18n, 'Travel fund')).toBe('Fundo de viagem');
  });

  it('keeps renamed and custom buckets as named', () => {
    expect(bucketLabel(i18n, 'Viagem ao Japão')).toBe('Viagem ao Japão');
    expect(bucketLabel(i18n, 'Bonds')).toBe('Bonds'); // no translation loaded: the stored name
    expect(bucketLabel(i18n, null)).toBe('');
  });
});
