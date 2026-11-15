---
description: Angular testing: the repo's runner (Vitest), TestBed with standalone components, signal inputs, HTTP testing, Playwright e2e
paths:
  - "**/*.spec.ts"
  - "**/*.test.ts"
  - "**/e2e/**/*.ts"
  - "**/playwright.config.*"
---
# Angular testing

- Test-first from the acceptance criteria. Use the runner the repo configures (`ng test --watch=false`; Vitest in new
  projects).
- Standalone components: `TestBed.configureTestingModule({ imports: [TheComponent] })`; set signal inputs with
  `fixture.componentRef.setInput('name', value)`.
- Test through the rendered DOM and public API: what the user sees and does, not private fields. CDK harnesses for
  library widgets.
- HTTP: `provideHttpClient(), provideHttpClientTesting()`, `HttpTestingController`, and `httpMock.verify()` after each
  test.
- Services and pure functions are tested without a fixture; pipes as plain functions.
- Router-dependent components with `RouterTestingHarness`.
- Every new UI string: a test (or the build) fails when a key is missing from a language file.
- Playwright e2e for the critical flows, on synthetic data, at a phone and a desktop width.
- Never skip, delete or weaken a test to pass the gate.

<!-- Adapted from affaan-m/ECC rules/angular/testing.md (MIT) at d2ef268; merged with the owner's conventions (personal-finance-sdd: Vitest via ng test, Playwright e2e, synthetic data, i18n keys in every language file). -->
