import { displayFx, inDisplay } from './display-currency';

describe('display currency', () => {
  afterEach(() => displayFx.set({ currency: 'EUR', perEur: 1 }));

  it('shows EUR amounts in USD at the loaded rate and leaves other currencies alone', () => {
    displayFx.set({ currency: 'USD', perEur: 1.1 });
    expect(inDisplay(100)).toEqual({ value: 110.00000000000001, currency: 'USD' });
    expect(inDisplay(50, 'GBP')).toEqual({ value: 50, currency: 'GBP' });
  });

  it('keeps EUR as is by default', () => {
    expect(inDisplay(100)).toEqual({ value: 100, currency: 'EUR' });
  });
});
