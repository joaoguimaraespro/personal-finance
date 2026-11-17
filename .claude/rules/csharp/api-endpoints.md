---
description: HTTP endpoint conventions for .NET APIs: minimal APIs, routes, verbs, status codes, ProblemDetails, pagination, tenancy, and what never to do
paths:
  - "**/*.Api/**/*.cs"
  - "**/*Endpoint*.cs"
  - "**/Endpoints/**/*.cs"
  - "**/BuildingBlocks/Web/**/*.cs"
  - "**/contracts/*api*.md"
---
# API endpoints

Every .NET API follows this; the repo's `contracts/api-conventions.md` (from Armature's template) is the contract the
endpoints are tested against.

## Shape
- **Minimal APIs**, one endpoint per use case: `internal sealed class Create<Entity>Endpoint : IEndpoint` with
  `MapEndpoint(IEndpointRouteBuilder)`; the module's `Map<Module>Endpoints` maps them under the versioned group.
- The endpoint only: binds the request, sends one command or query through `ISender`, turns the `Result` into HTTP.
  No business logic, no `DbContext`, no repository in an endpoint.
- `TypedResults` with an explicit return union (`Results<Ok<T>, ProblemHttpResult>`), `.WithName`, `.WithTags`,
  `.Produces`/`.ProducesProblem` so OpenAPI is right.

## Routes
- `/api/v{version}/<resources>`: plural, kebab-case nouns (`/api/v1/production-orders`), ids as path segments
  (`/api/v1/production-orders/{id}`), sub-resources one level deep (`/lots/{id}/quality-results`).
- Actions that are not CRUD are a sub-resource with POST (`POST /api/v1/mappings/{id}/approval`), never a verb in a
  query string.
- Versioning in the URL segment; a breaking change is a new version, never a change in place.
- Public ids only (`ExternalId`/GUID or the source code); never an internal database key.

## Verbs and status
| Verb | Success | Body |
|---|---|---|
| GET one | 200 | the resource |
| GET list | 200 | `PagedResult<T>`: `items, page, pageSize, totalCount, totalPages, hasNextPage, hasPreviousPage` |
| POST create | 201 with `Location` | the created resource (or its id) |
| POST action | 200 or 202 (async work, with a status URL) | the result |
| PUT / PATCH | 200 | the updated resource |
| DELETE | 204 | none |

Lists are always paginated (`page` from 1, `pageSize` default 20, max 100), with a stable sort (`sort=field,-other`).
Filters are named query parameters (`?status=open&from=2026-01-01`), validated like a body.

## Errors: ProblemDetails (RFC 9457)
One shape for every error, `application/problem+json`, made in one place (`BuildingBlocks/Web`):

```json
{ "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5", "title": "Production order not found",
  "status": 404, "detail": "No production order with this id in your organisation.",
  "instance": "/api/v1/production-orders/7c1…", "code": "production_order_not_found" }
```

- `code` is a stable snake_case extension the client switches on; `title` and `detail` are fixed text for people,
  never built from input or an exception message.
- Validation adds `errors: [{ "field": "quantity", "code": "must_be_positive", "detail": "…" }]`.
- `ErrorType` → status: Validation 400, Problem 400, Unauthorized 401, Forbidden 403, NotFound 404, Conflict 409,
  Failure 500 (generic title, nothing internal). The `X-Request-Id` header, not the body, carries the request id.

## Tenancy and auth
- `RequireAuthorization()` on the version group; anonymous endpoints are listed by name (health, OpenAPI in dev).
- The tenant comes only from the token's `organization` claim. A route with `{org}` must match it (403
  `tenant_mismatch`). Another tenant's resource answers **404**, never 403 (no existence leak).
- Roles and features through policies (`.RequireAuthorization("plant-ops")`), not checks inside handlers.

## Never
- Never put business rules, queries or `SaveChanges` in an endpoint.
- Never return an entity: always a response record.
- Never take the tenant from a header, query string, cookie or body.
- Never return 200 with an error inside the body, or 500 for a business rule.
- Never put an exception message, stack trace, SQL or another tenant's data in an error.
- Never accept unbounded lists, `pageSize` above the maximum, or free-text sort/filter expressions.
- Never break a published route or field; add a new version.
- Never use verbs in paths (`/getOrders`, `/createLot`), trailing slashes, or file extensions.
- Never block on async (`.Result`, `.Wait()`, `GetAwaiter().GetResult()`).
