---
description: UI code follows the repo's design system: tokens only, shared components, every state designed, WCAG 2.2 AA
paths:
  - "**/*.tsx"
  - "**/*.jsx"
  - "**/*.css"
  - "**/*.scss"
  - "**/*.html"
  - "**/components/**/*.ts"
  - "**/src/app/**/*.ts"
  - "**/DESIGN.md"
---
# Design system

**Sources, in order of authority:** the product rules (`docs/design-system.md` or what `AGENTS.md` names), then
`DESIGN.md` (tokens and patterns as built), then the token values in the theme CSS. A design skill never overrides them.

## Tokens only

- Colours, spacing, radius, shadows, type sizes and motion durations come from tokens (CSS variables exposed as
  utilities). No raw hex/rgb/hsl/oklch, no arbitrary utility values, no inline `style` colours.
- Semantic colours (success, warning, critical) are separate from the accent; meaning never relies on colour alone.
- Light and dark themes come from the same tokens.
- A new token is added to the theme and to `DESIGN.md` in the same change, never invented inside a component.

## Components

- Reuse the shared UI package before adding anything; extend a shared component instead of copying it.
- No new UI library without an ADR.
- Components accept `className` and merge classes with the repo's `cn()` helper.
- **Icons:** one icon set per repo, chosen in the repo's design-system doc (with its licence and weight/size rules).
  Use only that set; no emoji as icons, no second set mixed in.
- **Chat and agent surfaces use the chat library's own components** (CopilotKit in most repos): before designing
  them, read what the installed version ships and its styling hooks (the `copilotkit` skill; the installed
  `node_modules/@copilotkit`), and style those components with the repo's tokens. Never draw a parallel chat UI next
  to the library's.

## Approved drafts

When the feature has approved drafts (`design.md`, wireframes, HTML drafts), the UI matches their screens, states,
microcopy and accessibility notes. A difference is a question for the owner, not a decision for the implementer.

## Design files and skills

- Impeccable owns `PRODUCT.md` (users, purpose, tone; `/impeccable init`) and `DESIGN.md` (the visual system;
  `/impeccable document`, refreshed in the same PR as a visual change). No `design-system/` folder. Telemetry off
  (`DO_NOT_TRACK=1`).
- Review a screen by running it: light and dark, 375 px and 1440 px, keyboard only; fix by severity and show before
  and after screenshots. `impeccable` critiques and polishes, `frontend-design` sets a new screen's direction,
  `ui-ux-pro-max` looks things up (never `--persist`), `emil-design-eng` and `review-animations` check motion,
  `break-ui` stresses the screen, the `design-system-guard` agent checks a UI diff.
- Vendored skills are pinned in Armature's `vendor.lock.json`; never edit them in the repo.

## Every state is designed

Empty, loading, partial, error, no permission and stale. Long text, huge lists and narrow widths do not break the
layout (check with the `break-ui` skill when installed).

## Accessibility: WCAG 2.2 AA

Full keyboard use with visible focus, labelled controls, accessible names on icon buttons, contrast AA, focus trapped
in dialogs and returned on close, `prefers-reduced-motion` respected.

## Done means looked at

A visual change is finished only after it was viewed in a browser at a phone and a desktop width, light and dark
(the `visual-check` skill when installed), and `DESIGN.md` was refreshed if tokens or patterns changed.

<!-- Sources: the owner's conventions from ordo-frontend .claude/agents/design-system-guard.md and the AI-89 "Design work" rules (design system outranks skills, every state a deliverable, WCAG 2.2 AA, tokens never hard-coded, DESIGN.md refreshed with visual changes); affaan-m/ECC rules/web/coding-style.md (CSS custom properties, MIT) at d2ef268. -->
