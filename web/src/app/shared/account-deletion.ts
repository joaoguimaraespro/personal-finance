import { TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { Api } from '../core/api';
import { Confirm } from '../core/confirm';

/**
 * Deletes an account or a person for good, after showing what goes with it (movements, recurring items, shares).
 * Resolves true when it was deleted.
 */
export async function deleteAccountConfirmed(
  api: Api,
  confirm: Confirm,
  i18n: TranslateService,
  id: string,
): Promise<boolean> {
  const p = await firstValueFrom(api.accountDeletion(id));
  const parts = [
    p.transactions ? i18n.instant('deletion.transactions', { count: p.transactions }) : '',
    p.recurring ? i18n.instant('deletion.recurring', { count: p.recurring }) : '',
    p.sharedParts ? i18n.instant('deletion.sharedParts', { count: p.sharedParts }) : '',
    p.hasLoanTerms ? i18n.instant('deletion.loan') : '',
  ].filter((x) => !!x);
  const message =
    i18n.instant(p.kind === 'Person' ? 'deletion.askPerson' : 'deletion.ask', { name: p.name }) +
    ' ' +
    (parts.length
      ? i18n.instant('deletion.alsoDeletes', { what: parts.join(', ') })
      : i18n.instant('deletion.nothingElse')) +
    ' ' +
    i18n.instant('deletion.cannotUndo');
  const ok = await confirm.ask(message, {
    destructive: true,
    confirmLabel: i18n.instant('common.delete'),
  });
  if (!ok) return false;
  await firstValueFrom(api.deleteAccount(id));
  return true;
}
