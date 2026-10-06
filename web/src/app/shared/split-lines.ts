import { ChangeDetectionStrategy, Component, computed, input, model } from '@angular/core';
import { NgIcon } from '@ng-icons/core';
import { TranslatePipe } from '@ngx-translate/core';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { MoneyPipe } from '../core/format';
import { SplitLineRequest } from '../core/models';
import { APP_ICONS } from './icons';
import { parseAmount } from './parse-amount';
import { SelectComponent, SelectOption } from './select';

/** One line as typed in the form: amounts stay text until saved (decimal comma or dot). */
export interface SplitDraftLine {
  categoryId: string;
  amount: string;
  note: string;
}

export const MIN_SPLIT_LINES = 2;
export const MAX_SPLIT_LINES = 20;
export const MAX_SPLIT_NOTE = 120;

export type SplitError = 'count' | 'category' | 'duplicate' | 'amount' | 'note' | 'sum';

const emptyLine = (): SplitDraftLine => ({ categoryId: '', amount: '', note: '' });

/** Exact sum in 1/10000 units, so 0,1 + 0,2 is 0,3 and never 0,30000000000000004. */
export function splitSum(lines: SplitDraftLine[]): number {
  const units = lines.reduce((sum, l) => sum + Math.round((parseAmount(l.amount) ?? 0) * 10000), 0);
  return units / 10000;
}

/** What is still to be assigned to a line (negative when the lines exceed the total). */
export function splitRemaining(total: number | null, lines: SplitDraftLine[]): number {
  return (Math.round((total ?? 0) * 10000) - Math.round(splitSum(lines) * 10000)) / 10000;
}

/** The first problem that stops the split from being saved, or null. Mirrors the server's rules. */
export function splitError(total: number | null, lines: SplitDraftLine[]): SplitError | null {
  if (lines.length < MIN_SPLIT_LINES || lines.length > MAX_SPLIT_LINES) return 'count';
  if (lines.some((l) => !l.categoryId)) return 'category';
  if (new Set(lines.map((l) => l.categoryId)).size !== lines.length) return 'duplicate';
  if (lines.some((l) => !((parseAmount(l.amount) ?? 0) > 0))) return 'amount';
  if (lines.some((l) => l.note.trim().length > MAX_SPLIT_NOTE)) return 'note';
  return splitRemaining(total, lines) === 0 ? null : 'sum';
}

export function toSplitRequests(lines: SplitDraftLine[]): SplitLineRequest[] {
  return lines.map((l) => ({
    categoryId: l.categoryId,
    amount: parseAmount(l.amount)!,
    note: l.note.trim() || null,
  }));
}

export function fromSplits(
  splits: { categoryId: string; amount: number; note?: string | null }[],
): SplitDraftLine[] {
  return splits.map((s) => ({
    categoryId: s.categoryId,
    amount: String(s.amount).replace('.', ','),
    note: s.note ?? '',
  }));
}

/** Starting a split: the current category on the first line, then an empty one. */
export function startSplit(categoryId: string | null): SplitDraftLine[] {
  return [{ ...emptyLine(), categoryId: categoryId ?? '' }, emptyLine()];
}

/** Puts what is left into the last line without an amount (or the last line). */
export function fillRemaining(total: number | null, lines: SplitDraftLine[]): SplitDraftLine[] {
  let target = lines.length - 1;
  for (let i = lines.length - 1; i >= 0; i--) {
    if (!lines[i].amount.trim()) {
      target = i;
      break;
    }
  }
  const others = lines.filter((_, i) => i !== target);
  const left = splitRemaining(total, others);
  if (left <= 0) return lines;
  return lines.map((l, i) => (i === target ? { ...l, amount: String(left).replace('.', ',') } : l));
}

/**
 * Category lines of one movement (e.g. one energy bill: electricity + gas). Each line has a category and an amount,
 * with an optional note; the remaining amount is shown live and must reach zero before saving.
 */
@Component({
  selector: 'app-split-lines',
  imports: [NgIcon, TranslatePipe, MoneyPipe, SelectComponent, HlmInputImports, HlmButtonImports],
  providers: [APP_ICONS],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="space-y-2" role="group" [attr.aria-label]="'split.title' | translate">
      @for (line of lines(); track $index; let i = $index) {
        <!-- Category and remove on the first row; amount and note on the second (fits a 390px phone). -->
        <div class="space-y-2 rounded-xl border p-2" data-testid="split-line">
          <div class="flex items-center gap-2">
            <app-select
              class="flex-1"
              size="sm"
              [inputId]="idPrefix() + '-cat-' + i"
              [options]="options()"
              [value]="line.categoryId"
              [ariaLabel]="('tx.category' | translate) + ' ' + (i + 1)"
              (valueChange)="patch(i, { categoryId: $event })"
            />
            <button
              type="button"
              hlmBtn
              variant="ghost"
              size="icon-sm"
              [disabled]="lines().length <= min"
              [attr.aria-label]="'split.removeLine' | translate"
              (click)="remove(i)"
            >
              <ng-icon name="lucideX" />
            </button>
          </div>
          <div class="flex gap-2">
            <input
              hlmInput
              class="num h-8 w-28 shrink-0 text-right"
              inputmode="decimal"
              autocomplete="off"
              placeholder="0,00"
              [id]="idPrefix() + '-amt-' + i"
              [attr.aria-label]="('tx.amount' | translate) + ' ' + (i + 1)"
              [value]="line.amount"
              (input)="patch(i, { amount: $any($event.target).value })"
            />
            <input
              hlmInput
              class="h-8 min-w-0 flex-1 text-sm"
              [attr.maxlength]="maxNote"
              autocomplete="off"
              [placeholder]="'split.note' | translate"
              [attr.aria-label]="('split.note' | translate) + ' ' + (i + 1)"
              [value]="line.note"
              (input)="patch(i, { note: $any($event.target).value })"
            />
          </div>
        </div>
      }
      <div class="flex flex-wrap items-center justify-between gap-2">
        <div class="flex gap-2">
          <button
            type="button"
            hlmBtn
            variant="outline"
            size="sm"
            [disabled]="lines().length >= max"
            (click)="add()"
          >
            <ng-icon name="lucidePlus" />{{ 'split.addLine' | translate }}
          </button>
          @if (remaining() > 0) {
            <button type="button" hlmBtn variant="ghost" size="sm" (click)="fill()">
              {{ 'split.fillRemaining' | translate }}
            </button>
          }
        </div>
        <span
          class="num text-sm font-medium"
          aria-live="polite"
          data-testid="split-remaining"
          [class]="
            remaining() === 0
              ? 'text-emerald-700 dark:text-emerald-400'
              : 'text-rose-600 dark:text-rose-400'
          "
        >
          @if (remaining() === 0) {
            {{ 'split.balanced' | translate }}
          } @else if (remaining() > 0) {
            {{ 'split.remaining' | translate: { amount: (remaining() | money: currency()) } }}
          } @else {
            {{ 'split.over' | translate: { amount: (-remaining() | money: currency()) } }}
          }
        </span>
      </div>
    </div>
  `,
})
export class SplitLinesComponent {
  readonly lines = model.required<SplitDraftLine[]>();
  readonly options = input.required<SelectOption[]>();
  readonly total = input<number | null>(null);
  readonly currency = input('EUR');
  readonly idPrefix = input('split');

  protected readonly min = MIN_SPLIT_LINES;
  protected readonly max = MAX_SPLIT_LINES;
  protected readonly maxNote = MAX_SPLIT_NOTE;
  protected readonly remaining = computed(() => splitRemaining(this.total(), this.lines()));

  protected patch(index: number, change: Partial<SplitDraftLine>) {
    this.lines.update((list) => list.map((l, i) => (i === index ? { ...l, ...change } : l)));
  }

  protected add() {
    this.lines.update((list) => [...list, emptyLine()]);
    const next = this.lines().length - 1;
    setTimeout(() => document.getElementById(`${this.idPrefix()}-amt-${next}`)?.focus());
  }

  protected remove(index: number) {
    this.lines.update((list) => list.filter((_, i) => i !== index));
  }

  protected fill() {
    this.lines.set(fillRemaining(this.total(), this.lines()));
  }
}
