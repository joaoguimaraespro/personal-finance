import { ChangeDetectionStrategy, Component, input, signal } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { problemMessage } from '../core/toast';

/** A form's errors by field: an i18n key ("validation.…") or a server message per field that is not valid. */
export type FieldErrors<F extends string = string> = Partial<Record<F, string>>;

/**
 * What a failed save says, for the dialog itself (a toast would sit behind it): the server's field errors
 * (ProblemDetails `errors`, keyed like "Name" or "Amount") by lower-camel field, and one message for the form.
 */
export function serverErrors(err: unknown): { fields: Record<string, string>; message: string } {
  const errors = (err as { error?: { errors?: Record<string, string[]> } })?.error?.errors ?? {};
  const fields: Record<string, string> = {};
  for (const [key, messages] of Object.entries(errors)) {
    const name = key.split('.').pop() ?? key;
    fields[name.charAt(0).toLowerCase() + name.slice(1)] = messages[0] ?? '';
  }
  return { fields, message: problemMessage(err) };
}

/**
 * The validation state of one form in a dialog: field errors shown under each field, and a form-level message for
 * what the server refused. `check` runs before saving; `fail` takes a failed save.
 */
export class FormState<F extends string = string> {
  readonly errors = signal<FieldErrors<F>>({});
  readonly message = signal<string | null>(null);

  reset() {
    this.errors.set({});
    this.message.set(null);
  }

  /** Shows these errors; true when there are none (the form can be saved). */
  check(errors: FieldErrors<F>): boolean {
    const found = Object.fromEntries(
      Object.entries(errors).filter(([, e]) => !!e),
    ) as FieldErrors<F>;
    this.message.set(null);
    this.errors.set(found);
    return Object.keys(found).length === 0;
  }

  /** A save the server refused: its field errors under the fields, its message in the form. */
  fail(err: unknown) {
    const { fields, message } = serverErrors(err);
    this.errors.set(fields as FieldErrors<F>);
    // Shown once: under its field when the server named one, otherwise for the whole form.
    this.message.set(Object.keys(fields).length ? null : message);
  }

  /** Clears one field's error as soon as it is edited. */
  touch(field: F) {
    if (this.errors()[field]) this.errors.update((e) => ({ ...e, [field]: undefined }));
  }
}

/** The message under a field: a "validation.…" key is translated, a server message is shown as it is. */
@Component({
  selector: 'app-field-error',
  imports: [TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `@if (error(); as e) {
    <p class="field-error" role="alert">
      {{ e.startsWith('validation.') ? (e | translate: params()) : e }}
    </p>
  }`,
})
export class FieldErrorComponent {
  readonly error = input<string | undefined | null>(null);
  readonly params = input<Record<string, unknown>>({});
}

/** What the server refused, inside the form (above its buttons). */
@Component({
  selector: 'app-form-alert',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `@if (message(); as m) {
    <p class="form-alert" role="alert">{{ m }}</p>
  }`,
})
export class FormAlertComponent {
  readonly message = input<string | null>(null);
}

export const FORM_ERRORS = [FieldErrorComponent, FormAlertComponent] as const;

/** Common checks; each returns the i18n key when the value is not valid. */
export const rules = {
  required: (text: string | null | undefined) => (text?.trim() ? undefined : 'validation.required'),
  positive: (value: number | null) =>
    value === null ? 'validation.number' : value > 0 ? undefined : 'validation.positive',
  /** Optional, but a number when given. */
  number: (text: string, value: number | null) =>
    text.trim() && value === null ? 'validation.number' : undefined,
  pick: (value: string | null | undefined) => (value ? undefined : 'validation.pick'),
};
