import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FORM_ERRORS, FormState, rules } from '../../shared/form-errors';
import { liveResource } from '../../core/resource';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { Api } from '../../core/api';
import { Confirm } from '../../core/confirm';
import { DataEvents } from '../../core/data-events';
import { Category, ExpenseNature } from '../../core/models';
import { Prefs } from '../../core/prefs';
import { Toasts } from '../../core/toast';
import { CategoryLabelPipe, categoryLabel } from '../../shared/category-label';
import { ModalComponent } from '../../shared/modal';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { SelectComponent, SelectOption } from '../../shared/select';
import { NgIcon } from '@ng-icons/core';
import { APP_ICONS, PAGE_ICONS } from '../../shared/icons';
import { PageHeaderComponent } from '../../shared/page-header';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';

@Component({
  selector: 'app-categories',
  imports: [
    ...FORM_ERRORS,
    PageHeaderComponent,
    HlmTooltipImports,
    NgIcon,
    SelectComponent,
    HlmInputImports,
    HlmButtonImports,
    TranslatePipe,
    CategoryLabelPipe,
    ModalComponent,
  ],
  providers: [APP_ICONS],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-page-header [icon]="icons.categories" [title]="'nav.categories' | translate">
      <button hlmBtn class="self-start sm:self-auto" (click)="openNew()">
        <ng-icon name="lucidePlus" />{{ 'categories.new' | translate }}
      </button>
    </app-page-header>

    <div class="grid gap-4 lg:grid-cols-2">
      @for (group of groups(); track group.title) {
        <section class="card !p-0">
          <h2 class="card-title px-5 pt-5">
            <ng-icon [name]="group.icon" />{{ group.title | translate }}
          </h2>
          <ul class="divide-y divide-border">
            @for (c of group.items; track c.id) {
              <li
                class="hover:bg-muted/40 flex flex-wrap items-center gap-x-3 gap-y-1.5 px-5 py-2.5 transition-colors"
                [class.pl-10]="!!c.parentId"
                [class.opacity-50]="c.archived"
              >
                <span class="h-3 w-3 shrink-0 rounded-full" [style.background]="c.color"></span>
                @if (renamingId() === c.id) {
                  <form
                    class="flex min-w-0 flex-1 items-center gap-1"
                    (submit)="$event.preventDefault(); saveRename(c)"
                  >
                    <input
                      hlmInput
                      class="h-8 min-w-0 flex-1 text-sm"
                      maxlength="60"
                      [attr.aria-label]="'categories.newName' | translate"
                      [value]="renameValue()"
                      (input)="renameValue.set($any($event.target).value)"
                      (keydown.escape)="renamingId.set(null)"
                      autofocus
                    />
                    <button
                      hlmBtn
                      variant="ghost"
                      size="icon-sm"
                      type="submit"
                      [attr.aria-label]="'common.save' | translate"
                    >
                      <ng-icon name="lucideCheck" />
                    </button>
                    <button
                      hlmBtn
                      variant="ghost"
                      size="icon-sm"
                      type="button"
                      [attr.aria-label]="'common.cancel' | translate"
                      (click)="renamingId.set(null)"
                    >
                      <ng-icon name="lucideX" />
                    </button>
                  </form>
                } @else {
                  <span class="min-w-0 flex-1 truncate text-sm">{{ c | categoryLabel }}</span>
                  @if (c.renamed) {
                    <button
                      hlmBtn
                      variant="ghost"
                      size="icon-sm"
                      (click)="resetName(c)"
                      [attr.aria-label]="'categories.resetName' | translate"
                      [hlmTooltip]="'categories.resetName' | translate"
                    >
                      <ng-icon name="lucideRotateCcw" />
                    </button>
                  }
                  <button
                    hlmBtn
                    variant="ghost"
                    size="icon-sm"
                    (click)="startRename(c)"
                    [attr.aria-label]="'categories.rename' | translate"
                    [hlmTooltip]="'categories.rename' | translate"
                  >
                    <ng-icon name="lucidePencil" />
                  </button>
                }
                @if (c.type === 'Expense') {
                  <app-select
                    class="w-32"
                    size="sm"
                    triggerClass="text-xs"
                    [options]="natureOptions()"
                    [value]="c.defaultNature"
                    [ariaLabel]="c | categoryLabel"
                    (valueChange)="setNature(c, $any($event))"
                  />
                }
                @if (!c.isSystem) {
                  <span class="badge bg-muted text-muted-foreground"
                    ><ng-icon name="lucideUserRound" aria-hidden="true" />{{
                      'categories.custom' | translate
                    }}</span
                  >
                }
                <button
                  hlmBtn
                  variant="ghost"
                  size="icon-sm"
                  (click)="toggleArchive(c)"
                  [attr.aria-label]="(c.archived ? 'common.restore' : 'common.archive') | translate"
                  [hlmTooltip]="(c.archived ? 'common.restore' : 'common.archive') | translate"
                >
                  <ng-icon [name]="c.archived ? 'lucideArchiveRestore' : 'lucideArchive'" />
                </button>
                @if (!c.isSystem) {
                  <button
                    hlmBtn
                    variant="ghost"
                    size="icon-sm"
                    class="text-destructive"
                    (click)="remove(c)"
                    [attr.aria-label]="('common.delete' | translate) + ' · ' + c.name"
                    [hlmTooltip]="'common.delete' | translate"
                  >
                    <ng-icon name="lucideTrash2" />
                  </button>
                }
              </li>
            }
          </ul>
        </section>
      }
    </div>

    <app-modal
      [open]="formOpen()"
      [title]="'categories.new' | translate"
      (closed)="formOpen.set(false)"
    >
      <form novalidate class="space-y-3" (submit)="$event.preventDefault(); create()">
        <div>
          <label class="label" for="c-name">{{ 'common.name' | translate }}</label>
          <input
            id="c-name"
            hlmInput
            required
            maxlength="60"
            (input)="fs.touch('name'); name.set($any($event.target).value)"
            [attr.aria-invalid]="!!fs.errors().name || null"
          />
          <app-field-error [error]="fs.errors().name" />
        </div>
        <div class="segmented">
          <button
            type="button"
            class="gap-1.5"
            [class.active]="type() === 'Expense'"
            (click)="type.set('Expense')"
          >
            <ng-icon name="lucideArrowUpRight" aria-hidden="true" />{{ 'type.Expense' | translate }}
          </button>
          <button
            type="button"
            class="gap-1.5"
            [class.active]="type() === 'Income'"
            (click)="type.set('Income')"
          >
            <ng-icon name="lucideArrowDownLeft" aria-hidden="true" />{{ 'type.Income' | translate }}
          </button>
        </div>
        @if (type() === 'Expense') {
          <div class="segmented ml-2">
            <button
              type="button"
              [class.active]="nature() === 'Variable'"
              (click)="nature.set('Variable')"
            >
              {{ 'nature.Variable' | translate }}
            </button>
            <button
              type="button"
              [class.active]="nature() === 'Fixed'"
              (click)="nature.set('Fixed')"
            >
              {{ 'nature.Fixed' | translate }}
            </button>
          </div>
        }
        <div>
          <label class="label" for="c-parent">{{ 'categories.parent' | translate }}</label>
          <app-select
            inputId="c-parent"
            [options]="parentOptions()"
            [value]="parentId() ?? ''"
            (valueChange)="parentId.set($event || null)"
          />
        </div>
        <div>
          <label class="label" for="c-color">{{ 'categories.color' | translate }}</label>
          <input
            id="c-color"
            type="color"
            class="h-9 w-16 rounded"
            [value]="color()"
            (input)="color.set($any($event.target).value)"
          />
        </div>
        <app-form-alert [message]="fs.message()" />
        <div class="flex justify-end gap-2 pt-2">
          <button type="button" hlmBtn variant="outline" (click)="formOpen.set(false)">
            {{ 'common.cancel' | translate }}
          </button>
          <button hlmBtn><ng-icon name="lucideSave" />{{ 'common.save' | translate }}</button>
        </div>
      </form>
    </app-modal>

    <!-- A category in use: what was in it moves to another one of the same kind before it is deleted. -->
    <app-modal
      [open]="!!deleting()"
      [title]="'deletion.askCategory' | translate: { name: deleting()?.name }"
      (closed)="deleting.set(null)"
    >
      <form novalidate class="space-y-3" (submit)="$event.preventDefault(); confirmDelete()">
        <p class="text-sm">
          {{
            'deletion.categoryInUse'
              | translate
                : {
                    transactions: deletingUsage()?.transactions ?? 0,
                    recurring: deletingUsage()?.recurring ?? 0,
                  }
          }}
        </p>
        <div>
          <label class="label" for="cat-move">{{ 'deletion.moveTo' | translate }}</label>
          <app-select
            inputId="cat-move"
            [options]="moveOptions()"
            [value]="moveTo() ?? ''"
            (valueChange)="moveTo.set($event || null)"
          />
        </div>
        <p class="text-muted-foreground text-xs">{{ 'deletion.categoryHint' | translate }}</p>
        <div class="flex justify-end gap-2">
          <button type="button" hlmBtn variant="outline" (click)="deleting.set(null)">
            {{ 'common.cancel' | translate }}
          </button>
          <button hlmBtn variant="destructive" [disabled]="!moveTo()">
            <ng-icon name="lucideTrash2" />{{ 'common.delete' | translate }}
          </button>
        </div>
      </form>
    </app-modal>
  `,
})
export class CategoriesComponent {
  protected readonly icons = PAGE_ICONS;
  protected readonly fs = new FormState<'name'>();
  private readonly api = inject(Api);
  private readonly events = inject(DataEvents);
  private readonly toasts = inject(Toasts);
  private readonly i18n = inject(TranslateService);
  private readonly confirm = inject(Confirm);
  private readonly prefs = inject(Prefs);
  protected readonly categories = liveResource({
    params: () => this.events.version(),
    stream: () => this.api.categories(true),
  });
  protected readonly formOpen = signal(false);
  protected readonly name = signal('');
  protected readonly type = signal<'Expense' | 'Income'>('Expense');
  protected readonly nature = signal<ExpenseNature>('Variable');
  protected readonly parentId = signal<string | null>(null);
  protected readonly color = signal('#71717a');

  protected readonly groups = computed(() => {
    const all = this.categories.value() ?? [];
    const ordered = (type: 'Expense' | 'Income') => {
      const roots = all.filter((c) => c.type === type && !c.parentId);
      return roots.flatMap((r) => [r, ...all.filter((c) => c.parentId === r.id)]);
    };
    return [
      { title: 'categories.expenses', icon: 'lucideArrowUpRight', items: ordered('Expense') },
      { title: 'categories.income', icon: 'lucideArrowDownLeft', items: ordered('Income') },
    ];
  });

  protected readonly parents = computed(() =>
    (this.categories.value() ?? []).filter(
      (c) => c.type === this.type() && !c.parentId && !c.archived,
    ),
  );
  protected readonly parentOptions = computed<SelectOption[]>(() => {
    this.prefs.translations();
    return [
      { value: '', label: '—' },
      ...this.parents().map((p) => ({ value: p.id, label: categoryLabel(this.i18n, p) })),
    ];
  });
  protected readonly natureOptions = computed<SelectOption[]>(() => {
    this.prefs.translations();
    return (['Fixed', 'Variable'] as const).map((n) => ({
      value: n,
      label: this.i18n.instant(`nature.${n}`),
    }));
  });

  protected openNew() {
    this.name.set('');
    this.parentId.set(null);
    this.fs.reset();
    this.formOpen.set(true);
  }

  protected async create() {
    if (!this.fs.check({ name: rules.required(this.name()) })) return;
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
      this.fs.fail(err);
    }
  }

  protected async setNature(c: Category, nature: ExpenseNature) {
    try {
      await firstValueFrom(
        this.api.updateCategory(c.id, {
          name: c.name,
          defaultNature: nature,
          color: c.color,
          icon: c.icon,
        }),
      );
      this.events.bump();
    } catch (err) {
      this.toasts.error(err);
    }
  }

  protected readonly renamingId = signal<string | null>(null);
  protected readonly renameValue = signal('');

  protected startRename(c: Category) {
    this.renameValue.set(categoryLabel(this.i18n, c));
    this.renamingId.set(c.id);
  }

  protected async saveRename(c: Category) {
    const name = this.renameValue().trim();
    this.renamingId.set(null);
    if (!name || name === categoryLabel(this.i18n, c)) return;
    try {
      await firstValueFrom(
        this.api.updateCategory(c.id, {
          name,
          defaultNature: c.defaultNature,
          color: c.color,
          icon: c.icon,
        }),
      );
      this.events.bump();
    } catch (err) {
      this.toasts.error(err);
    }
  }

  protected async resetName(c: Category) {
    try {
      await firstValueFrom(this.api.resetCategoryName(c.id));
      this.events.bump();
    } catch (err) {
      this.toasts.error(err);
    }
  }

  protected readonly deleting = signal<Category | null>(null);
  protected readonly deletingUsage = signal<{ transactions: number; recurring: number } | null>(
    null,
  );
  protected readonly moveTo = signal<string | null>(null);
  protected readonly moveOptions = computed<SelectOption[]>(() => {
    const c = this.deleting();
    return [
      { value: '', label: '—' },
      ...(this.categories.value() ?? [])
        .filter((o) => c && o.id !== c.id && o.type === c.type && !o.archived)
        .map((o) => ({ value: o.id, label: categoryLabel(this.i18n, o) })),
    ];
  });

  /** Deletes a custom category for good; in use, its movements move to another one first. */
  protected async remove(c: Category) {
    try {
      const usage = await firstValueFrom(this.api.categoryDeletion(c.id));
      if (usage.transactions || usage.recurring) {
        this.deletingUsage.set(usage);
        this.moveTo.set(null);
        this.deleting.set(c);
        return;
      }
      const ok = await this.confirm.ask(
        this.i18n.instant('deletion.askCategory', { name: c.name }) +
          ' ' +
          this.i18n.instant('deletion.cannotUndo'),
        { destructive: true, confirmLabel: this.i18n.instant('common.delete') },
      );
      if (!ok) return;
      await firstValueFrom(this.api.deleteCategory(c.id, null));
      this.events.bump();
      this.toasts.show(this.i18n.instant('deletion.done'));
    } catch (err) {
      this.toasts.error(err);
    }
  }

  protected async confirmDelete() {
    const c = this.deleting();
    if (!c || !this.moveTo()) return;
    try {
      await firstValueFrom(this.api.deleteCategory(c.id, this.moveTo()));
      this.deleting.set(null);
      this.events.bump();
      this.toasts.show(this.i18n.instant('deletion.done'));
    } catch (err) {
      this.toasts.error(err);
    }
  }

  protected async toggleArchive(c: Category) {
    try {
      await firstValueFrom(
        c.archived ? this.api.restoreCategory(c.id) : this.api.archiveCategory(c.id),
      );
      this.events.bump();
    } catch (err) {
      this.toasts.error(err);
    }
  }
}
