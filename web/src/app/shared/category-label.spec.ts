import { describe, expect, it } from 'vitest';
import { TranslateService } from '@ngx-translate/core';
import { categoryLabel } from './category-label';

const i18n = {
  instant: (key: string) => (key === 'categories.gym' ? 'Ginásio' : key),
} as unknown as TranslateService;

describe('categoryLabel', () => {
  it('translates built-in categories by key', () => {
    expect(categoryLabel(i18n, { key: 'gym', name: 'Gym' })).toBe('Ginásio');
  });

  it("shows the owner's name for a renamed built-in category", () => {
    expect(categoryLabel(i18n, { key: 'gym', name: 'Ginásio & Padel', renamed: true })).toBe('Ginásio & Padel');
  });

  it('shows custom categories by name', () => {
    expect(categoryLabel(i18n, { key: 'custom-side-project', name: 'Side project' })).toBe('Side project');
  });
});
