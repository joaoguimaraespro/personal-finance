# ADR-0007: spartan/ui for UI components

- Status: Accepted
- Date: 2026-09-26

## Context
The first UI used hand-written Tailwind components. Forms, dialogs, selects, date pickers and tables
needed a consistent, accessible component set. PrimeNG was evaluated first: from v20 it is distributed
under the commercial "PrimeUI License" — free for individuals only with a license key renewed yearly, a
visible banner without one, and no open-source release compatible with Angular 22. That does not fit a
public MIT repository that anyone should be able to clone and run.

## Decision
- Use **spartan/ui** (MIT): headless "brain" primitives on the Angular CDK (focus trap, overlays,
  keyboard handling, ARIA) plus shadcn-style "helm" components generated into `web/src/app/ui` and owned
  by this repo.
- Theme with shadcn CSS variables in `styles.css`: neutral zinc surfaces, emerald primary, light/dark.
- Keep native `<select>` (styled by a small `uiSelect` directive) where the platform picker is the better
  mobile experience; use the spartan calendar for dates and months.
- Icons: lucide via `@ng-icons`.

## Consequences
- Fully open source, no license keys, same CSP (no runtime style injection beyond what Angular already needs).
- Generated components are our code: upgrades are deliberate (re-generate with `@spartan-ng/cli`, which
  is installed only temporarily because it pulls in Nx).
- Richer widgets (e.g. data grid) are composed from primitives rather than bought off the shelf.
