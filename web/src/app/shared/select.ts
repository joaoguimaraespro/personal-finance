import {
  ChangeDetectionStrategy,
  Component,
  booleanAttribute,
  computed,
  input,
  output,
  viewChild,
} from '@angular/core';
import { BrnSelect, provideBrnSelectConfig } from '@spartan-ng/brain/select';
import { HlmSelectImports } from '@spartan-ng/helm/select';
import type { ClassValue } from 'clsx';

let nextId = 0;

export interface SelectOption {
  value: string;
  label: string;
  disabled?: boolean;
}

/**
 * Themed single select (spartan/ui hlm-select on the CDK overlay) with a <select>-like API:
 * string values, an options list, [value] in and (valueChange) out. Replaces the native <select>,
 * whose option popup is drawn by the browser/OS and ignores the dark theme.
 * An option with value '' behaves like a native "none/all" option.
 */
@Component({
  selector: 'app-select',
  imports: [HlmSelectImports],
  // '' is a real choice here (like <option value="">), not "nothing selected".
  providers: [
    provideBrnSelectConfig({ isSingleValuePresent: (v) => v !== null && v !== undefined }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'block min-w-0' },
  template: `
    <hlm-select
      [value]="value()"
      (valueChange)="pick($event)"
      [disabled]="disabled()"
      [itemToString]="labelOf()"
    >
      <hlm-select-trigger
        [buttonId]="inputId()"
        [size]="size()"
        [class]="triggerClass()"
        [attr.aria-label]="ariaLabel() || null"
      >
        <hlm-select-value [placeholder]="placeholder()" />
      </hlm-select-trigger>
      <hlm-select-content *hlmSelectPortal class="max-h-80">
        @for (o of options(); track o.value) {
          <hlm-select-item [value]="o.value" [disabled]="!!o.disabled">{{
            o.label
          }}</hlm-select-item>
        }
      </hlm-select-content>
    </hlm-select>
  `,
})
export class SelectComponent {
  readonly options = input.required<SelectOption[]>();
  readonly value = input<string | null>('');
  readonly disabled = input(false, { transform: booleanAttribute });
  /** Id of the trigger button, so a <label for> points at it. */
  readonly inputId = input(`app-select-${nextId++}`);
  readonly ariaLabel = input('');
  readonly placeholder = input('');
  readonly size = input<'default' | 'sm'>('default');
  readonly userClass = input<ClassValue>('', { alias: 'triggerClass' });
  /** Action pickers ("+ Add …"): emit the choice, then snap back to [value]. */
  readonly resetOnPick = input(false, { transform: booleanAttribute });
  readonly valueChange = output<string>();

  private readonly select = viewChild.required(BrnSelect);

  protected readonly triggerClass = computed(() => ['w-full', this.userClass()]);
  protected readonly labelOf = computed(() => {
    const labels = new Map(this.options().map((o) => [o.value, o.label]));
    return (value: string) => labels.get(value) ?? '';
  });

  private resetting = false;

  protected pick(value: string | null | undefined) {
    if (this.resetting) return;
    this.valueChange.emit(value ?? '');
    if (!this.resetOnPick()) return;
    // Writing the model re-emits valueChange; swallow that echo.
    this.resetting = true;
    this.select().writeValue(this.value());
    this.resetting = false;
  }
}
