import { FormState, rules, serverErrors } from './form-errors';

describe('form errors', () => {
  it('checks fields and ignores the ones without an error', () => {
    const form = new FormState<'name' | 'amount'>();
    expect(form.check({ name: rules.required(' '), amount: rules.positive(5) })).toBe(false);
    expect(form.errors()).toEqual({ name: 'validation.required' });
    expect(form.check({ name: rules.required('Ana'), amount: rules.positive(5) })).toBe(true);
    expect(form.errors()).toEqual({});
  });

  it('puts the server field errors under their fields, or its message on the form', () => {
    const form = new FormState<'name'>();
    form.fail({ error: { title: 'Bad', errors: { Name: ['Too long'] } } });
    expect(form.errors()).toEqual({ name: 'Too long' });
    expect(form.message()).toBeNull();
    form.fail({ error: { title: 'Already exists' } });
    expect(form.message()).toBe('Already exists');
  });

  it('maps nested server keys to the last segment', () => {
    expect(serverErrors({ error: { errors: { 'Shares[0].Amount': ['x'] } } }).fields).toEqual({
      amount: 'x',
    });
  });

  it('has the common rules', () => {
    expect(rules.positive(null)).toBe('validation.number');
    expect(rules.positive(0)).toBe('validation.positive');
    expect(rules.number('', null)).toBeUndefined();
    expect(rules.number('abc', null)).toBe('validation.number');
    expect(rules.pick('')).toBe('validation.pick');
  });
});
