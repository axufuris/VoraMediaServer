# Copilot instructions for Vora

Vora is a self-hosted media server: a .NET 10 backend (`Vora.Api`) and a React + TypeScript SPA (`Vora.Web`) over PostgreSQL with `pgvector`. `docs/` holds the subsystem guides; `CLAUDE.md` is the fuller version of what follows.

When reviewing a pull request, prefer a small number of findings that would actually break something or violate a rule below over a long list of style observations. If a change looks wrong but the surrounding code explains why it is right, say nothing.

## Rules that are not negotiable

**Backend (C#)**

- Nullable reference types are on. **Never** suppress with `!` or `#pragma` — flag any new suppression and ask for the null path to be handled instead.
- The C# codebase is intentionally **comment-free**, including XML doc comments. Explanatory comments belong only where they record *why* a non-obvious decision was made, not what the code does. Flag commentary that restates the code.
- Async methods end in `Async`.
- `Vora.Domain` depends on nothing else in the solution. `Vora.Application` depends only on `Vora.Domain` and `Vora.Plugins`. Repository *implementations* live in `Vora.Infrastructure`. Flag any dependency that crosses those lines.
- Endpoints stay thin: parse → call a manager/service → return. Logic belongs in `Vora.Application`.
- Claims access goes through `Vora.Api/Extensions/AuthExtensions.cs` (`GetProfileId`, `GetAccountId`, …). Flag any direct `user.FindFirst("…")`.
- API responses return `*VM` / Response types from `Vora.Application`. **Never** expose a `Vora.Domain` entity or a `Vora.Plugins` DTO through an endpoint — a plugin DTO on the wire ends up in the OpenAPI document and therefore in every generated native client.
- HTTP clients come from `IHttpClientFactory`. No static `HttpClient`.
- DI registration goes in the matching helper in `ServiceRegistrationExtensions.cs`, never inline in `Program.cs`.
- **Never edit a checked-in migration.** A schema change needs a new one.
- **Never give a credential a default value on an entity.** `ServerSetting` once carried live API keys as property initializers; because the entity is seeded with `HasData`, those literals were baked into every migration snapshot and could not be removed without regenerating the whole history.

**Frontend (TypeScript / React)**

- No `any`, no `unknown` left in the code.
- No `alert()` / `confirm()` / `prompt()` — use `useDialog()`.
- All API calls go through the per-domain services under `src/api/<Domain>/`. Pages must not call axios directly.
- **Colours come from tokens**, never the Tailwind palette: `var(--vora-bg-canvas)`, `var(--vora-accent-500)` — not `bg-gray-900`, `text-white`.
- **Card and row sizes come from tokens too** (`--vora-card-w-*`, `--vora-card-gap`, …). Never size a tile in `px`.
- One tile, one row, one hero: `MediaCard`, `MediaRow`, `MediaGrid`, `DetailHero`. Flag a hand-rolled card or scroller.
- Modals overlay the header — raw `fixed inset-0` overlays need `z-[200]` or higher.
- eslint must be **0 errors and 0 warnings**.

## Things that have actually gone wrong here

Weight these highly; each is a real regression this project has shipped or nearly shipped.

- **`FindAsync(id, token)` silently binds to the `params object?[]` overload**, making the cancellation token part of a composite key lookup. It compiles and returns nothing. Correct form is `FindAsync([id], token)`.
- **Two or more sibling collection `Include`s in one query is a cartesian product.** Flag a multi-collection include without `.AsSplitQuery()`.
- **Optional plugin settings must not gate anything.** Only a setting declared `Required` can make a plugin count as unconfigured.
- **Minimal API enum query binding is case-sensitive** — `?sort=alphabetical` 400s where `Alphabetical` binds.
- **Times on the wire are UTC** (`UtcDateTimeConverter`), and a calendar date is a `DateOnly` that must not shift across a timezone. Flag a `DateTime` used for a date-only concept.
- **React Compiler lint rules are enforced**: no `setState` in an effect body, no ref writes during render, no `Date.now()` during render. Prefer derived state over an effect that syncs one piece of state to another.
- **A feature flag that can be switched on but unusable should be derived** (`toggle && configured`), with the raw toggle exposed separately for the admin switch — otherwise a client shows a nav entry that opens an empty page.

## Tests

- The suite must pass; `dotnet test Vora.slnx` and `npm run test:run` both run in PR checks.
- **Be suspicious of a changed assertion.** If a PR modifies an existing test so it passes, that is worth a comment unless the PR explains why the old expectation was wrong.
- The Infrastructure tests use the EF **in-memory** provider, which does not implement split queries or real SQL semantics. A green suite does not prove a relational query works — flag new repository query logic that has no coverage against a real provider.

## Out of scope

Do not comment on: generated migration files under `src/Vora.Infrastructure/Migrations/`, `package-lock.json`, or formatting that eslint and the compiler already enforce.
