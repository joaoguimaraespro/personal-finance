import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TranslatePipe } from '@ngx-translate/core';
import { HlmInputImports } from '@spartan-ng/helm/input';
import {
  Subject,
  catchError,
  debounceTime,
  distinctUntilChanged,
  map,
  of,
  switchMap,
  tap,
} from 'rxjs';
import { Api } from '../../core/api';
import { CoinMatch } from '../../core/models';
import { CoinIconComponent } from '../../shared/coin-icon';

/**
 * Search-as-you-type over the price provider's coins (name or ticker). Only the typed text is sent to the
 * server, which asks the provider; nothing about holdings leaves it.
 */
@Component({
  selector: 'app-coin-search',
  imports: [CoinIconComponent, HlmInputImports, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="relative">
      <input
        [id]="inputId()"
        hlmInput
        class="w-full"
        type="search"
        role="combobox"
        autocomplete="off"
        aria-autocomplete="list"
        [attr.aria-controls]="inputId() + '-list'"
        [attr.aria-expanded]="open()"
        [attr.aria-invalid]="invalid() || null"
        [attr.aria-activedescendant]="active() >= 0 ? inputId() + '-' + active() : null"
        [placeholder]="'crypto.coinPlaceholder' | translate"
        [value]="query()"
        (input)="onInput($any($event.target).value)"
        (keydown)="onKey($event)"
        (focus)="focused.set(true)"
        (blur)="focused.set(false)"
      />
      @if (open()) {
        <ul
          [id]="inputId() + '-list'"
          role="listbox"
          class="bg-popover text-popover-foreground absolute inset-x-0 top-full z-50 mt-1 max-h-64 overflow-y-auto rounded-lg border p-1 shadow-md"
        >
          @if (loading()) {
            <li class="text-muted-foreground px-3 py-2 text-sm">
              {{ 'asset.searching' | translate }}
            </li>
          } @else {
            @for (c of items(); track c.id; let i = $index) {
              <li
                [id]="inputId() + '-' + i"
                role="option"
                [attr.aria-selected]="i === active()"
                class="flex cursor-pointer items-center gap-3 rounded-md px-3 py-2 text-sm"
                [class.bg-accent]="i === active()"
                (mousedown)="$event.preventDefault(); pick(c)"
                (mouseenter)="active.set(i)"
              >
                <app-coin-icon [symbol]="c.symbol" [size]="22" />
                <span class="w-16 shrink-0 truncate font-medium">{{ c.symbol }}</span>
                <span class="text-muted-foreground min-w-0 flex-1 truncate">{{ c.name }}</span>
              </li>
            } @empty {
              <li class="text-muted-foreground px-3 py-2 text-sm">
                {{ (query().trim().length < 2 ? 'crypto.typeMore' : 'crypto.noCoins') | translate }}
              </li>
            }
          }
        </ul>
      }
    </div>
  `,
})
export class CoinSearchComponent {
  private readonly api = inject(Api);
  readonly inputId = input('coin-search');
  readonly invalid = input(false);
  readonly picked = output<CoinMatch>();

  protected readonly query = signal('');
  protected readonly focused = signal(false);
  protected readonly loading = signal(false);
  protected readonly active = signal(-1);
  protected readonly items = signal<CoinMatch[]>([]);
  private readonly queries = new Subject<string>();

  protected readonly open = computed(() => this.focused() && this.query().trim().length > 0);

  constructor() {
    this.queries
      .pipe(
        map((q) => q.trim()),
        debounceTime(250),
        distinctUntilChanged(),
        tap((q) => this.loading.set(q.length >= 2)),
        switchMap((q) =>
          q.length < 2 ? of([]) : this.api.searchCoins(q).pipe(catchError(() => of([]))),
        ),
        takeUntilDestroyed(inject(DestroyRef)),
      )
      .subscribe((r) => {
        this.loading.set(false);
        this.items.set(r);
        this.active.set(r.length ? 0 : -1);
      });
  }

  protected onInput(value: string) {
    this.query.set(value);
    this.queries.next(value);
  }

  protected onKey(event: KeyboardEvent) {
    const count = this.items().length;
    if (event.key === 'ArrowDown' && count) {
      event.preventDefault();
      this.active.set((this.active() + 1) % count);
    } else if (event.key === 'ArrowUp' && count) {
      event.preventDefault();
      this.active.set((this.active() - 1 + count) % count);
    } else if (event.key === 'Enter' && this.open() && this.active() >= 0 && count) {
      // Picking a suggestion must not submit the surrounding form.
      event.preventDefault();
      event.stopPropagation();
      this.pick(this.items()[this.active()]);
    } else if (event.key === 'Escape' && this.open()) {
      event.stopPropagation();
      this.focused.set(false);
    }
  }

  protected pick(c: CoinMatch) {
    this.query.set('');
    this.queries.next('');
    this.items.set([]);
    this.focused.set(false);
    this.picked.emit(c);
  }
}
