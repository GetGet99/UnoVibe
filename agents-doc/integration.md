# UnoVibe.Integration conventions

`UnoVibe.Integration` is a standalone `net10.0` class library containing the HTTP client
(`OpencodeClient`) for the opencode server REST API. It has zero Uno dependency and is
AOT-compatible.
**Read this file when** adding or modifying API endpoints, request/response DTOs, or the
`AppJsonContext` registrations.

## Project layout

- `OpencodeClient.cs` — core partial class: constructor (baseUrl + Basic auth), private HTTP
  helpers (`GetResultAsync`, `PostResultAsync`, throwing variants), static URL builders.
- `APIs/` — one file per endpoint, each declaring a `partial class OpencodeClient` with a
  single public method. Request/response DTOs live in the same file when endpoint-specific.
- `SharedModels/` — DTOs shared across multiple endpoints (`SessionInfo`, `MessageWithParts`,
  etc.).
- `AppJsonContext.cs` — source-generated `JsonSerializerContext` registering every DTO type.
- `Result.cs` — `Result<T>` discriminated return type and `ApiError`.

## Convention: one partial class = one endpoint

Every file under `APIs/` defines exactly one public method on `partial class OpencodeClient`.
If the endpoint has a request body, define the request DTO class (and any nested sub-models)
in the same file. Response types go in `SharedModels/` when reused, or in the same file when
endpoint-specific.

Example structure (`APIs/Sessions/CreateSessionAsync.cs`):

```csharp
namespace UnoVibe.Integration;

public sealed class CreateSessionRequest { ... }
public sealed class CreateSessionModelRequest { ... }

partial class OpencodeClient
{
    public async Task<Result<SessionInfo>> CreateSessionAsync(
        CreateSessionRequest request, string? directory = null,
        CancellationToken ct = default) { ... }
}
```

## API logic must be dumb

Endpoint methods must be thin wrappers around the HTTP helpers. They should:

- Build the URL (using `DirectoryUrl` or `LocationUrl` helpers).
- Delegate to `GetResultAsync`, `PostResultAsync`, or the throwing variants.
- Return the result directly.

They must **not** do any post-processing, field remapping, aggregation, or business logic
on the data. The API layer's job is transport and (de)serialization only. Any transformation
belongs in the caller (e.g., `ChatStore` or `SessionStore`).

## No JsonElement for structured data

Do not read or convert fields from `JsonElement` in API methods. Instead, define a proper
C# model/DTO with the correct types and register it in `AppJsonContext`.

The one exception is for **deferred/complex type unions** that are not yet fully implemented.
For example, `MessageWithParts.Parts` is `List<JsonElement>?` because the full part-type
union is not yet modeled. When a `JsonElement` field is used, add a doc comment explaining
that it is deferred (see `MessageWithParts.cs` for the pattern).

## Reference the opencode source for ins/outs

When adding a new endpoint, **do not guess** the request or response shape. Check the
opencode server source at the path documented in
[`referenced-projects.md`](referenced-projects.md) (the `opencode` checkout). Look at the
route handler to see the exact JSON fields, nullability, and nesting.

## JsonElement as a temporary escape hatch

Fields that are not yet needed by the client, or whose shape is a complex discriminated
union not yet worth modeling, may be typed as `JsonElement` or omitted entirely. As soon as
the client needs to read or act on such a field, transition it to a proper C# model and
register the model in `AppJsonContext`.

## Registering new types in AppJsonContext

Every new DTO class must be added as a property in `AppJsonContext.cs` so the source
generator can produce the AOT-compatible serialization metadata. Without this registration,
`JsonSerializer.Serialize/Deserialize` calls will throw at runtime under Native AOT.

## Result<T> pattern

Public endpoint methods return `Result<T>` (non-throwing) or `Task`/`Task<T>` (throwing)
depending on the error-handling needs. The `Result<T>` type carries either a value or an
`ApiError` (HTTP status code + message). Callers use `TryGetValue`, `GetOrThrow`, or
`GetOr` to handle both paths.
