import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { Api } from '../../core/api';
import { DataEvents } from '../../core/data-events';
import { Category, ExpenseNature } from '../../core/models';
import { Toasts } from '../../core/toast';
import { CategoryLabelPipe } from '../../shared/category-label';
import { ModalComponent } from '../../shared/modal';

@Component({
  selector: 'app-categories',
  imports: [TranslatePipe, CategoryLabelPipe, ModalComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mb-6 flex flex-wrap items-center justify-between gap-3">
      <h1 class="text-2xl font-semibold tracking-tight">{{ 'nav.categories' | translate }}</h1>
      <button class="btn btn-primary" (click)="openNew()">＋ {{ 'categories.new' | translate }}</button>
    </div>

    <div class="grid gap-4 lg:grid-cols-2">
      @for (group of groups(); track group.title) {
        <section class="card !p-0">
          <h2 class="card-title px-5 pt-5">{{ group.title | translate }}</h2>
          <ul class="divide-y divide-slate-100 dark:divide-slate-800">
            @for (c of group.items; track c.id) {
              <li class="flex items-center gap-3 px-5 py-2.5" [class.pl-10]="!!c.parentId" [class.opacity-50]="c.archived">
                <span class="h-3 w-3 rounded-full" [style.background]="c.color"></span>
                <span class="flex-1 text-sm">{{ c | categoryLabel }}</span>
                @if (c.type === 'Expense') {
                  <select class="input !w-auto !py-1 text-xs" [value]="c.defaultNature" (change)="setNature(c, $any($event.target).value)">
                    <option value="Fixed">{{ 'nature.Fixed' | translate }}</option>
                    <option value="Variable">{{ 'nature.Variable' | translate }}</option>
                  </select>
                }
                @if (!c.isSystem) { <span class="badge bg-slate-100 text-slate-500 dark:bg-slate-800">{{ 'categories.custom' | translate }}</span> }
                <button class="btn btn-ghost !px-2 !py-1 text-xs" (click)="toggleArchive(c)">{{ (c.archived ? 'common.restore' : 'common.archive') | translate }}</button>
              </li>
            }
          </ul>
        </section>
      }
    </div>

    <app-modal [open]="formOpen()" [title]="'categories.new' | translate" (closed)="formOpen.set(false)">
      <form class="space-y-3" (submit)="$event.preventDefault(); create()">
        <div>
          <label class="label" for="c-name">{{ 'common.name' | translate }}</label>
          <input id="c-name" class="input" required maxlength="60" (input)="name.set($any($event.target).value)" />
        </div>
        <div class="segmented">
          <button type="button" [class.active]="type() === 'Expense'" (click)="type.set('Expense')">{{ 'type.Expense' | translate }}</button>
          <button type="button" [class.active]="type() === 'Income'" (click)="type.set('Income')">{{ 'type.Income' | translate }}</button>
        </div>
        @if (type() === 'Expense') {
          <div class="segmented ml-2">
            <button type="button" [class.active]="nature() === 'Variable'" (click)="nature.set('Variable')">{{ 'nature.Variable' | translate }}</button>
            <button type="button" [class.active]="nature() === 'Fixed'" (click)="nature.set('Fixed')">{{ 'nature.Fixed' | translate }}</button>
          </div>
        }
        <div>
          <label class="label" for="c-parent">{{ 'categories.parent' | translate }}</label>
          <select id="c-parent" class="input" (change)="parentId.set($any($event.target).value || null)">
            <option value="">—</option>
            @for (p of parents(); track p.id) { <option [value]="p.id">{{ p | categoryLabel }}</option> }
          </select>
        </div>
        <div>
          <label class="label" for="c-color">{{ 'categories.color' | translate }}</label>
          <input id="c-color" type="color" class="h-9 w-16 rounded" [value]="color()" (input)="color.set($any($event.target).value)" />
        </div>
        <div class="flex justify-end gap-2 pt-2">
          <button type="button" class="btn" (click)="formOpen.set(false)">{{ 'common.cancel' | translate }}</button>
          <button class="btn btn-primary">{{ 'common.save' | translate }}</button>
        </div>
      </form>
    </app-modal>
  `,
})
export class CategoriesComponent {
  private readonly api = inject(Api);
  private readonly events = inject(DataEvents);
  private readonly toasts = inject(Toasts);
  private readonly i18n = inject(TranslateService);
  protected readonly categories = rxResource({ params: () => this.events.version(), stream: () => this.api.categories(true) });
  protected readonly formOpen = signal(false);
  protected readonly name = signal('');
  protected readonly type = signal<'Expense' | 'Income'>('Expense');
  protected readonly nature = signal<ExpenseNature>('Variable');
  protected readonly parentId = signal<string | null>(null);
  protected readonly color = signal('#64748b');

  protected readonly groups = computed(() => {
    const all = this.categories.value() ?? [];
    const ordered = (type: 'Expense' | 'Income') => {
      const roots = all.filter((c) => c.type === type && !c.parentId);
      return roots.flatMap((r) => [r, ...all.filter((c) => c.parentId === r.id)]);
    };
    return [
      { title: 'categories.expenses', items: ordered('Expense') },
      { title: 'categories.income', items: ordered('Income') },
    ];
  });

  protected readonly parents = computed(() =>
    (this.categories.value() ?? []).filter((c) => c.type === this.type() && !c.parentId && !c.archived),
  );

  protected openNew() {
    this.name.set('');
    this.parentId.set(null);
    this.formOpen.set(true);
  }

  protected async create() {
    try {
      await firstValueFrom(
        this.api.createCategory({
          name: this.name(),
          type: this.type(),
          defaultNature: this.type() === 'Expense' ? this.nature() : null,
          parentId: this.parentId(),
          color: this.color(),
          icon: null,
        }),
      );
      this.formOpen.set(false);
      this.events.bump();
      this.toasts.show(this.i18n.instant('common.saved'));
    } catch (err) {
      this.toasts.error(err);
    }
  }

  protected async setNature(c: Category, nature: ExpenseNature) {
    try {
      await firstValueFrom(this.api.updateCategory(c.id, { name: c.name, defaultNature: nature, color: c.color, icon: c.icon }));
      this.events.bump();
    } catch (err) {
      this.toasts.error(err);
    }
  }

  protected async toggleArchive(c: Category) {
    try {
      await firstValueFrom(c.archived ? this.api.restoreCategory(c.id) : this.api.archiveCategory(c.id));
      this.events.bump();
    } catch (err) {
      this.toasts.error(err);
    }
  }
}
