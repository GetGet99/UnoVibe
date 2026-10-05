# UnoVibe.Integration conventions

`UnoVibe.Integration` is a standalone `net10.0` class library containing the HTTP client
(`OpencodeClient`) for the opencode server REST API. It has zero Uno dependency and is
AOT-compatible.
**Read this file when** adding or modifying API endpoints, request/response DTOs, or the
`AppJsonContext` registrations.

## Project layout

- `OpencodeClient.cs` — core partial class: constructor (baseUrl + Basic auth), private HTTP
  helpers, static URL builders.
- `APIs/` — one file per endpoint, each declaring a `partial class OpencodeClient` with a
  single public method. Request/response DTOs live in the same file when endpoint-specific.
- `SharedModels/` — DTOs shared across multiple endpoints.
- `SharedModels/Events/` — typed SSE event payloads (see "Event models" below).
- `AppJsonContext.cs` — source-generated `JsonSerializerContext` registering every DTO type.
- `Result.cs` — `Result<T>` discriminated return type and `ApiError`.

## Convention: one partial class = one endpoint

Every file under `APIs/` defines exactly one public method on `partial class OpencodeClient`.
If the endpoint has a request body, define the request DTO class (and any nested sub-models)
in the same file. Response types go in `SharedModels/` when reused, or in the same file when
endpoint-specific. (See any file under `APIs/Sessions/` for the shape.)

## API logic must be dumb

Endpoint methods must be thin wrappers around the HTTP helpers. They should:

- Build the URL (using `DirectoryUrl` or `LocationUrl` helpers).
- Delegate to `GetResultAsync`, `PostResultAsync`, or the throwing variants.
- Return the result directly.

They must **not** do any post-processing, field remapping, aggregation, or business logic
on the data. The API layer's job is transport and (de)serialization only. Any transformation
belongs in the caller (e.g., `ChatboxState` or `SessionsStateProvider`).

## No JsonElement for structured data

Do not read or convert fields from `JsonElement` in API methods. Instead, define a proper
C# model/DTO with the correct types and register it in `AppJsonContext`.

## Event models

All SSE event payloads are modeled as C# classes in `SharedModels/Events/`. The `OpencodeEvent`
envelope's `Properties` field remains as `JsonElement` — consumers deserialize it into the
appropriate typed event model using the source-generated context.

### Discriminated unions

All TypeScript string-literal discriminated unions use `JsonDerivedType` on a base class (see
`MessageInfo` in `EventBase.cs` for the shape).

### Tool input / metadata

Tool `input`, `metadata`, `structured`, and `ProviderMetadata` fields remain as `JsonElement`
since their shape varies per tool and is truly dynamic.

## Reference the opencode source for ins/outs

When adding a new endpoint, **do not guess** the request or response shape. Check the
opencode server source at the path documented in
[`referenced-projects.md`](referenced-projects.md) (the `opencode` checkout). Look at the
route handler to see the exact JSON fields, nullability, and nesting.

## Registering new types in AppJsonContext

Every new DTO class must be added as a property in `AppJsonContext.cs` so the source
generator can produce the AOT-compatible serialization metadata. Without this registration,
`JsonSerializer.Serialize/Deserialize` calls will throw at runtime under Native AOT.

## Result<T> pattern

Public endpoint methods return `Result<T>` (non-throwing) or `Task`/`Task<T>` (throwing)
depending on the error-handling needs. Callers handle both paths via the `Result<T>` API.
