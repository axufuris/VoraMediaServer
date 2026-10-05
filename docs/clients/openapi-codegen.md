# OpenAPI codegen for native clients

Every native client (Android, Apple, future Roku) consumes a generated typed API client built from `Vora.Api`'s OpenAPI document. The backend is the single source of truth; client SDKs are regenerated on demand. No hand-maintained DTOs, no manually-typed endpoints.

This file covers: how the OpenAPI doc is produced and how to harden it for codegen. The commands for generating the Swift and Kotlin clients are in [`docs/clients/generated-clients.md`](generated-clients.md). Why the clients are native per ecosystem is in [`docs/architecture.md`](../architecture.md).

## How `Vora.Api` exposes the OpenAPI document

`AddVoraSwagger()` (in `Vora.Api/Extensions/ServiceRegistrationExtensions.cs`) wires up Swashbuckle:

```csharp
private static IServiceCollection AddVoraSwagger(this IServiceCollection services)
{
    services.AddEndpointsApiExplorer();
    services.AddSwaggerGen();
    return services;
}
```

`UseVoraPipeline()` mounts `UseSwagger()` and `UseSwaggerUI()` only when `app.Environment.IsDevelopment()`. The doc is served at `/swagger/v1/swagger.json`; the UI at `/`.

That's sufficient for now: codegen runs against a development instance of `Vora.Api`. Production builds don't need the Swagger UI mounted.

## Hardening Swashbuckle for codegen

The default `AddSwaggerGen()` call produces an OpenAPI document that *works* for codegen but generates ugly client method names and loses some type fidelity. The recommended hardening (do this before scaling out to a second client) is to:

### 0. Keep `Vora.Plugins` types out of the schema

Every `.Produces<T>()` must name a `*VM` / `*Response` from `Vora.Application`. A `Vora.Plugins` DTO returned directly gets pulled into the OpenAPI document, and from there into every generated Swift/Kotlin client — so plugin-assembly types become part of the public client surface and a plugin-side refactor silently breaks native builds.

This is the project-wide View Model rule (see `docs/backend-conventions.md`), but codegen is what makes violating it expensive. `GET /api/discovery/details/{providerId}/{type}/{externalId}` used to return `DiscoveryItemDetailsDto` straight from `Vora.Plugins`; it now returns `DiscoveryItemDetailsVM`. When adding an endpoint, check the `.Produces<>` type's namespace.

### 0.5 Keep `[Flags]` enums off the wire

`JsonStringEnumConverter` serializes a combined `[Flags]` value as a comma-joined
string — `"Actor, Producer"` — but an OpenAPI enum schema describes a single
value. A strictly-typed generated client (Android via kotlinx-serialization)
throws when it meets one, and only for the subset of records that happen to have
a combined value, so it looks like bad data rather than a contract bug.

`MediaCastRole` is the only such enum reaching view models. Every VM that carries
it marks it `[JsonIgnore]` and exposes the formatted `Role` string instead
(`CastMemberVM`, `ActorRoleVM`). `CastRoleSerializationTests` fails the build if a
new VM exposes one.

### 1. Set explicit operation IDs

Without explicit IDs, Swashbuckle generates names like `GetApiUsersById` from verb + route. With explicit IDs, generated method names match what the endpoint actually does.

Add to `Vora.Api/Extensions/ServiceRegistrationExtensions.cs`:

```csharp
private static IServiceCollection AddVoraSwagger(this IServiceCollection services)
{
    services.AddEndpointsApiExplorer();
    services.AddSwaggerGen(options =>
    {
        options.CustomOperationIds(api =>
        {
            // Endpoint name set via .WithName("...") on each endpoint definition.
            // Falls back to "{Controller}_{Action}" shape when not set.
            return api.ActionDescriptor.AttributeRouteInfo?.Name
                ?? $"{api.ActionDescriptor.RouteValues["controller"]}_{api.ActionDescriptor.RouteValues["action"]}";
        });
    });
    return services;
}
```

Then, on each endpoint mapped in `Vora.Api/Endpoints/*Endpoints.cs`, add `.WithName("GetUserById")` (or similar). Existing `.WithName` calls already serve as operation IDs once the resolver above is in place.

The shape we want: PascalCase verb + noun (`ListUsers`, `GetUserById`, `CreateUser`, `UpdateUser`, `DeleteUser`, `ListLibraryItems`, `StartPlaybackSession`, …). These translate cleanly to Swift method names (`client.listUsers()`) and Kotlin method names (`client.listUsers()`).

### 2. Verify enum serialization is string

`AddVoraJsonOptions` registers `JsonStringEnumConverter`, so enums serialize as strings at the HTTP boundary. Confirm the OpenAPI schema reflects this by checking a sample endpoint that returns an enum — the schema should show `"type": "string", "enum": ["FOO", "BAR"]`, not integer values. If it shows integers, add to `AddSwaggerGen`:

```csharp
options.UseInlineDefinitionsForEnums();
options.MapType<SomeEnum>(() => new OpenApiSchema { Type = "string", Enum = ... });
```

Usually not needed because the JSON converter is global, but verify.

### 3. Document ProblemDetails as the error shape

Standard ASP.NET Core problem-details responses (RFC 7807) are emitted by `UseExceptionHandler()` + `UseStatusCodePages()`. The OpenAPI doc should declare these as error response shapes on every endpoint. Add to `AddSwaggerGen`:

```csharp
options.SupportNonNullableReferenceTypes();
options.IncludeXmlComments(...);  // skip — repo is comment-free
```

Then either decorate endpoints with `.ProducesProblem(StatusCodes.Status400BadRequest)` etc., or add a document filter that adds problem-details responses to every endpoint. The latter is less invasive.

### 4. Verify the schema with a dry-run

After the hardening, fetch the doc and run a no-op codegen to confirm everything generates cleanly:

```bash
curl http://localhost:8080/swagger/v1/swagger.json > /tmp/vora-openapi.json
# Validate it's well-formed:
npx @apidevtools/swagger-cli validate /tmp/vora-openapi.json
```

If `swagger-cli` reports errors (orphan refs, missing schemas, anonymous types without IDs), fix them in `Vora.Api` before scaling out to native client generation.

Generating the Swift and Kotlin clients, the auth middleware they need, and multi-server support are in [`docs/clients/generated-clients.md`](generated-clients.md).

## Things to watch out for

- **Anonymous response types.** Minimal API endpoints that return `TypedResults.Ok(new { foo, bar })` produce inline anonymous schemas with no name. Generators turn these into ugly `InlineObject1` types. Either return a named record (`*VM` or `*Response`) or add `WithOpenApi(op => op.OperationId = ...)` to give the response a stable name.
- **Polymorphic results.** `Results<Ok<UserVM>, NotFound, ProblemDetails>` is correctly emitted, but `swift-openapi-generator` and the Kotlin generator handle the union case differently. Test before relying on polymorphic returns; prefer single-typed return + status code variation where possible.
- **DateTime handling.** Backend uses `DateTimeOffset`. Generators produce `Date` / `ZonedDateTime` types. Add adapters if you need a custom parser.
- **Nullable reference types.** The `SupportNonNullableReferenceTypes()` option above is required for `string?` to come through as Swift `String?` / Kotlin `String?` instead of always-optional. Verify after the hardening.
- **Pagination.** Vora endpoints that paginate (`?page=`, `?pageSize=`) emit response shapes with explicit `Items` + `TotalCount`. Make sure generated clients model these as paginated collections, not bare arrays.

## Verification

After both clients are wired:

1. Add a deliberate API rename in `Vora.Api` (e.g. rename a `*VM` field). Run codegen on both clients. Both should fail to compile at the call site that used the old name.
2. Add a new endpoint. Run codegen. Both clients should pick it up automatically without any hand-edits.
3. Diff the generated Swift / Kotlin clients across two commits to confirm regeneration is deterministic — the same OpenAPI doc must produce the same output bytes every time.
