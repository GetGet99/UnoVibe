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

Session identity is shared across v1 and v2 (live check on 1.18.29: `GET /session`
and `GET /api/session?directory=` return identical ID sets, and v2 `session.get`
resolves v1 IDs). Only the payload stores are split by flow: v2 message/history/context
and v2 question/permission lists do not see v1-flow data. Migration is therefore
per-endpoint: endpoints without a v2 equivalent (fork, title update, status map, MCP,
VCS, path) stay on v1 and keep working on the same session IDs.

## Stage 1 — Straightforward (v2 exists, same shape, field rewiring only)

| UnoVibe usage | v1 today | v2 target | Notes |
|---|---|---|---|
| `HealthAsync.cs` | `GET /global/health` → `{healthy, version}` | `GET /api/health` → `{healthy: true}` (`protocol/src/groups/health.ts:5`) | UnoVibe maps to `bool`; `version` unused. Trivial. Live check on 1.18.29: both return healthy. |

## Stage 2 — Needs work (v2 exists, UnoVibe structural changes required)

| UnoVibe usage | v1 today | v2 target | What changes |
|---|---|---|---|
| `GetCommandsAsync.cs` (dual call stays) | `GET /command` merges built-ins + config + MCP prompts + skill-synthesized + plugin-added, each with `source` (`command/mcp/skill`) + `hints` (`opencode/src/command/index.ts:22`) | `GET /api/command` discovers config `commands` per location (the v1 `command` key migrates via `ConfigMigrateV1`), but no MCP prompts, no skill-synthesized commands, no plugin-added commands, and no `source`/`hints` (`schema/src/command.ts`) | Live check on 1.18.29 with a fixture dir: v2 serves the fixture's custom command for that directory only, with no leak into a sibling worktree dir, so `location` is respected. `SuggestionProviders.cs:114-116` and `ChatboxState.Command.cs:27-31` branch on `Source`, so v2-only additionally needs a replacement classification strategy. Cold-location caveat: the first v2 queries for a fresh directory return `[]` and self-heal within seconds (location warm-up race); the current legacy-first order masks this. Keep the dual call. |
| `Question/*`, `Permission/*` (coupled to send-path migration) | `GET /question`, `POST /question/:id/reply|reject`; `GET /permission`, `POST /permission/:id/reply` | `GET /api/question/request`, session-scoped reply/reject; `GET /api/permission/request`, session-scoped reply (`protocol/src/groups/question.ts:20-81`, `permission.ts:23-136`) | Shapes are near-identical (question `Reply` is the same `{answers}` struct; permission is renames: `permission`→`action`, `patterns`→`resources`, `always`→`save`, `tool`→`source.tool`). Not usable yet: v1 and v2 keep separate pending stores (v1 `Permission` service holds `pending: Map` in `InstanceState`; v2 handlers use `QuestionV2.Service`/v2 permission services), and the agent's tools run in the v1 session flow, so v1-flow requests are invisible to v2 list/reply (live check: v1 list showed the pending `que_*`, v2 list empty, v2 reply 404s). Adopt together with the v2 `prompt` send path, and thread `sessionID` through the reply/reject signatures then. |
| `GetModesAsync.cs` (`GetAgentsAsync`) | `GET /agent` items keyed by `name` (`opencode/src/agent/agent.ts:35`) | `GET /api/agent` items keyed by `id`, no `name` (`schema/src/agent.ts:20`) | Live check on 1.18.29: same 7 agents under both. `ModelsProvider.cs` filters `Mode == "primary"`, shows `Name`, and the send path submits agent name strings. Mapping `Name`←`id` is a semantic change needing verification against server behavior, not just rewiring. |
| `Provider/GetProvidersAsync.cs` | `GET /provider` → `{all: 226 with models, connected, default}` single shape | `GET /api/provider` (active providers only, no models) + `GET /api/model` (flat, with `variants`/`limit`, `schema/src/model.ts:71-103`) | Live check on 1.18.29: v2 provider list returns 1 entry (`opencode`); models come from `/api/model` (38 entries, each with `providerID`). `ModelsProvider.cs` consumes `Connected`/`All`/`Variants`/`Limit` together — rework to join two endpoints. `GetProviderAuthMethodsAsync` (key-entry prompts, used by `ProviderConnectDialog.cs:261`) has no v2 endpoint on either line; it stays v1 until the integration-connect UI is built (see Stage 3). |
| `Provider/AuthorizeOAuthAsync.cs`, `CompleteOAuthAsync.cs`, `SetAuthAsync.cs` | `POST /provider/:id/oauth/authorize|callback`, `PUT /auth/:id` | `/api/integration/*` (`connect/key`, `connect/oauth` + attempt poll/complete/cancel) and `/api/credential` (`protocol/src/groups/integration.ts:12-127`, `credential.ts:8-36`) | Different multi-step flow with attempt polling, not field rewiring. Includes new UI states. |
| `Sessions/ListSessionsAsync.cs`, `CreateSessionAsync.cs`, `GetSessionAsync.cs` | `GET /session?directory=` → bare array; `POST /session` body `{title?, agent?, model?, parentID?, ...}`; v1 `Session.Info` has flat `directory`, `slug`, `version`, `share` (`opencode/src/session/session.ts:224`) | `GET /api/session?directory=` → `{data, cursor}` paginated; `POST /api/session` body `{id?, agent?, model?, location?}` (no title/parent); v2 `Session.Info` nests `location`, drops slug/version/share (`schema/src/session.ts:19`) | `/api/session` takes the flat `SessionsQuery` (`?directory=`, `protocol/src/groups/session.ts:98-104`) — not `location[directory]`; do not use the `LocationUrl` helper here. Live check on 1.18.29: identical 49-session ID sets under both. `SessionsStateProvider.cs` session model, per-directory fetch, and creation flow assume v1 fields (title-on-create, slug). Pagination + model reshape. |
| `Sessions/GetMessagesAsync.cs`, `GetMessageAsync.cs` | `GET /session/:id/message[/:mid]` → `WithParts` (`MessageInfo` + `Part` union incl. tool states) | `GET /api/session/:id/message[/:mid]` (+ `history`/`context`) → v2 `Message`/event shapes (`protocol/src/groups/session.ts:292-360`, `message.ts:26`) | v2 message/history/context endpoints are blind to v1-flow sessions (live check on 1.18.29: 95 v1 messages → 0 v2 messages; single-message fetch → `MessageNotFoundError`). v2 reads `SessionMessageTable` (`core/src/session/store.ts:48`) while v1 assembles `WithParts` from the message/part tables — separate stores, same bug class as questions/permissions. `ChatMessagesState.cs` and all tool views consume `MessageWithParts`/`Parts` deeply. New message model + rendering mapping, usable only alongside the v2 send path. |
| `Sessions/SendPromptAsync.cs`, `SendCommandAsync.cs`, `SendShellAsync.cs`, `AbortAsync.cs`, `RevertAsync.cs`, `UnrevertAsync.cs` | `prompt_async` (ack + SSE), `command`, `shell`, `abort`, `revert{messageID}`, `unrevert` | `POST .../prompt` (returns `Admitted`, options `delivery/resume`), `.../interrupt`, `.../revert/stage|clear|commit` (`protocol/src/groups/session.ts:205-281,345`) | Unified prompt endpoint replaces three send paths (incl. infinite-timeout clients); revert becomes a 3-step stage/clear/commit flow. Send/retry/autoscroll logic in `ChatMessagesState.cs` must be redesigned, not rewired. |
| `ReadEventAsync.cs` | `GET /event?directory=` — one SSE stream per directory (`EventsProvider.cs` fan-out) | `GET /api/event` — single global stream, no location param (`protocol/src/groups/event.ts:35`), or per-session `GET /api/session/:id/event` | No store split: v1 `/event` is projected from the v2 bus (`EventV2Bridge`, `opencode/src/event-v2-bridge.ts`), and both streams emit the same type names with the same activity (live check on 1.18.29). Only the envelope differs (`properties` vs `data` + `durable`/`location`). Subscription management still inverts (fan-out → single stream + dispatch on payload location), plus `V2Event` union remap for every `EventTypes` consumer. |

## Stage 3 — Blocked (no v2 equivalent at v1.18.29)

| UnoVibe usage | v1 today | v2 status |
|---|---|---|
| `Sessions/ForkSessionAsync.cs` | `POST /session/:id/fork` | No fork route in `protocol/src/groups/session.ts`. |
| `Sessions/UpdateSessionTitleAsync.cs` | `PATCH /session/:id {title}` | No title-update route; v2 session group has no PATCH. |
| `Sessions/GetSessionStatusAsync.cs` | `GET /session/status` → per-session `{idle/busy/retry + payload}` drives busy/outcome indicators | Only `GET /api/session/active` → `{sessionID: {type: "running"}}` (`protocol/src/groups/session.ts:146`). Different semantics; idle/retry payloads unavailable. |
| `Mcp/GetMcpStatusAsync.cs`, `McpConnectAsync.cs`, `McpDisconnectAsync.cs`, `McpAuthenticateAsync.cs` | `GET /mcp`, `POST /mcp/:name/connect|disconnect|auth/authenticate` | No `/api/mcp` group exists. |
| `GetVCSInfoAsync.cs` | `GET /vcs` (+ `/vcs/status|diff` used via events) | No v2 VCS routes. |
| `GetPathAsync.cs` | `GET /path` | No v2 path route (`GET /api/location` returns location info, not instance paths). |
| `GetSkillsAsync.cs` (dual call stays) | `GET /skill` → config entries + embedded + external `.agents/skills/`, `.claude/skills/`, worktree-upward scan (`opencode/src/skill/index.ts:37`) | `GET /api/skill` covers config `skill`/`skills` dirs, config `skills` entries (relative paths resolve against the requested directory), and embedded `customize-opencode` (`core/src/config/plugin/skill.ts`) — but no external scan | Live check on 1.18.29 with a fixture dir: v2 serves the fixture's configured skill for that directory only, so `location` is respected. The remaining gap is exactly the external `.agents/.claude` upward scan (v1 lists `quickmarkup` from it, v2 does not; v1 gates it behind `disableExternalSkills`/`disableClaudeCodeSkills` flags that have no v2 counterpart). The `v2` branch does not restore the scan. Upstream tracking: #41213 (v1 line, but its repro queries `/api/skill`, not v1 `GET /skill`) and #43742 (v2 line) — both open, assigned, `2.0` label; #43742 notes the v2 docs list `~/.agents/skills` as a documented compatibility source. UnoVibe is unaffected: it uses v1 `GET /skill` legacy-first, and the agent skill tool reads the same v1 `Skill.Service` (`opencode/src/tool/skill.ts:15`); the v1 external-scan code is identical in v1.18.15 and v1.18.29. `AGENTS.md` instruction files are unaffected — v2 loads global + upward `AGENTS.md` (`core/src/config/plugin/instruction.ts`) — only `.agents/skills/**/SKILL.md` discovery is missing. Same cold-location warm-up race as commands (first queries return `[]`); the legacy-first order masks it. Keep the dual call until discovery parity. |
| `Provider/GetProviderAuthMethodsAsync.cs` | `GET /provider/auth` → key-entry prompts (used by `ProviderConnectDialog.cs:261`) | No v2 endpoint on either line (the `v2` branch `provider` group is list + get only). Replacement is the `/api/integration` connect flow from the Stage 2 auth work. |

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

Stage 1 is health only: questions/permissions/skills stay on v1 (separate stores,
discovery subset — see above), and commands already dual-call. Each Stage 2 item is
one `APIs/` file pair plus caller updates, verified by building `UnoVibe.Integration`
and exercising the corresponding UI surface against a live server.
Do not start Stage 2 items piecemeal: sessions/messages/send/events/questions/permissions
are mutually dependent (v2 `prompt` → v2 stores → v2 list/reply/message/event shapes),
so they form one workstream, while agents/providers/auth form a second (model-picker)
workstream.
