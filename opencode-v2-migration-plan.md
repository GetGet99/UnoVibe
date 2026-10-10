# OpenCode v2 API migration plan

Reference: opencode `v1.18.29` (`16747470f9`) for the v1/`/api/*` inventory;
branch `v2` of the same repo for the forward-looking items.
Client usage: `UnoVibe.Integration/APIs/` (one file per endpoint).

"v2" means the `/api/*` route group (OpenAPI identifiers `v2.*`); no literal `/v2/`
URL prefix exists. v2 list routes take a single location (`?location[directory]=`,
`packages/protocol/src/groups/location.ts:5-12`) and return a `{location, data}`
envelope, which `UnoVibe.Integration` already models as `APIEntryResponse<T>`
(`SharedModels/APIEntryResponse.cs`).

Multi-directory behavior: neither v1 (`?directory=`,
`packages/opencode/src/server/routes/instance/httpapi/middleware/workspace-routing.ts:86-88`)
nor v2 (`packages/server/src/location.ts:29-39`) accepts multiple directories per
request; each request resolves exactly one location, and session-pinned routes ignore
the parameter and use the session's own directory. The v2 list handlers resolve one
per-location service each (`CommandV2` in `packages/core/src/command.ts:64`,
`SkillV2` in `packages/core/src/skill.ts:132`). UnoVibe issues one request per
directory for these lists (`SuggestionProviders.cs`, `SessionsStateProvider.cs`);
no server-side fan-out exists in either version, so client-side fan-out stays as-is.

Already on v2, no action needed: `FindFilesAsync.cs` uses `GET /api/fs/find`.
`GetCommandsAsync.cs` / `GetSkillsAsync.cs` already call v2 (`/api/command`,
`/api/skill`) as a fallback when the legacy endpoint returns empty/fails.

## Stage 1 — Straightforward (v2 exists, same shape, field rewiring only)

| UnoVibe usage | v1 today | v2 target | Notes |
|---|---|---|---|
| `HealthAsync.cs` | `GET /global/health` → `{healthy, version}` | `GET /api/health` → `{healthy: true}` (`protocol/src/groups/health.ts:5`) | UnoVibe maps to `bool`; `version` unused. Trivial. |
| `GetSkillsAsync.cs` | `GET /skill` → `[{name, description?, location, content}]` (`opencode/src/skill/index.ts:37`) | `GET /api/skill` → `{location, data: [{name, description?, slash?, location, content}]}` (`schema/src/skill.ts`) | v2 is a superset; UnoVibe consumes only name/description. Drop the legacy call, keep v2-only. Same per-directory fan-out. |
| `Question/GetPendingQuestionsAsync.cs`, `ReplyQuestionAsync.cs`, `RejectQuestionAsync.cs` | `GET /question`, `POST /question/:id/reply {answers}`, `POST /question/:id/reject` | `GET /api/question/request`, `POST /api/session/:sid/question/:id/reply`, `POST .../reject` (`protocol/src/groups/question.ts:20-81`) | Request shape identical (`PendingQuestion` mirrors v2 `Request`: id, sessionID, questions, tool). v2 `Reply` is the same `{answers}` struct (`schema/src/question.ts`). Envelope matches the existing pattern. Reply/reject are session-scoped, so thread `sessionID` (already in the DTO) through the two reply/reject signatures. |
| `Permission/GetPendingPermissionsAsync.cs`, `ReplyPermissionAsync.cs` | `GET /permission`, `POST /permission/:id/reply {reply, message?}` | `GET /api/permission/request`, `POST /api/session/:sid/permission/:id/reply` (`protocol/src/groups/permission.ts:23-136`) | Near-renames: `permission`→`action`, `patterns`→`resources`, `always`→`save`, `tool{messageID,callID}`→`source{type:"tool",messageID,callID}` (`schema/src/permission.ts:25-32`). Reply literals (`once/always/reject`) identical. The rename propagates to `PermissionRequestItem`/callers, and reply is session-scoped like questions. Mechanical, no new logic. |

## Stage 2 — Needs work (v2 exists, UnoVibe structural changes required)

| UnoVibe usage | v1 today | v2 target | What changes |
|---|---|---|---|
| `GetCommandsAsync.cs` (v2-only) | `GET /command` items carry `source` (`command/mcp/skill`) + `hints` (`opencode/src/command/index.ts:22`) | `GET /api/command` items have neither (`schema/src/command.ts`) | `SuggestionProviders.cs:114-116` (`:mcp` display, skill-vs-command key) and `ChatboxState.Command.cs:27-31` (skill routing) branch on `Source`. v2-only needs a replacement classification strategy. Keep the legacy-first dual call until then. |
| `GetModesAsync.cs` (`GetAgentsAsync`) | `GET /agent` items keyed by `name` (`opencode/src/agent/agent.ts:35`) | `GET /api/agent` items keyed by `id`, no `name` (`schema/src/agent.ts:20`) | `ModelsProvider.cs` filters `Mode == "primary"`, shows `Name`, and the send path submits agent name strings. Mapping `Name`←`id` is a semantic change needing verification against server behavior, not just rewiring. |
| `Provider/GetProvidersAsync.cs`, `GetProviderAuthMethodsAsync.cs` | `GET /provider` → `{all, connected, default}` single shape | `GET /api/provider` + `GET /api/model` (providers split from models) | `ModelsProvider.cs` consumes `Connected`/`All`/`Variants`/`Limit` together. Rework to join two endpoints. |
| `Provider/AuthorizeOAuthAsync.cs`, `CompleteOAuthAsync.cs`, `SetAuthAsync.cs` | `POST /provider/:id/oauth/authorize|callback`, `PUT /auth/:id` | `/api/integration/*` (`connect/key`, `connect/oauth` + attempt poll/complete/cancel) and `/api/credential` (`protocol/src/groups/integration.ts:12-127`, `credential.ts:8-36`) | Different multi-step flow with attempt polling, not field rewiring. Includes new UI states. |
| `Sessions/ListSessionsAsync.cs`, `CreateSessionAsync.cs`, `GetSessionAsync.cs` | `GET /session` → bare array; `POST /session` body `{title?, agent?, model?, parentID?, ...}`; v1 `Session.Info` has flat `directory`, `slug`, `version`, `share` (`opencode/src/session/session.ts:224`) | `GET /api/session` → `{data, cursor}` paginated; `POST /api/session` body `{id?, agent?, model?, location?}` (no title/parent); v2 `Session.Info` nests `location`, drops slug/version/share (`schema/src/session.ts:19`) | `SessionsStateProvider.cs` session model, per-directory fetch, and creation flow assume v1 fields (title-on-create, slug). Pagination + model reshape. |
| `Sessions/GetMessagesAsync.cs`, `GetMessageAsync.cs` | `GET /session/:id/message[/:mid]` → `WithParts` (`MessageInfo` + `Part` union incl. tool states) | `GET /api/session/:id/message[/:mid]` (+ `history`/`context`) → v2 `Message`/event shapes (`protocol/src/groups/session.ts:292-360`, `message.ts:26`) | `ChatMessagesState.cs` and all tool views consume `MessageWithParts`/`Parts` deeply. New message model + rendering mapping. |
| `Sessions/SendPromptAsync.cs`, `SendCommandAsync.cs`, `SendShellAsync.cs`, `AbortAsync.cs`, `RevertAsync.cs`, `UnrevertAsync.cs` | `prompt_async` (ack + SSE), `command`, `shell`, `abort`, `revert{messageID}`, `unrevert` | `POST .../prompt` (returns `Admitted`, options `delivery/resume`), `.../interrupt`, `.../revert/stage|clear|commit` (`protocol/src/groups/session.ts:205-281,345`) | Unified prompt endpoint replaces three send paths (incl. infinite-timeout clients); revert becomes a 3-step stage/clear/commit flow. Send/retry/autoscroll logic in `ChatMessagesState.cs` must be redesigned, not rewired. |
| `ReadEventAsync.cs` | `GET /event?directory=` — one SSE stream per directory (`EventsProvider.cs` fan-out) | `GET /api/event` — single global stream, no location param (`protocol/src/groups/event.ts:35`), or per-session `GET /api/session/:id/event` | Subscription management inverts (fan-out → single stream + dispatch on payload location), plus `V2Event` union remap for every `EventTypes` consumer. |

## Stage 3 — Blocked (no v2 equivalent at v1.18.29)

| UnoVibe usage | v1 today | v2 status |
|---|---|---|
| `Sessions/ForkSessionAsync.cs` | `POST /session/:id/fork` | No fork route in `protocol/src/groups/session.ts`. |
| `Sessions/UpdateSessionTitleAsync.cs` | `PATCH /session/:id {title}` | No title-update route; v2 session group has no PATCH. |
| `Sessions/GetSessionStatusAsync.cs` | `GET /session/status` → per-session `{idle/busy/retry + payload}` drives busy/outcome indicators | Only `GET /api/session/active` → `{sessionID: {type: "running"}}` (`protocol/src/groups/session.ts:146`). Different semantics; idle/retry payloads unavailable. |
| `Mcp/GetMcpStatusAsync.cs`, `McpConnectAsync.cs`, `McpDisconnectAsync.cs`, `McpAuthenticateAsync.cs` | `GET /mcp`, `POST /mcp/:name/connect|disconnect|auth/authenticate` | No `/api/mcp` group exists. |
| `GetVCSInfoAsync.cs` | `GET /vcs` (+ `/vcs/status|diff` used via events) | No v2 VCS routes. |
| `GetPathAsync.cs` | `GET /path` | No v2 path route (`GET /api/location` returns location info, not instance paths). |

Out of scope (UnoVibe does not use them): v2 also lacks config/global/tui equivalents,
and v1-only share/summarize/init/diff/todo/pty/file/sync routes have no UnoVibe callers,
so they are not migration blockers. (`/api/pty` exists but is unused by the app.)

## v2 branch alternatives for the blocked items

Branch `2.0` predates v1.18.29 and is superseded; the active line is branch `v2`
(31 `/api/*` groups, moves daily — re-check `packages/protocol/src/groups/` there
before starting this work).

**DO NOT DO THIS TODAY: It will not be compatible with user's installed OpenCode version.**

| Blocked item | `v2` branch alternative | Migration shape |
|---|---|---|
| Fork | `POST /api/session/:id/fork {before?}` → `{data}` (`session.ts:313`) | Near-rename: `messageID`→`before`, plus envelope. Stage 1-level effort. |
| Title PATCH | `PATCH /api/session/:id {title?, metadata?, permissions?}` → NoContent (`session.ts:362`) | Drop the returned `SessionInfo` (refetch or update locally). Small. |
| Session status map | Still none. `session.active` stays a running-map; `Session.Info` carries no status; `stats` is usage analytics | Remains blocked. The alternative is deriving busy/idle from the session event stream (`GET /api/session/:id/event`), which is new logic. |
| MCP (list/connect/disconnect/auth) | `GET /api/mcp` → `{location, data: [{name, status, integrationID?}]}` with tagged `status: connected\|pending\|disabled\|failed\|needsAuth`; connect/disconnect exist under `/api/experimental/mcp/:server/`; no authenticate endpoint (`mcp.ts`) | List needs array+tagged-union mapping instead of the name-keyed `{Status, Error?}` map. Auth has no endpoint; the `integrationID` field points at the `/api/integration` OAuth flow as the intended path, which is a new flow. Medium. |
| VCS (`GET /vcs` → `{branch?, default_branch?}`) | `GET /api/vcs` → `{location, data: {provider?, branch: {current?, default?}}}` (`vcs.ts:52`, `schema/src/vcs.ts:12`); plus `status`, `diff`, `branch/list` | Small rewiring: `branch`→`branch.current`, `default_branch`→`branch.default`, plus envelope. |
| Path (`GET /path`, used only for `directory` in `OpencodeConnection.cs:54`) | No path route. `GET /api/info` returns `{version, pid, urls, paths: {tmp}}` (no workdir); `GET /api/location` returns `{directory, workspaceID?, project}` (`location.ts:36`) | `location.get` without a directory (falls back to server cwd) is the candidate replacement for the one field used. Needs verification. |

Related to Stage 2: the `v2` branch also carries `POST /api/session/:id/command`
(`{name, ...prompt, delivery?}`) and `POST .../shell` (`session.ts:418,480`), so the
send-command/send-shell paths may map almost directly instead of via unified `prompt`.


## Suggested order

Within Stage 1, cheapest first: health → skills (drop the legacy call) →
questions → permissions. Each is one `APIs/` file pair plus mechanical caller updates,
verified by building `UnoVibe.Integration` and exercising the corresponding UI surface.
Do not start Stage 2 items piecemeal: sessions/messages/send/events are mutually
dependent (prompt → Admitted + event stream → v2 message shapes), so they form one
workstream, while agents/providers/auth form a second (model-picker) workstream.
