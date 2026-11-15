---
description: Angular coding style: standalone components, signals, inject(), OnPush, block control flow, templates, forms, styles
paths:
  - "**/src/app/**/*.ts"
  - "**/src/app/**/*.html"
  - "**/*.component.ts"
  - "**/*.component.html"
  - "**/*.service.ts"
  - "**/*.directive.ts"
  - "**/*.pipe.ts"
  - "**/*.guard.ts"
  - "**/angular.json"
---
# Angular coding style

## Version first

Check the installed Angular version (`package.json`, `ng version`) before writing code: APIs differ between majors.
Use the CLI's conventions for that version, including its file naming (recent versions drop the `.component` suffix;
follow what the repo already does). After a change, the build and tests in the gate must pass.

## Components

- Standalone components only; `ChangeDetectionStrategy.OnPush` on every component (or zoneless, as configured).
- Signal APIs: `input()` / `input.required()`, `output()`, `model()`, `viewChild()`. No `@Input()`/`@Output()`
  decorators in new code.
- `inject()` for dependencies; no constructor injection. `InjectionToken` for non-class values.
- One component, service or pipe per file; feature folders (`features/<feature>/`), shared UI in `shared/` or the
  repo's `ui/` folder, cross-cutting services in `core/`.

## Signals

- `signal` for local state, `computed` for derived values, `linkedSignal` for writable state that resets when its
  source changes.
- `resource()` / `rxResource()` for async data into signals; no manual `subscribe` to load data.
- `effect()` only for side effects outside Angular (logging, third-party DOM, storage). Never to copy one signal into
  another: use `computed` or `linkedSignal`.
- Observables at the edges (HTTP, router events); convert with `toSignal` / `rxResource`. Any manual subscription uses
  `takeUntilDestroyed()`.

## Templates

- Block control flow: `@if`, `@for (x of xs(); track x.id)`, `@switch`, `@defer` for heavy below-the-fold parts.
  `track` always names a stable id.
- No logic in templates beyond simple conditions; computed signals or pure pipes instead of method calls.
- Every user-visible string goes through the repo's i18n pipe or `$localize`, with keys added to every language
  file in the same change.
- Formatting of money, dates and numbers through the repo's shared pipes, never ad hoc in the template.

## Forms

Follow the repo's choice: Signal Forms (v21+), typed Reactive Forms for complex forms, template-driven only for
trivial ones. Errors appear next to the field.

## Styles

- Component styles stay encapsulated; `:host` for the host element; no `ViewEncapsulation.None` outside a design
  system.
- Tailwind utilities and CSS custom properties from the theme; no raw colours (see the design rule if installed).

## Formatting

Prettier (or the repo's formatter) is part of the gate; run its check before finishing.

<!-- Adapted from affaan-m/ECC rules/angular/coding-style.md (MIT) at d2ef268; merged with the owner's conventions from personal-finance-sdd (Angular 22, standalone + OnPush + inject(), signals and rxResource, ngx-translate keys in every language file, shared money/date pipes, Prettier in the gate). Ordo's ECC copy has no Angular rules. -->
