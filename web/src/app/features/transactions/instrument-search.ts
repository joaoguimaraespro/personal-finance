import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  output,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TranslatePipe } from '@ngx-translate/core';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { Subject, catchError, debounceTime, distinctUntilChanged, map, of, switchMap, tap } from 'rxjs';
import { Api } from '../../core/api';
import { InstrumentMatch, InstrumentSearchResult } from '../../core/models';

/**
 * Search-as-you-type over the server's instrument lookup (Trading 212, IBKR, synced holdings). Never used for crypto.
 * Any failure yields an empty list plus a hint; the form around it always stays usable by hand.
 */
@Component({
  selector: 'app-instrument-search',
  imports: [HlmInputImports, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="relative">
      <label class="label" for="qa-instrument">{{ 'asset.search' | translate }}</label>
      <input
        id="qa-instrument"
        hlmInput
        type="search"
        role="combobox"
        autocomplete="off"
        aria-autocomplete="list"
        aria-controls="qa-instrument-list"
        [attr.aria-expanded]="open()"
        [attr.aria-activedescendant]="active() >= 0 ? 'qa-instrument-' + active() : null"
        [placeholder]="'asset.searchPlaceholder' | translate"
        [value]="query()"
        (input)="onInput($any($event.target).value)"
        (keydown)="onKey($event)"
        (focus)="focused.set(true)"
        (blur)="onBlur()"
      />
      @if (open()) {
        <ul
          id="qa-instrument-list"
          role="listbox"
          class="bg-popover text-popover-foreground absolute inset-x-0 top-full z-50 mt-1 max-h-64 overflow-y-auto rounded-lg border p-1 shadow-md"
        >
          @if (loading()) {
            <li class="text-muted-foreground px-3 py-2 text-sm">{{ 'asset.searching' | translate }}</li>
          } @else {
            @for (m of items(); track m.provider + m.brokerSymbol; let i = $index) {
              <li
                [id]="'qa-instrument-' + i"
                role="option"
                [attr.aria-selected]="i === active()"
                class="flex cursor-pointer items-center gap-3 rounded-md px-3 py-2 text-sm"
                [class.bg-accent]="i === active()"
                (mousedown)="$event.preventDefault(); pick(m)"
                (mouseenter)="active.set(i)"
              >
                <span class="min-w-0 flex-1">
                  <span class="block truncate font-medium">{{ m.symbol }}</span>
                  <span class="text-muted-foreground block truncate text-xs">
                    {{ m.name }}@if (m.isin) {
                      · {{ m.isin }}
                    }
                  </span>
                </span>
                <span class="text-muted-foreground shrink-0 text-right text-[11px] leading-tight">
                  {{ 'asset.provider.' + m.provider | translate }}<br />{{ m.currency }}
                </span>
              </li>
            } @empty {
              <li class="text-muted-foreground px-3 py-2 text-sm">
                {{ 'asset.noResults' | translate }}
              </li>
            }
          }
        </ul>
      }
      @if (hint(); as h) {
        <p class="text-muted-foreground mt-1.5 text-xs">{{ h | translate }}</p>
      }
    </div>
  `,
})
export class InstrumentSearchComponent {
  private readonly api = inject(Api);
  readonly picked = output<InstrumentMatch>();

  protected readonly query = signal('');
  protected readonly focused = signal(false);
  protected readonly loading = signal(false);
  protected readonly active = signal(-1);
  private readonly result = signal<InstrumentSearchResult | null>(null);
  private readonly queries = new Subject<string>();

  protected readonly items = computed(() => this.result()?.items ?? []);
  protected readonly open = computed(() => this.focused() && this.query().trim().length > 0);
  /** Explains why suggestions may be thin: no broker keys, or a broker reported a problem. */
  protected readonly hint = computed(() => {
    const providers = this.result()?.providers ?? [];
    const message = providers.find((p) => p.configured && p.message)?.message;
    if (message) return message;
    const brokers = providers.filter((p) => p.provider !== 'Portfolio');
    return brokers.length && brokers.every((p) => !p.configured) ? 'asset.notConfigured' : null;
  });

  constructor() {
    this.queries
      .pipe(
        map((q) => q.trim()),
        debounceTime(250),
        distinctUntilChanged(),
        tap((q) => this.loading.set(q.length > 0)),
        switchMap((q) =>
          q.length === 0
            ? of(null)
            : this.api.searchInstruments(q).pipe(catchError(() => of({ items: [], providers: [] }))),
        ),
        takeUntilDestroyed(inject(DestroyRef)),
      )
      .subscribe((r) => {
        this.loading.set(false);
        if (r) this.result.set(r);
        this.active.set(r?.items.length ? 0 : -1);
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

  protected onBlur() {
    this.focused.set(false);
  }

  protected pick(m: InstrumentMatch) {
    this.query.set('');
    this.queries.next('');
    this.result.set(null);
    this.focused.set(false);
    this.picked.emit(m);
  }
}
