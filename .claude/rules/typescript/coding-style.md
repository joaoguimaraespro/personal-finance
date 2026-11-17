---
description: TypeScript and JavaScript coding style: strict types, no any, immutability, errors, validation at the boundary, Biome
paths:
  - "**/*.ts"
  - "**/*.tsx"
  - "**/*.mts"
  - "**/*.cts"
  - "**/*.js"
  - "**/*.jsx"
  - "**/*.mjs"
---
# TypeScript coding style

## Compiler and tooling

- `strict: true` in every `tsconfig`, plus `noUncheckedIndexedAccess` and `noImplicitOverride` in new projects. Never
  turn a strict flag off to make a build pass.
- One formatter and linter per repo, run by the gate. Default: **Biome** (shared config package in a monorepo).
  Follow the repo's choice when it already uses Prettier/ESLint; never add a second one.
- Suppress a lint finding only on the line, with the tool's own syntax and a reason
  (`// biome-ignore lint/<group>/<rule>: <reason>`). No file-wide disables.
- **Banned APIs** (Armature template `plugins/stack-typescript/templates/biome.banned.json`, adopted with
  `"extends": ["./biome.banned.json"]` in the repo's `biome.json`): `any`, `console`, timers (`setTimeout`,
  `setInterval`, `requestAnimationFrame`, `requestIdleCallback`) and React `useEffect`/`useLayoutEffect` are errors
  in product code, each with a message that names the fix. Code whose job is timing or syncing with an outside
  system lives in the allow-listed folder (`src/lib/platform/`), as do tests and scripts. With ESLint the same list is
  `no-restricted-globals`, `no-restricted-imports`, `no-console` and `@typescript-eslint/no-explicit-any`. Add an
  entry when a review keeps correcting the same call (the `review-lessons` skill); never remove one to pass the gate.
- Use the repo's package manager (lockfile decides: `bun.lock`, `pnpm-lock.yaml`, `package-lock.json`). Never mix.

## Types

- Exported functions, shared utilities and public methods have explicit parameter and return types. Let TypeScript
  infer obvious locals.
- `interface` for object shapes that may be extended; `type` for unions, intersections, tuples, mapped and utility
  types. Prefer string literal unions to `enum`.
- No `any` in application code. Use `unknown` for external input and narrow it; use generics when the type depends on
  the caller.
- No `as` casts that hide a type error and no non-null `!` without a guard on the line above. `satisfies` is the
  way to check a literal against a type without widening it.
- Type-only imports use `import type`.
- In `.js` files that cannot move to TypeScript, use JSDoc types that match runtime behaviour.

```typescript
// WRONG
export function getErrorMessage(error: any) {
  return error.message
}

// CORRECT
export function getErrorMessage(error: unknown): string {
  return error instanceof Error ? error.message : 'Unexpected error'
}
```

## Immutability

Return new objects instead of mutating arguments; mark inputs `Readonly<T>` when it helps.

```typescript
function rename(user: Readonly<User>, name: string): User {
  return { ...user, name }
}
```

## Errors

- `async`/`await` with `try`/`catch`; the caught value is `unknown` and is narrowed before use.
- Never swallow an error: log it with context and rethrow, or turn it into a typed result the caller must handle.
- No floating promises: await them, return them, or mark fire-and-forget explicitly with `void` and a comment.

## Validation at the boundary

Everything that crosses a trust boundary (HTTP bodies, query strings, env vars, `localStorage`, messages from a model
or a tool) is parsed with a schema (Zod or the repo's equivalent) and the type is inferred from the schema.

```typescript
const UserInput = z.object({ email: z.string().email(), age: z.number().int().min(0).max(150) })
type UserInput = z.infer<typeof UserInput>
const input: UserInput = UserInput.parse(body)
```

## Logging

No `console.log` in committed code. Use the repo's logger; never log tokens, secrets or personal data.

## Naming

- Files: kebab-case (`user-card.tsx`) unless the repo's convention differs; one exported component or concept per file.
- Symbols: `PascalCase` types and components, `camelCase` values and functions, `UPPER_SNAKE` for true constants.
- Booleans read as questions: `isLoading`, `hasError`, `canSubmit`.

<!-- Adapted from affaan-m/ECC rules/typescript/coding-style.md and hooks.md (MIT) at d2ef268; merged with the owner's frontend conventions (ordo-frontend CLAUDE.md: strict TS, Biome, bun; personal-finance-sdd typescript-reviewer priorities). -->
