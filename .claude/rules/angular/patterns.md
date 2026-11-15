---
description: Angular patterns: smart and presentational components, API service, resources, interceptors, routing and guards, overlays, accessibility
paths:
  - "**/src/app/**/*.ts"
  - "**/src/app/**/*.html"
  - "**/*.component.ts"
  - "**/*.service.ts"
  - "**/*.interceptor.ts"
  - "**/*.routes.ts"
---
# Angular patterns

## Components

- Pages (smart) own data loading and mutations; presentational components take inputs, emit outputs and hold only
  UI state.
- Shared building blocks (page header, empty state, status badge, modal, select, date field) live once in `shared/`
  and are reused; a second copy is a review finding.
- Every list view handles loading, empty and error states explicitly; a reload keeps the previous data on screen
  and shows a quiet indicator instead of blanking.

## Data access

- One API service in `core/` wraps `HttpClient` and returns typed results; components never build URLs.
- Use `HttpClient` (with `withFetch()`), never raw `fetch`, so interceptors apply.
- The backend computes business figures; the UI only displays them.

## Interceptors (functional, `withInterceptors([...])`)

- Session expiry: a 401 sends the user to sign in and back to the same page.
- Timeouts on API calls, longer for uploads, with a clear message.
- XSRF: `withXsrfConfiguration` when the API issues an XSRF cookie.
- Interceptors never log request or response bodies.

## Routing

- Lazy-load features with `loadComponent` / `loadChildren`; `withComponentInputBinding()` binds route params to
  inputs.
- Functional guards; `canMatch` for routes a user may not load at all. Hiding a link is not access control.

## Overlays

CDK overlays (dialogs, selects, menus) render in the browser top layer by default in recent CDK versions, which hides
toasts behind open dialogs. Provide `{ provide: OVERLAY_DEFAULT_CONFIG, useValue: { usePopover: false } }` once in the
app config when the app shows toasts, and show a form's errors inside its dialog.

## RxJS

`switchMap` for "latest wins" (search), `concatMap` for ordered writes, `exhaustMap` for submit buttons; no nested
subscribes.

## Accessibility

Semantic elements, labelled controls, focus moved into dialogs and back on close, keyboard paths for every action,
visible focus.

<!-- Adapted from affaan-m/ECC rules/angular/patterns.md (MIT) at d2ef268; merged with the owner's conventions from personal-finance-sdd (core/api service, functional interceptors for 401, timeouts and XSRF, shared page components, liveResource keeping data on reload, backend computes figures) and NEXUS knowledge/angular/cdk-overlay-top-layer.md. -->
