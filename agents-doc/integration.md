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
- `SharedModels/Events/` — typed SSE event payloads (see "Event models" below).
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

## Event models

All SSE event payloads are modeled as C# classes in `SharedModels/Events/`. The `OpencodeEvent`
envelope's `Properties` field remains as `JsonElement` — consumers deserialize it into the
appropriate typed event model using the source-generated context:

```csharp
// Example: deserializing a session.status event
var status = JsonSerializer.Deserialize(
    evt.Properties.GetRawText(),
    AppJsonContext.Default.SessionStatusEvent);
```

### Event model file layout

| File | Contents |
|---|---|
| `EventBase.cs` | Enums, base classes with `JsonDerivedType` (MessageInfo, Part, ToolState, FilePartSource, AssistantError, SessionStatusPayload), shared sub-models, `EventTypes` constants |
| `SessionEvents.cs` | Session CRUD events (created/updated/deleted) using unified `SessionInfo` |
| `MessageEvents.cs` | Message updated/removed, part updated/removed/delta + 12 Part types + ToolState hierarchy + MessageInfo hierarchy (User/Assistant) |
| `SessionStatusEvents.cs` | Session status/idle/error/diff/compacted events |
| `PermissionEvents.cs` | V1 + V2 permission asked/replied events |
| `QuestionEvents.cs` | V1 + V2 question asked/replied/rejected events |
| `SessionNextEvents.cs` | V2 `session.next.*` events (shell, step, text, reasoning, tool, compaction, revert, agent/model switch, prompted, retried) |
| `SimpleEvents.cs` | Remaining simple events (server, file, mcp, tui, vcs, project, pty, todo, workspace, worktree, installation, plugin, reference, catalog, integration, command) |

### Discriminated unions

All TypeScript string-literal discriminated unions use `JsonDerivedType` on a base class:

```csharp
[JsonPolymorphic(TypeDiscriminatorPropertyName = "role")]
[JsonDerivedType(typeof(UserMessageInfo), "user")]
[JsonDerivedType(typeof(AssistantMessageInfo), "assistant")]
public abstract class MessageInfo { }
```

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
depending on the error-handling needs. The `Result<T>` type carries either a value or an
`ApiError` (HTTP status code + message). Callers use `TryGetValue`, `GetOrThrow`, or
`GetOr` to handle both paths.
