import { Account } from '../core/models';
import { SelectOption } from './select';

/** An account in a select: its name, with its bank (institution) as a muted hint when it has one. */
export function accountOption(a: Pick<Account, 'id' | 'name' | 'institution'>): SelectOption {
  const institution = a.institution?.trim();
  const hint =
    institution && institution !== a.name && !institution.startsWith('Manual')
      ? institution
      : undefined;
  return { value: a.id, label: a.name, hint };
}
