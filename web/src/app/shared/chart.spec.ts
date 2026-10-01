import { describe, expect, it } from 'vitest';
import { chartIsEmpty } from './chart';

describe('chartIsEmpty', () => {
  it('is empty without series, with empty data or with only zeros/nulls', () => {
    expect(chartIsEmpty({})).toBe(true);
    expect(chartIsEmpty({ series: [{ type: 'bar', data: [] }] })).toBe(true);
    expect(
      chartIsEmpty({
        series: [
          { type: 'bar', data: [0, 0, null] },
          { type: 'line', data: [0] },
        ],
      }),
    ).toBe(true);
    expect(chartIsEmpty({ series: [{ type: 'line', data: [['2026-01-01', 0]] }] })).toBe(true);
    expect(chartIsEmpty({ series: [{ type: 'pie', data: [{ name: 'a', value: 0 }] }] })).toBe(true);
  });

  it('has data when any point has a non-zero value, whatever the data shape', () => {
    expect(chartIsEmpty({ series: [{ type: 'bar', data: [0, 12] }] })).toBe(false);
    expect(chartIsEmpty({ series: [{ type: 'line', data: [['2026-01-01', 3]] }] })).toBe(false);
    expect(chartIsEmpty({ series: [{ type: 'pie', data: [{ name: 'a', value: 5 }] }] })).toBe(
      false,
    );
    expect(chartIsEmpty({ series: { type: 'bar', data: [-4] } })).toBe(false);
  });
});
