# TODOs (extracted from code comments)

Tracked work items removed from the codebase under the no-comments rule.
File and line references point at the pre-prune tree; lines have shifted since,
so treat them as approximate locations.

## High

- `UnoVibe/Providers/EventsProvider.cs:9` — SSE never reconnects/unsubscribes:
  single ReadEvent loop, fire-and-forget Task.Run with no catch/retry,
  registries grow, per-dir streams never torn down.
  Add reconnect + watchdog + Unsubscribe, clean map.
- `UnoVibe/Providers/SessionsStateProvider.cs:204` — AsyncHelper resumes off-UI-thread
  after await TurnStopActionAsync: Outcome/ShowContinue/reactive writes must marshal
  via Dispatcher.
- `UnoVibe/States/ChatboxState.AutoContinue.cs:21` — Drops old echo guards: any finish
  with reasoning-end retriggers continue up to 50, dedup by SSE-id not message-id.
  Add last-continued-id.
- `UnoVibe/States/ChatboxState.AutoContinue.cs:38` — Drains queue only when neither
  continue nor auto-continue can run: queued prompt behind error/reasoning stop stalls.
  Drain or surface count even when ShowContinue.
- `UnoVibe/States/ChatboxState.cs:206` — Fire-and-forget Task.Run: inner catch toasts
  via Dispatcher, but if dispatcher fails IsBusy stays true and toast lost.
  Route via AsyncHelper or assert dispatcher non-null.
- `UnoVibe/Pages/Chat/ModelPicker.cs:43` — Models.ModelOptions is ReactiveKeyedSet:
  confirm Count/enumeration invalidates on Add/Clear or list won't refresh after
  RefreshModelsAsync.

## Medium

- `UnoVibe.Integration/APIs/Sessions/GetMessageAsync.cs:8` — No directory param while
  SDK SessionMessageData carries query.directory: may 404 for folder-opened instances.
  Add string? directory like permission/question endpoints or document why global.
- `UnoVibe/Providers/EventsProvider.cs:235` — Null dispatcher silently drops all events
  when constructed off-UI-thread. Capture DispatcherQueue at root and assert non-null.
- `UnoVibe/Providers/ModelsProvider.cs:17` — Fire-and-forget in ctor races
  ResolveContextLimit (ContextLimit=0 until refresh). Expose awaited InitAsync or
  route via AsyncHelper.
- `UnoVibe/Providers/SessionsStateProvider.cs:23` — Per-session chatboxes never evicted
  except DeleteSession: ShowContinue/queues go stale across switches. Evict/LRU or reset
  on ActiveSessionId change.
- `UnoVibe/Providers/SessionsStateProvider.cs:24` — Per-directory null-session maps grow
  forever: remove when directory removed/session created.
- `UnoVibe/Providers/SessionsStateProvider.cs:137` — Guard skips already-known sessions
  so title/model changes while SSE down never heal. Add per-session refresh on
  SessionUpdated miss or retry button.
- `UnoVibe/Providers/UnoVibeProviders.cs:12` — Sessions + Models missing from
  Disposables: SessionsStateProvider never disposed, per-stream SSE delegates leak.
  Add or document why not.
- `UnoVibe/Helpers/MessageJsonHelper.cs:138` — Missing OutputLengthError arm
  (vs ApplyMessageError): ErrorMessage becomes "". Share helper with ApplyMessageError.
- `UnoVibe/Helpers/MessageJsonHelper.cs:308` — Drops OutputLength/StructuredOutput/
  ContextOverflow/ContentFilter to "". Add arms (share helper with retry switch).
- `UnoVibe/Helpers/MessageJsonHelper.cs:411` — Fire-and-forget image load breaks the
  no-fire-and-forget rule: unobserved exceptions vanish. Await or AsyncHelper.
- `UnoVibe/States/ChatMessagesState.cs:248` — Extend in-place update to the remaining
  part types (same contract: create on absent, update fields on present) instead of
  swapping the instance.
- `UnoVibe/States/ChatMessagesState.cs:539` — Cost re-sums truncated 200-msg window +
  Tokens from last assistant only: long sessions under-report. Verify ChatCost cache.
- `UnoVibe/States/ChatboxState.AutoContinue.cs:86` — Server GetMessageAsync per
  turn-stop (old code inspected local parts): extra latency + failure mode.
  Prefer local ChatMessagesState parts.
- `UnoVibe/States/ChatboxState.Command.cs:24` — Cache keyed only on TTL, not directory:
  switching dirs within 5min reuses wrong list. Store directory and refetch on change.
- `UnoVibe/States/ChatboxState.cs:279` — Same fire-and-forget as SendCommandNow: IsBusy
  reset only inside dispatcher closure, stuck if enqueue fails.
- `UnoVibe/States/OpencodeConnection.cs:5` — Lives in States/ but is a singleton
  connection/client lifecycle: move to Providers/ to match lifetime.
- `UnoVibe/States/OpencodeConnection.cs:6` — ConnectPage compares ConnectionStatus ==
  "Connected": expose bool IsConnected instead of string compare.
- `UnoVibe/Pages/Chat/ChatPage.cs:84` — Banned async void + forced GC.Collect smell:
  make async Task + AsyncHelper or delete with GC hack.
- `UnoVibe/Pages/Chat/ChatPage.cs:120` — Async void event handler: exceptions after
  await escape. Wrap body in try/catch -> toast.
- `UnoVibe/Pages/Chat/ChatPage.cs:149` — Async void: same escape risk as per-message
  fork. Wrap in try/catch.
- `UnoVibe/Pages/Chat/ChatRetryCard.cs:30` — DispatcherTimer started pre-Init, never
  stopped: accumulates on remount. Stop on Unloaded.
- `UnoVibe/Pages/Main/McpService.cs:7` — View-owned service with fire-and-forget
  Task.Run: Provider-shaped (window-scoped like Sessions/Events). Move to Providers/
  and use AsyncHelper.
- `UnoVibe/Pages/Main/SessionGroup.cs:14` — Group.Sessions is List<SessionHead> with
  plain Count/Take: works only if provider rebuilds groups on every change. Document
  contract or switch to ReactiveList.

## Low

- `UnoVibe/WindowsHelper.cs:23` — Length>0 -> is not null forwards "" to
  SuggestedStartFolder. Use !string.IsNullOrEmpty.
- `UnoVibe.Integration/AppJsonContext.cs:67` — Duplicate List<CommandInfo>: remove one.
- `UnoVibe.Integration/AppJsonContext.cs:148` — Duplicate registration: SessionInfo
  already registered at top. Remove one.
- `UnoVibe.Integration/SharedModels/Events/EventBase.cs:20` — Comment says "all opencode
  SSE event payloads" but this is MessageInfo role union.
- `UnoVibe.Integration/SharedModels/Events/MessageEvents.cs:71` — Upstream format is
  text|json_schema discriminated union; flattened here loses
  json_schema.schema/retryCount fidelity. Note as known gap.
- `UnoVibe.Integration/SharedModels/Events/MessageEvents.cs:188` — Upstream RetryPart.error
  is APIError-only; AssistantError accepts more. Narrow to ApiAssistantError or leave.
- `UnoVibe.Integration/SharedModels/Events/MessageEvents.cs:240` — Docs say metadata stays
  JsonElement but code types ToolMetadata: update agents-doc/integration.md
  (input/structured/ProviderMetadata stay JsonElement, metadata is typed).
- `UnoVibe.Integration/SharedModels/Events/SessionNextEvents.cs:3` — Speculative
  session.next.* surface not in SDK Event union and not emitted by current CLI: dead AOT
  bloat. Don't wire EventsProvider cases until server emits.
- `UnoVibe.Integration/SharedModels/Events/ToolMetadataModels.cs:39` — Verify
  JsonPropertyName casings against live /event payloads: wrong names silently stay null.
- `UnoVibe.Integration/APIs/Sessions/GetMessagesAsync.cs:9` — Stale doc: Info/Parts are
  now MessageInfo/List<Part>. Update or delete.
- `UnoVibe/Providers/EventsProvider.cs:253` — Two nested locks redundant: single lock
  suffices. Also UnregisterDelegate never removes delegateMapping entry (leak).
- `UnoVibe/Pages/Chat/ChatComposer.cs:226` — Async void handler (banned form,
  handler-justified): ensure exceptions can't escape; prefer async Task where markup allows.
- `UnoVibe/Pages/Chat/ChatHeader.cs:133` — Async void (banned form, internally guarded):
  prefer async Task + await in markup.
- `UnoVibe/Pages/Chat/ChatMessageList.cs:41` — Null ChatState renders nothing but null
  enumerable in keyed foreach is fragile: use empty-enumerable fallback or guard.
- `UnoVibe/Pages/Chat/MessagePartView.cs:3` — Naming overlap MessageView vs
  MessagePartView vs MessageParts/* confuses: rename to MessagePartDispatcher/Switch or
  document the chain.
- `UnoVibe/Pages/Connect/ConnectPage.cs:221` — Lost start directory (was
  ServerDirectory, now null): dialog opens at arbitrary path. Restore or document portal
  current_folder reason.
- `UnoVibe/Pages/Main/SessionGroup.cs:15` — ShowMore is per-component state and resets on
  keyed rebuild: "Show more" collapses on every sidebar refresh.

## Untriaged (no severity or empty marker)

- `UnoVibe/Models/ToolCallPartItem.cs:123` — Add GetInput<T>() back when typed tool input
  classes are implemented (Phase 2c).
- `UnoVibe/Pages/Main/SessionGroup.cs:27` — When attached property support falling back to
  element properly, do that instead of wrapping in Grid.
- `UnoVibe/Pages/Chat/ChatComposer.cs:304` — Empty marker between SendAsync and input
  clear (submit path).
- `UnoVibe/Pages/Chat/ChatComposer.cs:320` — Empty marker between SendAsync and input
  clear (send-with-mode path).
