using System.Text.Json;
using System.Threading.Channels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;
using UnoVibe.Models;

namespace UnoVibe.Services;

/// <summary>
/// Reactive store for ONE chat session. Owns the session's messages, usage stats, revert
/// marker, retry/continue state, composer attachments and per-session mode/model/variant —
/// everything the chat page shows for the currently-active session.
///
/// Stores are created lazily by <see cref="ChatStore"/> the first time a session is opened
/// and cached (keyed by session id) so switching sessions never recreates or resets them:
/// switching re-points the router's <see cref="ChatStore.Active"/> reference and the cached
/// store (messages included) is reused. Sessions that exist on the sidebar but were never
/// opened have no store — only the router's per-session sidebar maps track them.
///
/// The mutable display fields are QuickMarkup reactive references (declared in the markup
/// header) so the chat page binds to them directly via <c>Store.Active.X</c>.
/// </summary>
[QuickMarkup("""
    using UnoVibe.Models;
    using QuickMarkup.Infra.Collections;
    public int HiddenMessages;
    public bool IsBusy;
    public int PendingPrompts;
    public int PendingImageCount;
    public string SessionTitle = "New Chat";
    public string UsageCostLabel = "$0.00";
    public string UsageTokensLabel = "0";
    public string ContextLabel = "0%";
    public double ContextUsage;
    public long UsageTokensInput;
    public long UsageTokensOutput;
    public long UsageTokensReasoning;
    public long UsageTokensCacheRead;
    public long UsageTokensCacheWrite;
    public long ContextLimit;
    // Parent session id when this session is a subagent (spawned by a task tool call).
    // Empty for root sessions; drives the header's "back to parent" button.
    public string ParentSessionId = "";
    // Human-readable session status banner (busy/retry messages); empty means idle.
    public string StatusMessage = "";
    // Auto-retry state for the active turn (session.status type "retry"); drives the
    // end-of-chat retry card. RetryNextMs is the absolute unix-ms time of the next attempt.
    public bool IsRetrying;
    public string RetryMessage = "";
    public int RetryAttempt;
    public long RetryNextMs;
    // Live countdown text recomputed each second by the chat page timer ("Attempt #2 · retrying in 3s").
    public string RetryCountdown = "";
    // True when the stopped turn warrants the end-of-chat "Continue" button: the last assistant
    // message carried a non-interrupt error, or the chat ends on a Thinking (reasoning) part.
    // Never set when the stop was handled by an automatic "continue" (turn.autocontinue setting).
    public bool ShowContinue;
    public string Mode = "build";
    public string ModelId = "";
    public string ProviderId = "";
    public string Variant = "Default";
    public bool HasVariants;
    // The ModelOption currently selected by the model combo. A computed derived from the
    // ModelId/ProviderId refs + the router's model list, so it re-resolves automatically
    // when the options are (re)populated (refresh rebuilds the option instances).
    public ModelOption? SelectedModelOption => `Router.ModelOptions.Reactive.FirstOrDefault(m => m.Id == ModelId && m.ProviderId == ProviderId)`;
    // Undo marker for this session: the id of the user message the conversation is
    // reverted to (the server's session "revert" field). Empty = not reverted. Drives the
    // revert card + message filter (messages with id >= RevertMessageId are hidden).
    public string RevertMessageId = "";
    // Card label for the revert banner, e.g. "1 message reverted". Computed whenever the
    // revert point changes (recounts the reverted user messages from the message list).
    public string RevertCountLabel = "";
    """)]
public sealed partial class SessionStore
{
    /// <summary>Maximum number of messages kept in the UI; older ones are dropped to keep rendering smooth.</summary>
    public const int MaxVisibleMessages = 200;

    /// <summary>Image file extensions accepted by the picker and the clipboard storage-items paste path.</summary>
    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp" };

    /// <summary>
    /// Clipboard format names probed (in order) when pasting raw image bytes. Covers the union
    /// of what each Skia backend exposes: X11 mime atoms (<c>image/png</c>, <c>image/jpeg</c>,
    /// ...) returning <c>byte[]</c>, and Win32 registered format names (<c>PNG</c>, <c>JFIF</c>,
    /// ...) returning <c>IRandomAccessStream</c>, plus the CF_DIB remap
    /// <c>StandardDataFormats.Bitmap</c> returning a <c>RandomAccessStreamReference</c>.
    /// </summary>
    private static readonly (string Name, string Mime, string Ext)[] ImageClipboardFormats =
    {
        ("image/png", "image/png", "png"),
        ("image/jpeg", "image/jpeg", "jpeg"),
        ("image/gif", "image/gif", "gif"),
        ("image/webp", "image/webp", "webp"),
        ("image/bmp", "image/bmp", "bmp"),
        ("PNG", "image/png", "png"),
        ("JFIF", "image/jpeg", "jpeg"),
        ("JPEG", "image/jpeg", "jpeg"),
        ("GIF", "image/gif", "gif"),
        ("WEBP", "image/webp", "webp"),
        ("BMP", "image/bmp", "bmp"),
        (StandardDataFormats.Bitmap, "image/bmp", "bmp"),
    };

    /// <summary>The router that owns this store (client, sidebar, settings options).</summary>
    public ChatStore Router { get; set; } = null!;

    /// <summary>The server session id this store renders ("" for an unsaved draft).</summary>
    public string SessionId { get; set; } = "";

    public ObservableCollection<MessageItem> Messages { get; } = new();

    /// <summary>Image attachments staged for the next prompt (shown as thumbnails above the input).</summary>
    public ObservableCollection<ImageAttachment> PendingImages { get; } = new();

    private readonly Dictionary<string, MessageItem> _messagesById = new();
    private readonly Queue<string> _pendingPrompts = new();

    // Auto-continue ("turn.autocontinue" setting) bookkeeping. When a turn stops with the chat
    // ending on a Thinking (reasoning) part, a "continue" prompt is sent automatically instead of
    // surfacing the end-of-chat Continue button, and the router suppresses the completion toast +
    // sidebar unread/outcome indicators for that stop (the turn is already restarting).
    private const int MaxAutoContinues = 50;

    /// <summary>Consecutive automatic continues fired without an intervening manual send or a
    /// stop that didn't qualify — bounds runaway loops against a provider that keeps stopping
    /// mid-thinking; past the cap the manual Continue button returns.</summary>
    private int autoContinueStreak;
    private bool autoContinued;
    private bool sawRunningStatus;

    /// <summary>
    /// True between an automatic continue firing and the server confirming the restarted turn
    /// with its first non-idle status event. Any further stop signal for that same stop (the
    /// server emits session.status idle and the final message.updated carrying finish in either
    /// order) is an echo and must neither re-fire nor clobber the fresh turn's busy state.
    /// </summary>
    private bool AwaitingAutoContinueRun => autoContinued && !sawRunningStatus;

    [QuickMarkupConstructor]
    private void Ctor()
    {
        Init();
    }

    private void AppendMessage(MessageItem message)
    {
        Messages.Add(message);
        while (Messages.Count > MaxVisibleMessages)
        {
            Messages.RemoveAt(0);
            HiddenMessages++;
        }
    }

    /// <summary>
    /// The user prompt text of the message just undone, restored into the composer by the
    /// chat page after an undo (TUI parity). Plain field — read from code, not markup.
    /// </summary>
    public string RevertPromptText { get; private set; } = "";

    /// <summary>
    /// The user prompt text of the message just forked, restored into the composer by the
    /// chat page after a fork (TUI parity). Plain field — read from code, not markup.
    /// Set by the router after it switches to the forked session.
    /// </summary>
    public string ForkPromptText { get; internal set; } = "";

    /// <summary>
    /// Sends the prompt. <paramref name="mode"/> overrides the send-mode setting
    /// (<see cref="SettingsStore.SendMode"/>) for a one-shot send (used by the busy-state dropdown's
    /// per-send overrides; it never persists). While a turn is running the effective mode decides:
    ///   - OnNextToolCall (default): send immediately and let the server serialize — prompt_async
    ///     stores the message at once and the running session loop picks it up at the next agent
    ///     step (after the in-flight tool call). Matches the opencode TUI.
    ///   - Queue: hold the prompt in the client-side queue (EnqueuePrompt) and flush it one at a
    ///     time when the session goes idle (DrainPendingPromptsAsync).
    ///   - SendImmediately: interrupt the running turn first (abort), then send — the new prompt
    ///     becomes the active request instead of waiting for the next agent step. The abort POST
    ///     returns once the runner is idle, so the following prompt starts a fresh turn. When idle
    ///     it sends like OnNextToolCall.
    /// </summary>
    public async Task SendAsync(string text, SendPromptMode? mode = null)
        => await SendCoreAsync(text, mode, fromUser: true);

    /// <summary>Send implementation. <paramref name="fromUser"/> distinguishes real user sends
    /// (which reset the auto-continue streak) from the automatic "continue" (which must not).</summary>
    private async Task SendCoreAsync(string text, SendPromptMode? mode, bool fromUser)
    {
        if (fromUser)
        {
            autoContinueStreak = 0;
            autoContinued = false;
        }
        try
        {
            if (!await Router.EnsureSessionAsync()) return;

            var effective = mode ?? SettingsStore.SendMode;
            if (effective == SendPromptMode.Queue && IsBusy)
            {
                EnqueuePrompt(text);
                return;
            }
            if (effective == SendPromptMode.SendImmediately && IsBusy)
                await InterruptAsync();
            await SendPromptNowAsync(text);
        }
        catch (Exception ex)
        {
            Router.ShowError(ex.Message, "Message failed to send");
        }
    }

    /// <summary>
    /// Opens the native file picker and stages the chosen image as a pending attachment.
    /// <paramref name="window"/> is the hosting window used to initialize the picker (WinRT
    /// pickers need an HWND on Windows).
    /// </summary>
    public async Task PickImageAsync(Window window)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker
        {
            SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary,
        };
        foreach (var ext in ImageExtensions)
            picker.FileTypeFilter.Add(ext);
        WindowsHelper.InitializeWithWindow(picker, window);
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
        await AddPendingImageAsync(file.Path);
    }

    /// <summary>Reads an image file from disk and stages it as a pending attachment.</summary>
    public async Task AddPendingImageAsync(string path)
    {
        try
        {
            var bytes = await File.ReadAllBytesAsync(path);
            if (bytes.Length == 0) return;
            StageAttachment(new ImageAttachment
            {
                FileName = Path.GetFileName(path),
                Mime = ImageAttachment.MimeFromPath(path),
                Bytes = bytes,
                Preview = await ImageAttachment.DecodeAsync(bytes),
            });
        }
        catch (Exception ex)
        {
            Router.ShowError(ex.Message, "Could not attach image");
        }
    }

    /// <summary>
    /// Pastes an image from the system clipboard (Ctrl+V). Returns true when at least one
    /// image was staged; false when the clipboard holds no usable image, so the caller can
    /// let the default text paste proceed.
    /// </summary>
    /// <remarks>
    /// Uses Uno's built-in <see cref="Clipboard"/>, probing the union of what each Skia
    /// backend exposes. On X11 it routes to the <c>X11ClipboardExtension</c> (raw
    /// <c>image/png</c>/<c>image/jpeg</c> atoms returning <c>byte[]</c>, files via
    /// <c>text/uri-list</c>); on Windows to the <c>Win32ClipboardExtension</c> (registered
    /// format names like <c>PNG</c>/<c>JFIF</c> returning <c>IRandomAccessStream</c>, CF_DIB
    /// remapped to <c>StandardDataFormats.Bitmap</c>, files via <c>CF_HDROP</c>). Both expose
    /// files under <c>StandardDataFormats.StorageItems</c>, so that check is shared. Only the
    /// read path is needed here; the write path workaround from PocketPic is not required.
    /// </remarks>
    public async Task<bool> PasteImageFromClipboardAsync()
    {
        try
        {
            var content = Clipboard.GetContent();
            if (content is null) return false;

            // Files first: "Shell IDList Array" is the cross-platform storage-items format
            // (X11 maps text/uri-list to it; Win32 maps CF_HDROP to it).
            if (content.Contains(StandardDataFormats.StorageItems))
            {
                var items = await content.GetStorageItemsAsync();
                var staged = false;
                foreach (var item in items)
                {
                    if (item is not StorageFile file) continue;
                    var ext = Path.GetExtension(file.Path).ToLowerInvariant();
                    if (ImageExtensions.Contains(ext))
                    {
                        await AddPendingImageAsync(file.Path);
                        staged = true;
                    }
                }
                if (staged) return true;
            }

            // Raw image bytes: probe the union of format names each platform exposes. The
            // retrieved value may be byte[] (X11), IRandomAccessStream (Win32 registered
            // format), or RandomAccessStreamReference (Win32 CF_DIB).
            foreach (var (name, mime, ext) in ImageClipboardFormats)
            {
                if (!content.Contains(name)) continue;
                var item = await content.GetDataAsync(name);
                byte[]? bytes = item switch
                {
                    byte[] raw => raw,
                    IRandomAccessStream stream => await ReadAllBytes(stream),
                    IRandomAccessStreamReference streamRef => await ReadAllBytes(await streamRef.OpenReadAsync()),
                    _ => null,
                };
                if (bytes is { Length: > 0 })
                {
                    StageAttachment(await ImageAttachment.CreateFromBytesAsync(bytes, mime, $"Pasted image.{ext}"));
                    return true;
                }
            }
        }
        catch
        {
            // Foreign clipboard formats or an unavailable selection should not crash paste.
        }
        return false;
    }

    private static async Task<byte[]> ReadAllBytes(IRandomAccessStream stream)
    {
        // DataReader.LoadAsync is not implemented in Uno (Uno0001), so read the underlying
        // stream instead: AsStreamForRead unwraps the MemoryStream-backed IRandomAccessStream
        // that Uno's clipboard extensions produce (the same pattern Win32ClipboardExtension uses).
        stream.Seek(0);
        using var ms = new MemoryStream();
        await stream.AsStreamForRead().CopyToAsync(ms);
        return ms.ToArray();
    }

    private void StageAttachment(ImageAttachment attachment)
    {
        PendingImages.Add(attachment);
        PendingImageCount = PendingImages.Count;
    }

    /// <summary>Removes a staged image attachment.</summary>
    public void RemovePendingImage(ImageAttachment attachment)
    {
        PendingImages.Remove(attachment);
        PendingImageCount = PendingImages.Count;
    }

    /// <summary>
    /// Interrupts the currently-running turn (aborts in-flight tool calls and the
    /// model loop). Any queued prompts are flushed when the session goes idle.
    /// </summary>
    public async Task InterruptAsync()
    {
        if (SessionId.Length == 0) return;
        try
        {
            await Router.Client.AbortAsync(SessionId);
        }
        catch (Exception ex)
        {
            Router.ShowError(ex.Message, "Stop failed");
        }
    }

    /// <summary>
    /// Renames this session via PATCH /session/{id} and updates the local title.
    /// </summary>
    public async Task RenameSessionAsync(string title)
    {
        title = title.Trim();
        if (SessionId.Length == 0 || title.Length == 0) return;
        try
        {
            await Router.Client.UpdateSessionTitleAsync(SessionId, title);
            SessionTitle = title;
            var session = Router.GetSession(SessionId);
            if (session is not null) session.Title = title;
        }
        catch (Exception ex)
        {
            Router.ShowError(ex.Message, "Rename failed");
        }
    }

    // Client-side prompt queue for the "Queue" send mode (SettingsStore.SendMode):
    // SendAsync enqueues while a turn is busy, and DrainPendingPromptsAsync flushes the queue
    // one prompt at a time when the session goes idle. The "OnNextToolCall" mode skips the
    // queue entirely and sends immediately (the server serializes prompts itself).
    private void EnqueuePrompt(string text)
    {
        _pendingPrompts.Enqueue(text);
        PendingPrompts = _pendingPrompts.Count;
    }

    private void ClearPendingPrompts()
    {
        _pendingPrompts.Clear();
        PendingPrompts = 0;
    }

    private async Task SendPromptNowAsync(string text)
    {
        // Slash-command send (opencode Commands): when the input starts with "/name" and the
        // server knows that command for the active directory, route it through
        // POST /session/{id}/command so the server expands the template ($ARGUMENTS/$1..,
        // !`shell`, @file) and runs it with the command's own options — instead of sending the
        // verbatim text (which the session loop does NOT expand). Unknown slash text still sends
        // as a normal prompt, matching the TUI/web clients.
        if (ParseSlashCommand(text) is { } cmd && await Router.IsKnownCommandAsync(cmd.Name))
        {
            SendCommandNow(cmd.Name, cmd.Arguments);
            return;
        }

        // Mark busy optimistically so interleaved SendAsync calls queue instead of
        // racing the HTTP call; the server's session.status busy event confirms it.
        IsBusy = true;
        ResetTurnFlags();
        var images = PendingImages.ToArray();
        await Router.Client.SendPromptAsync(SessionId, text, images, Mode, ProviderId, ModelId, Variant);
        // Attachments travel with the prompt, so stage them off once the message is stored.
        PendingImages.Clear();
        PendingImageCount = 0;
    }

    /// <summary>
    /// Parses slash-command input. Returns <c>(name, arguments)</c> when the text starts with
    /// <c>/</c>, else null. The command name is the first line's first space-delimited token with
    /// the leading <c>/</c> stripped; the arguments are the rest of that line (space-joined with
    /// empty tokens preserved, so whitespace runs round-trip verbatim — no quote/escape parsing,
    /// exactly like the TUI/web clients) plus any trailing lines. Mirrors the TUI's
    /// <c>prompt/index.tsx</c> command parsing.
    /// </summary>
    private static (string Name, string Arguments)? ParseSlashCommand(string text)
    {
        if (string.IsNullOrEmpty(text) || text[0] != '/') return null;

        var newline = text.IndexOf('\n');
        var firstLine = newline < 0 ? text : text.Substring(0, newline);
        var tokens = firstLine.Split(' ');
        if (tokens.Length == 0) return null;

        var name = tokens[0].TrimStart('/');
        if (name.Length == 0) return null;

        var arguments = string.Join(" ", tokens.Skip(1));
        if (newline >= 0) arguments += "\n" + text.Substring(newline + 1);
        return (name, arguments);
    }

    /// <summary>
    /// Fires POST /session/{id}/command as a detached request. The endpoint runs the whole command
    /// server-side and blocks until its turn ends, but all progress arrives over the SSE stream,
    /// so the request is fire-and-forget (TUI parity) — the composer clears immediately and the
    /// same busy/status plumbing drives the UI as for a normal prompt. Errors (e.g. the command
    /// vanished server-side) are surfaced as an error toast.
    /// </summary>
    private void SendCommandNow(string name, string arguments)
    {
        IsBusy = true;
        ResetTurnFlags();
        var images = PendingImages.ToArray();
        PendingImages.Clear();
        PendingImageCount = 0;

        // Capture the reactive values on the UI thread (Reference<T> must only be read/written
        // there), then run the long-lived request off-thread.
        var sessionId = SessionId;
        var router = Router;
        string mode = Mode;
        string providerId = ProviderId;
        string modelId = ModelId;
        string variant = Variant;
        _ = Task.Run(async () =>
        {
            try
            {
                await router.Client!.SendCommandAsync(sessionId, name, arguments, images,
                    mode, providerId, modelId, variant);
            }
            catch (Exception ex)
            {
                router.PostToUi(() => router.ShowError(ex.Message, "Command failed"));
            }
        });
    }

    /// <summary>
    /// Runs a shell command inside the session (the composer's <c>!</c> shell mode, TUI parity).
    /// Fires POST /session/{id}/shell detached like <see cref="SendCommandNow"/>: the endpoint
    /// blocks until the command exits while all progress — the synthetic user message, the
    /// assistant message with a running <c>bash</c> tool part, and its streaming output —
    /// arrives over the SSE stream and renders through the normal message plumbing. The session
    /// goes busy for the duration (Stop aborts the command); the server 409s a concurrent run,
    /// so a busy session surfaces an error instead of sending.
    /// </summary>
    public async Task SendShellAsync(string command)
    {
        try
        {
            if (!await Router.EnsureSessionAsync()) return;
            if (IsBusy)
            {
                Router.ShowWarning("Wait for the current turn to finish before running a shell command.", "Session busy");
                return;
            }

            // Mark busy optimistically so a second shell submit can't race the HTTP call;
            // the server's session.status busy event confirms it.
            IsBusy = true;
            ResetTurnFlags();

            // Capture the reactive values on the UI thread (Reference<T> must only be read/written
            // there), then run the long-lived request off-thread.
            var sessionId = SessionId;
            var router = Router;
            string mode = Mode;
            string providerId = ProviderId;
            string modelId = ModelId;
            _ = Task.Run(async () =>
            {
                try
                {
                    await router.Client!.SendShellAsync(sessionId, command, mode, providerId, modelId);
                }
                catch (Exception ex)
                {
                    router.PostToUi(() =>
                    {
                        router.ShowError(ex.Message, "Shell command failed");
                        // A failed request means no run started (e.g. the server's 409
                        // concurrent-run rejection), so no session.status idle event will
                        // arrive to unstick the composer's busy state.
                        IsBusy = false;
                    });
                }
            });
        }
        catch (Exception ex)
        {
            Router.ShowError(ex.Message, "Shell command failed");
        }
    }

    private bool _draining;

    /// <summary>Drains queued prompts one at a time. Called when the session goes idle.</summary>
    private async Task DrainPendingPromptsAsync()
    {
        if (_draining || IsBusy || _pendingPrompts.Count == 0) return;
        _draining = true;
        try
        {
            while (!IsBusy && _pendingPrompts.Count > 0)
            {
                var text = _pendingPrompts.Dequeue();
                PendingPrompts = _pendingPrompts.Count;
                try
                {
                    await SendPromptNowAsync(text);
                }
                catch (Exception ex)
                {
                    Router.ShowError(ex.Message, "Queued prompt failed");
                    return;
                }
            }
        }
        finally
        {
            _draining = false;
        }
    }

    /// <summary>
    /// Full (awaited) load of this session's messages + settings. Called by the router the
    /// first time the session is opened. <paramref name="known"/> is the sidebar session
    /// when it's in the list (title/parent/model already known); a null it falls back to
    /// <c>GET /session/:id</c> (e.g. a subagent whose session.created raced the click).
    /// </summary>
    public async Task LoadAsync(SessionInfo? known)
    {
        if (known is not null)
        {
            ApplySessionSettings(known);
            if (known.Title.Length > 0) SessionTitle = known.Title;
            if (known.ParentId.Length > 0) ParentSessionId = known.ParentId;
        }
        else
        {
            await LoadInfoAsync();
        }
        await LoadMessagesAsync();
        await Router.SyncPendingQuestionsAsync();
    }

    /// <summary>Fetches this session's info via GET /session/:id when it isn't in the sidebar list.</summary>
    private async Task LoadInfoAsync()
    {
        try
        {
            var info = await Router.Client.GetSessionAsync(SessionId);
            if (info is not null)
            {
                if (info.Title.Length > 0) SessionTitle = info.Title;
                if (info.ParentId.Length > 0) ParentSessionId = info.ParentId;
                ApplySessionSettings(info);
            }
        }
        catch
        {
            // Fall back to the placeholder title; the message fetch below still works.
        }
    }

    /// <summary>
    /// Background refresh of a cached store's messages (stale-while-revalidate) so a revisit
    /// shows fresh content. Skips the swap while the session is busy, so an in-flight turn's
    /// streaming deltas are never clobbered by a snapshot taken mid-stream.
    /// </summary>
    public async Task RefreshAsync()
    {
        if (Router.IsSessionBusy(SessionId)) return;
        await LoadMessagesAsync();
        await Router.SyncPendingQuestionsAsync();
    }

    /// <summary>Fetches and replaces this store's message list from GET /session/:id.</summary>
    private async Task LoadMessagesAsync()
    {
        try
        {
            Messages.Clear();
            _messagesById.Clear();
            HiddenMessages = 0;
            var root = await Router.Client.GetMessagesAsync(SessionId);
            if (root.ValueKind != JsonValueKind.Array) return;
            foreach (var msg in root.EnumerateArray())
            {
                var message = MessageFromJson(msg);
                if (message is null) continue;
                _messagesById[message.Id] = message;
                AppendMessage(message);
            }
            UpdateSessionStats();
        }
        catch (Exception ex)
        {
            Router.ShowError(ex.Message, "Could not load messages");
        }
    }

    /// <summary>
    /// Undoes the agent's reply to the last user message. Mirrors the TUI's <c>session.undo</c>
    /// command: aborts if the session is busy (the server 409s a revert while busy), targets the
    /// last user message before the current revert point (so a second undo walks further back),
    /// calls POST /session/{id}/revert, and restores the undone user prompt (text + staged
    /// images) into the composer. No message refetch is needed — the server keeps reverted
    /// messages until the next prompt, and the chat page hides messages at/after the revert
    /// point via <see cref="RevertMessageId"/>.
    /// </summary>
    public async Task UndoLastMessageAsync()
    {
        if (SessionId.Length == 0) return;
        var target = FindUndoTargetMessage();
        if (target is null) return;
        await RevertToMessageAsync(target);
    }

    /// <summary>
    /// Reverts the conversation to a specific user message ("undo to here"), mirroring the web
    /// client's per-message revert action and the TUI's message dialog "Revert". Aborts if the
    /// session is busy, calls POST /session/{id}/revert for the target message, and restores
    /// that message's prompt (text + staged images) into the composer. Messages at/after the
    /// target are hidden via <see cref="RevertMessageId"/> (the target itself included).
    /// </summary>
    public async Task RevertToMessageAsync(MessageItem message)
    {
        if (SessionId.Length == 0 || message is null) return;
        try
        {
            if (IsBusy) await Router.Client.AbortAsync(SessionId);

            await Router.Client.RevertAsync(SessionId, message.Id);

            RestorePromptFromMessage(message);
            ApplyRevertMarker(message.Id);
        }
        catch (Exception ex)
        {
            Router.ShowError(ex.Message, "Revert failed");
        }
    }

    /// <summary>
    /// Restores reverted messages. If a user message exists beyond the revert point, reverts
    /// forward to it; otherwise clears the revert entirely (unrevert). Mirrors the TUI's
    /// <c>session.redo</c> command.
    /// </summary>
    public async Task RedoLastMessageAsync()
    {
        if (SessionId.Length == 0 || RevertMessageId.Length == 0) return;
        try
        {
            var next = Messages
                .Where(m => m.Role == "user" && StringComparer.Ordinal.Compare(m.Id, RevertMessageId) > 0)
                .OrderBy(m => m.Id, StringComparer.Ordinal)
                .FirstOrDefault();
            if (next is null)
            {
                await Router.Client.UnrevertAsync(SessionId);
                ResetRevertState();
                return;
            }

            await Router.Client.RevertAsync(SessionId, next.Id);
            ApplyRevertMarker(next.Id);
        }
        catch (Exception ex)
        {
            Router.ShowError(ex.Message, "Unrevert failed");
        }
    }

    /// <summary>
    /// The next undo target: the last user message strictly before the current revert point
    /// (a second undo walks further back), or the last user message overall when nothing is
    /// reverted yet. Null when there is nothing left to undo.
    /// </summary>
    private MessageItem? FindUndoTargetMessage()
    {
        for (var i = Messages.Count - 1; i >= 0; i--)
        {
            var m = Messages[i];
            if (m.Role != "user") continue;
            if (RevertMessageId.Length > 0 && StringComparer.Ordinal.Compare(m.Id, RevertMessageId) >= 0) continue;
            return m;
        }
        return null;
    }

    /// <summary>Sets the revert point and recomputes the card label.</summary>
    private void ApplyRevertMarker(string messageId)
    {
        RevertMessageId = messageId;
        RevertCountLabel = ComputeRevertCountLabel(messageId);
    }

    /// <summary>
    /// "N message(s) reverted" — counts the reverted user messages (id &gt;= the revert
    /// point, both user and assistant messages are hidden from view but only user messages
    /// are counted, matching the TUI's reverted-count).
    /// </summary>
    private string ComputeRevertCountLabel(string messageId)
    {
        if (messageId.Length == 0) return "";
        var count = Messages.Count(m => m.Role == "user" && StringComparer.Ordinal.Compare(m.Id, messageId) >= 0);
        return count == 1 ? "1 message reverted" : $"{count} messages reverted";
    }

    /// <summary>Clears the undo state. Called on connect / new / switch / delete / unrevert.</summary>
    private void ResetRevertState()
    {
        RevertMessageId = "";
        RevertCountLabel = "";
        RevertPromptText = "";
        ForkPromptText = "";
        autoContinueStreak = 0;
        autoContinued = false;
    }

    /// <summary>
    /// Restores the undone user message's prompt into the composer: concatenated non-synthetic
    /// text parts (TUI skips synthetic) plus its data-URL image file parts re-staged as pending
    /// attachments. Matches the TUI/web undo behavior.
    /// </summary>
    private void RestorePromptFromMessage(MessageItem message)
    {
        RevertPromptText = PromptTextFromMessage(message);
        StageImagesFromMessage(message);
    }

    /// <summary>Concatenates a message's non-synthetic text parts into a composer prompt (TUI parity).</summary>
    internal static string PromptTextFromMessage(MessageItem message)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var part in message.Parts)
        {
            if (part.Type == "text" && !part.Synthetic) sb.Append(part.Text);
        }
        return sb.ToString();
    }

    /// <summary>Re-stages a message's data-URL image file parts as pending attachments.</summary>
    internal void StageImagesFromMessage(MessageItem message)
    {
        PendingImages.Clear();
        PendingImageCount = 0;
        foreach (var part in message.Parts)
        {
            if (part.Type != "file") continue;
            var attachment = AttachmentFromPart(part);
            if (attachment is null) continue;
            PendingImages.Add(attachment);
            PendingImageCount = PendingImages.Count;
        }
    }

    /// <summary>Rebuilds an <see cref="ImageAttachment"/> from a data-URL image file part; null when not decodable.</summary>
    private static ImageAttachment? AttachmentFromPart(PartItem part)
    {
        if (!part.IsImage || !part.Url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return null;
        var comma = part.Url.IndexOf(',');
        if (comma < 0) return null;
        try
        {
            var bytes = Convert.FromBase64String(part.Url.Substring(comma + 1));
            var attachment = new ImageAttachment
            {
                FileName = part.FileName.Length > 0 ? part.FileName : "attachment",
                Mime = part.Mime.Length > 0 ? part.Mime : "image/png",
                Bytes = bytes,
            };
            // Decode fire-and-forget like PartItem.LoadImageAsync; the await resumes on the
            // UI thread so the thumbnail strip updates once the bitmap is ready.
            _ = DecodePreviewAsync(attachment);
            return attachment;
        }
        catch
        {
            return null;
        }
    }

    private static async Task DecodePreviewAsync(ImageAttachment attachment)
    {
        attachment.Preview = await ImageAttachment.DecodeAsync(attachment.Bytes);
    }

    private static MessageItem? MessageFromJson(JsonElement msg)
    {
        if (!msg.TryGetProperty("info", out var info) || info.GetStringProperty("id").Length == 0) return null;
        var item = new MessageItem
        {
            Id = info.GetStringProperty("id"),
            Role = info.GetStringProperty("role"),
            Agent = info.GetStringProperty("agent"),
        };
        ApplyMessageStats(item, info);
        if (msg.TryGetProperty("parts", out var parts) && parts.ValueKind == JsonValueKind.Array)
        {
            foreach (var part in parts.EnumerateArray())
            {
                if (part.GetStringProperty("type") is "step-start" or "step-finish") continue;
                var p = PartFromJson(part);
                if (p.Type == "text" && p.Synthetic) continue;
                if (p.Id.Length > 0) item.Parts.Add(p);
            }
        }
        if (IsAbortedError(info) && item.Parts.All(p => p.Type != "aborted"))
        {
            item.Interrupted = true;
            item.Parts.Add(new PartItem { Id = $"aborted-{item.Id}", MessageId = item.Id, Type = "aborted" });
        }
        else
        {
            ApplyMessageError(item, info);
        }
        if (item.Role == "user" && item.Parts.Count == 0) return null;
        LoadPartImages(item);
        return item;
    }

    internal void ApplyMessageUpdated(JsonElement properties)
    {
        if (!properties.TryGetProperty("info", out var info)) return;
        var id = info.GetStringProperty("id");
        if (id.Length == 0) return;

        if (_messagesById.TryGetValue(id, out var message))
        {
            var role = info.GetStringProperty("role");
            if (role.Length > 0) message.Role = role;
            ApplyMessageStats(message, info);
            MarkInterrupted(message, info);
            ApplyMessageError(message, info);
            // The server emits session.status idle and this final message.updated (carrying
            // finish/error) in either order; both are turn-stop signals handled uniformly
            // (auto-continue or the Continue button). While an auto-continue is awaiting its
            // restarted turn, a trailing finish echo must not clobber the fresh busy state.
            if (!AwaitingAutoContinueRun && info.TryGetProperty("finish", out _)) OnTurnCompleted();
            if (!IsBusy) HandleStoppedTurn();
            UpdateSessionStats();
            return;
        }

        message = new MessageItem
        {
            Id = id,
            Role = info.GetStringProperty("role"),
            Agent = info.GetStringProperty("agent"),
        };
        ApplyMessageStats(message, info);
        MarkInterrupted(message, info);
        ApplyMessageError(message, info);
        if (!AwaitingAutoContinueRun && info.TryGetProperty("finish", out _)) OnTurnCompleted();
        if (!IsBusy) HandleStoppedTurn();
        _messagesById[id] = message;
        AppendMessage(message);
        UpdateSessionStats();
    }

    /// <summary>Appends a reactive "aborted" marker part when the message carries an abort error.</summary>
    private static void MarkInterrupted(MessageItem message, JsonElement info)
    {
        if (!IsAbortedError(info)) return;
        message.Interrupted = true;
        if (message.Parts.Any(p => p.Type == "aborted")) return;
        message.Parts.Add(new PartItem
        {
            Id = $"aborted-{Guid.NewGuid():N}",
            MessageId = message.Id,
            Type = "aborted",
        });
    }

    private static bool IsAbortedError(JsonElement info)
    {
        if (!info.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object) return false;
        return error.GetStringProperty("name") == "MessageAbortedError";
    }

    /// <summary>
    /// Classifies how an assistant message's turn ended: "success" (no error), "interrupted"
    /// (<c>MessageAbortedError</c> — user stopped it), or "error" (any other error). Mirrors
    /// the opencode web client's turn-outcome logic (rows.ts interrupted/error detection).
    /// </summary>
    internal static string ClassifyMessageOutcome(JsonElement info)
    {
        if (!info.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object)
            return "success";
        return error.GetStringProperty("name") == "MessageAbortedError" ? "interrupted" : "error";
    }

    /// <summary>
    /// True when this session's most recent assistant message carries a non-interrupt
    /// error part (i.e. the last turn stopped with an error). Interrupts are MessageAbortedError
    /// → aborted part, not an error part, so they never qualify.
    /// </summary>
    private bool LastAssistantMessageErrored()
    {
        for (var i = Messages.Count - 1; i >= 0; i--)
        {
            var m = Messages[i];
            if (m.Role != "assistant") continue;
            return m.Parts.Any(p => p.Type == "error");
        }
        return false;
    }

    /// <summary>
    /// True when this session's most recent assistant message ends on a "reasoning" (thinking)
    /// part — the visible chat ends on a Thinking block. A turn that stops while the model is
    /// still thinking (stream failure mid-reasoning, or a reasoning-only finish) often leaves
    /// no error part to latch onto, so this catches the case <see cref="LastAssistantMessageErrored"/>
    /// misses.
    /// </summary>
    private bool LastAssistantMessageEndsOnThinking()
    {
        for (var i = Messages.Count - 1; i >= 0; i--)
        {
            var m = Messages[i];
            if (m.Role != "assistant") continue;
            if (m.Parts.Count == 0) return false;
            return m.Parts[^1].Type == "reasoning";
        }
        return false;
    }

    /// <summary>
    /// True when the stopped turn warrants the end-of-chat "Continue" button: the last assistant
    /// message carries an error part, or the chat ends on a Thinking (reasoning) part. Aborted
    /// turns never qualify (interrupt → "aborted" part, not an error part or a trailing Thinking).
    /// </summary>
    private bool ShouldShowContinue() =>
        LastAssistantMessageErrored() || LastAssistantMessageEndsOnThinking();

    /// <summary>
    /// Router-facing decision made BEFORE a session.status event is applied: whether an idle
    /// transition for this session will be swallowed by an automatic "continue" send (so the
    /// router suppresses the completion toast and sidebar unread/outcome flags for it), or has
    /// just been (<see cref="AwaitingAutoContinueRun"/> — echoes of that stop). Mirrors the check
    /// <see cref="HandleStoppedTurn"/> makes moments later on the same message list.
    /// </summary>
    internal bool WillAutoContinue() =>
        SettingsStore.AutoContinueOnThinking
        && autoContinueStreak < MaxAutoContinues
        && !AwaitingAutoContinueRun
        && !LastAssistantMessageInterrupted()
        && LastAssistantMessageEndsOnThinking();

    /// <summary>
    /// True when this session's most recent assistant message was interrupted by the user
    /// (abort). Guards the auto-continue against a stop signal racing the aborted part: a user
    /// Stop must never be answered with an automatic "continue".
    /// </summary>
    private bool LastAssistantMessageInterrupted()
    {
        for (var i = Messages.Count - 1; i >= 0; i--)
        {
            var m = Messages[i];
            if (m.Role != "assistant") continue;
            return m.Interrupted || m.Parts.Any(p => p.Type == "aborted");
        }
        return false;
    }

    /// <summary>
    /// Handles a stopped turn (session.status idle, or a message.updated carrying finish when the
    /// turn is already stopped): fires the automatic "continue" when the setting is on and the
    /// chat ends on a Thinking part, otherwise surfaces the Continue button as before. Echoes of
    /// an already-auto-continued stop are ignored, and the streak cap hands control back to the
    /// manual Continue button.
    /// </summary>
    private void HandleStoppedTurn()
    {
        if (AwaitingAutoContinueRun) return;
        if (autoContinued) autoContinued = false; // the restarted turn's own stop — decide fresh

        if (SettingsStore.AutoContinueOnThinking && !LastAssistantMessageInterrupted()
            && LastAssistantMessageEndsOnThinking() && autoContinueStreak < MaxAutoContinues)
        {
            autoContinued = true;
            sawRunningStatus = false;
            autoContinueStreak++;
            _ = SendCoreAsync("""
                continue
                <continue_metadata>
                If you have already finished your task, end the turn with a non-reasoning message instead.
                </continue_metadata>
                """, null, fromUser: false);
            return;
        }

        autoContinueStreak = 0;
        ShowContinue = ShouldShowContinue();
    }

    /// <summary>
    /// Adds a reactive "error" part when the message carries a non-abort error (e.g. a
    /// streaming failure like <c>"Streaming response failed: [503] The request queue is full."</c>).
    /// Aborts are rendered via the interrupted path instead.
    /// </summary>
    private static void ApplyMessageError(MessageItem message, JsonElement info)
    {
        if (!info.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object) return;
        var name = error.GetStringProperty("name");
        if (name == "MessageAbortedError") return;
        if (message.Parts.Any(p => p.Type == "error")) return;

        message.Parts.Add(new PartItem
        {
            Id = $"error-{message.Id}-{Guid.NewGuid():N}",
            MessageId = message.Id,
            Type = "error",
            ErrorName = name,
            ErrorMessage = UnwrapErrorMessage(error),
        });
    }

    private static string UnwrapErrorMessage(JsonElement error)
    {
        string message = "";
        if (error.TryGetProperty("data", out var data))
        {
            if (data.ValueKind == JsonValueKind.String)
                message = data.GetString() ?? "";
            else if (data.ValueKind == JsonValueKind.Object)
                message = data.GetStringProperty("message");
        }

        message = message.Trim();
        if (message.Length >= 2 && message[0] == '"' && message[^1] == '"')
            message = message.Substring(1, message.Length - 2);
        return message;
    }

    /// <summary>Runs when the active turn ends (message finished or session idle).</summary>
    private void OnTurnCompleted()
    {
        IsBusy = false;
        _ = DrainPendingPromptsAsync();
    }

    /// <summary>
    /// Clears the end-of-chat retry card and "Continue" state. Called when the active session
    /// is reset (connect / new / switch / deleted) and before sending a new prompt.
    /// </summary>
    private void ResetTurnFlags()
    {
        ShowContinue = false;
        IsRetrying = false;
        RetryMessage = "";
        RetryAttempt = 0;
        RetryNextMs = 0;
        RetryCountdown = "";
    }

    /// <summary>
    /// Recomputes the live countdown for the end-of-chat retry card. The chat page ticks this
    /// once per second while a turn is auto-retrying (<see cref="RetryNextMs"/> is the absolute
    /// unix-ms time the server will fire the next attempt at).
    /// </summary>
    public void UpdateRetryCountdown()
    {
        if (!IsRetrying)
        {
            RetryCountdown = "";
            return;
        }
        if (RetryNextMs <= 0)
        {
            RetryCountdown = RetryAttempt > 0 ? $"Attempt #{RetryAttempt} · retrying…" : "Retrying…";
            return;
        }
        var seconds = Math.Max(0, (int)Math.Ceiling((RetryNextMs - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) / 1000.0));
        RetryCountdown = RetryAttempt > 0 ? $"Attempt #{RetryAttempt} · retrying in {seconds}s" : $"Retrying in {seconds}s";
    }

    internal void ApplyPartUpdated(JsonElement properties)
    {
        if (!properties.TryGetProperty("part", out var part)) return;
        if (!_messagesById.TryGetValue(part.GetStringProperty("messageID"), out var message)) return;

        var partId = part.GetStringProperty("id");
        var existing = message.Parts.FirstOrDefault(p => p.Id == partId);
        if (existing is null)
        {
            if (part.GetStringProperty("type") is "step-start" or "step-finish") return;
            var p = PartFromJson(part);
            if (p.Synthetic && p.Type == "text" && message.Parts.Count == 0)
            {
                Messages.Remove(message);
                return;
            }
            message.Parts.Add(p);
            _ = p.LoadImageAsync();
            return;
        }

        UpdatePart(existing, part);
    }

    internal void ApplyPartDelta(JsonElement properties)
    {
        var messageId = properties.GetStringProperty("messageID");
        var partId = properties.GetStringProperty("partID");
        var field = properties.GetStringProperty("field");
        var delta = properties.GetStringProperty("delta");
        if (field != "text" || delta.Length == 0) return;
        if (!_messagesById.TryGetValue(messageId, out var message)) return;

        var part = message.Parts.FirstOrDefault(p => p.Id == partId);
        if (part is null) return;
        part.Text += delta;
    }

    internal void ApplyPartRemoved(JsonElement properties)
    {
        var messageId = properties.GetStringProperty("messageID");
        var partId = properties.GetStringProperty("partID");
        if (!_messagesById.TryGetValue(messageId, out var message)) return;

        var part = message.Parts.FirstOrDefault(p => p.Id == partId);
        if (part is not null) message.Parts.Remove(part);
    }

    /// <summary>
    /// Drops a message from the UI. The server emits this when a reverted session's messages
    /// are cleaned up at the start of the next prompt (SessionRevert.cleanup), and for other
    /// message removals. The message is scoped to this session (the router dispatches by
    /// sessionID), so only this store's list is touched.
    /// </summary>
    internal void ApplyMessageRemoved(JsonElement properties)
    {
        var id = properties.GetStringProperty("messageID");
        if (id.Length == 0) return;
        if (!_messagesById.TryGetValue(id, out var message)) return;

        _messagesById.Remove(id);
        Messages.Remove(message);
        UpdateSessionStats();
    }

    /// <summary>
    /// Applies a <c>session.status</c> event for THIS session only (the router forwards it).
    /// Handles the active banner (busy/retry) and the Continue button; sidebar maps are owned
    /// by the router.
    /// </summary>
    internal void ApplySessionStatus(JsonElement properties)
    {
        if (!properties.TryGetProperty("status", out var status)) return;
        var type = status.GetStringProperty("type");

        IsBusy = type != "idle";
        if (type != "idle") sawRunningStatus = true;

        if (type == "retry")
        {
            var message = status.GetStringProperty("message");
            var attempt = status.GetInt64Property("attempt");
            var next = status.GetInt64Property("next");
            var prefix = attempt > 0 ? $"Retry #{attempt}" : "Retry";
            StatusMessage = message.Length > 0 ? $"{prefix}: {message}" : prefix;

            IsRetrying = true;
            RetryMessage = message;
            RetryAttempt = (int)attempt;
            RetryNextMs = next;
            UpdateRetryCountdown();
        }
        else
        {
            StatusMessage = "";
            IsRetrying = false;
            RetryMessage = "";
            RetryAttempt = 0;
            RetryNextMs = 0;
            RetryCountdown = "";

            // The turn finished. If it stopped because of a non-interrupt error, or with the
            // chat left ending on a Thinking part, surface the "Continue" button — or, when the
            // auto-continue-on-thinking-stop setting is on and the stop qualifies, send the
            // "continue" prompt instead (HandleStoppedTurn). (Interrupts are MessageAbortedError
            // → aborted part instead.)
            if (type == "idle") HandleStoppedTurn();
        }

        if (!IsBusy) _ = DrainPendingPromptsAsync();
    }

    /// <summary>
    /// Re-syncs the pending-question request IDs for this session's tool parts after a reload
    /// (requestIDs only exist in the live question.asked event and the server's in-memory
    /// pending map, not in the persisted message parts).
    /// </summary>
    internal void AttachQuestionRequest(JsonElement question)
    {
        if (!question.TryGetProperty("tool", out var tool)) return;
        var messageId = tool.GetStringProperty("messageID");
        var callId = tool.GetStringProperty("callID");
        if (messageId.Length == 0 || callId.Length == 0) return;
        if (!_messagesById.TryGetValue(messageId, out var message)) return;

        var part = message.Parts.FirstOrDefault(p => p.CallId == callId && p.ToolName == "question");
        if (part is null || part.QuestionRequestId.Length > 0) return;

        AttachQuestion(part, question.GetStringProperty("id"), question);
    }

    /// <summary>Applies a live <c>question.asked</c> event to this session's tool part.</summary>
    internal void ApplyQuestionAsked(JsonElement properties)
    {
        var requestId = properties.GetStringProperty("id");
        if (requestId.Length == 0) return;

        if (!properties.TryGetProperty("tool", out var tool)) return;
        var messageId = tool.GetStringProperty("messageID");
        var callId = tool.GetStringProperty("callID");
        if (messageId.Length == 0 || callId.Length == 0) return;

        if (!_messagesById.TryGetValue(messageId, out var message)) return;
        var part = message.Parts.FirstOrDefault(p => p.CallId == callId);
        if (part is null) return;

        AttachQuestion(part, requestId, properties);
    }

    private static void AttachQuestion(PartItem part, string requestId, JsonElement properties)
    {
        part.QuestionRequestId = requestId;
        if (properties.TryGetProperty("questions", out var questions) && questions.ValueKind == JsonValueKind.Array)
        {
            part.QuestionJson = JsonSerializer.Serialize(questions, AppJsonContext.Default.JsonElement);
            PopulateQuestionForm(part, questions);
        }
    }

    /// <summary>
    /// Applies a <c>session.updated</c>/<c>session.created</c> event's info to this store:
    /// keeps the title, parent id and model settings current, and syncs the revert marker
    /// (the server omits "revert" entirely on unrevert).
    /// </summary>
    internal void ApplySessionInfo(SessionInfo session, JsonElement info)
    {
        if (session.Title.Length > 0) SessionTitle = session.Title;
        if (session.ParentId.Length > 0) ParentSessionId = session.ParentId;
        if (session.ModelId.Length > 0)
        {
            ModelId = session.ModelId;
            ProviderId = session.ModelProviderId;
            UpdateVariantOptions();
            Variant = session.ModelVariant is "" or "default" ? "Default" : session.ModelVariant;
            ReapplyComboSelections();
        }

        var revertMessageId = "";
        if (info.TryGetProperty("revert", out var revert) && revert.ValueKind == JsonValueKind.Object)
            revertMessageId = revert.GetStringProperty("messageID");
        if (revertMessageId != RevertMessageId)
        {
            if (revertMessageId.Length == 0) RevertPromptText = "";
            ApplyRevertMarker(revertMessageId);
        }
    }

    private static void PopulateQuestionForm(PartItem item, JsonElement questions)
    {
        item.QuestionForm.Clear();
        foreach (var q in questions.EnumerateArray())
        {
            var form = new QuestionFormItem
            {
                Question = q.GetStringProperty("question"),
                Header = q.GetStringProperty("header"),
                AllowCustom = q.GetBoolProperty("custom", true),
                Multiple = q.GetBoolProperty("multiple", false),
            };

            if (q.TryGetProperty("options", out var options) && options.ValueKind == JsonValueKind.Array)
            {
                foreach (var opt in options.EnumerateArray())
                {
                    form.Options.Add(new QuestionOptionItem
                    {
                        Label = opt.GetStringProperty("label"),
                        Description = opt.GetStringProperty("description"),
                    });
                }
            }

            item.QuestionForm.Add(form);
        }
    }

    private static PartItem PartFromJson(JsonElement part)
    {
        var item = new PartItem
        {
            Id = part.GetStringProperty("id"),
            MessageId = part.GetStringProperty("messageID"),
            CallId = part.GetStringProperty("callID"),
            Type = part.GetStringProperty("type"),
        };

        if (item.Type is "text" or "reasoning" && part.TryGetProperty("text", out var text))
            item.Text = text.GetString() ?? "";

        item.Synthetic = part.GetBoolProperty("synthetic", false);

        if (item.Type == "reasoning" && part.TryGetProperty("time", out var time))
            item.Time = ParsePartTime(time);

        if (item.Type == "tool")
        {
            item.ToolName = part.GetStringProperty("tool");
            ApplyToolState(item, part);
        }

        if (item.Type == "file")
        {
            item.Mime = part.GetStringProperty("mime");
            item.Url = part.GetStringProperty("url");
            item.FileName = part.GetStringProperty("filename") != "" ? part.GetStringProperty("filename") : item.Url;
        }

        if (part.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array)
            item.Files = files.EnumerateArray().Select(f => f.GetString() ?? "").Where(f => f.Length > 0).ToArray();

        return item;
    }

    private static void ApplyToolState(PartItem item, JsonElement part)
    {
        if (part.TryGetProperty("state", out var state))
        {
            item.ToolStatus = state.GetStringProperty("status");
            var title = state.GetStringProperty("title");
            if (title.Length > 0) item.ToolTitle = title;            if (state.TryGetProperty("input", out var input) && input.ValueKind == JsonValueKind.Object)
            {
                var serialized = JsonSerializer.Serialize(input, AppJsonContext.Default.JsonElement);
                if (serialized != "{}") item.ToolInput = serialized;
                if (input.TryGetProperty("command", out var command)) item.ToolCommand = command.GetString() ?? "";
                if (input.TryGetProperty("filePath", out var filePath)) item.ToolFilePath = filePath.GetString() ?? "";
                if (input.TryGetProperty("content", out var content)) item.ToolContent = content.GetString() ?? "";
                if (input.TryGetProperty("pattern", out var pattern)) item.ToolPattern = pattern.GetString() ?? "";
                if (input.TryGetProperty("path", out var searchPath)) item.ToolSearchPath = searchPath.GetString() ?? "";
                if (input.TryGetProperty("include", out var include)) item.ToolInclude = include.GetString() ?? "";
                if (input.TryGetProperty("workdir", out var workdir)) item.ToolWorkdir = workdir.GetString() ?? "";
                if (input.TryGetProperty("url", out var url)) item.ToolUrl = url.GetString() ?? "";
                if (input.TryGetProperty("name", out var skillName)) item.ToolSkillName = skillName.GetString() ?? "";
                if (input.TryGetProperty("subagent_type", out var subType)) item.ToolSubagentType = subType.GetString() ?? "";
                if (input.TryGetProperty("todos", out var todos) && todos.ValueKind == JsonValueKind.Array)
                    item.TodoJson = JsonSerializer.Serialize(todos, AppJsonContext.Default.JsonElement);
                if (input.TryGetProperty("questions", out var questions) && questions.ValueKind == JsonValueKind.Array)
                {
                    item.QuestionJson = JsonSerializer.Serialize(questions, AppJsonContext.Default.JsonElement);
                    PopulateQuestionForm(item, questions);
                }
            }
            if (state.TryGetProperty("output", out var output))
                item.ToolOutput = output.GetString() ?? "";
            if (state.TryGetProperty("error", out var error))
                item.ToolError = error.GetString() ?? "";
            if (state.TryGetProperty("metadata", out var meta) && meta.ValueKind == JsonValueKind.Object)
            {
                if (meta.TryGetProperty("interrupted", out var interm)) item.Interrupted = interm.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.String => interm.GetString() == "true",
                    _ => item.Interrupted,
                };
                if (meta.TryGetProperty("output", out var mOutput)) item.ShellOutput = mOutput.GetString() ?? "";
                if (meta.TryGetProperty("diff", out var mDiff)) item.Diff = mDiff.GetString() ?? "";
                if (meta.TryGetProperty("count", out var mCount)) item.MatchCount = mCount.ToString();
                if (meta.TryGetProperty("matches", out var mMatches)) item.MatchCount = mMatches.ToString();
                if (meta.TryGetProperty("loaded", out var mLoaded) && mLoaded.ValueKind == JsonValueKind.Array)
                    item.LoadedFiles = string.Join("\n", mLoaded.EnumerateArray().Select(x => x.GetString() ?? "").Where(s => s.Length > 0));
                if (meta.TryGetProperty("todos", out var mTodos) && mTodos.ValueKind == JsonValueKind.Array)
                    item.TodoJson = JsonSerializer.Serialize(mTodos, AppJsonContext.Default.JsonElement);
                if (meta.TryGetProperty("answers", out var mAnswers) && mAnswers.ValueKind == JsonValueKind.Array)
                    item.AnswerJson = JsonSerializer.Serialize(mAnswers, AppJsonContext.Default.JsonElement);
                // apply_patch records a per-file list here (see apply_patch.ts metadata):
                // { filePath, relativePath, type, patch, additions, deletions, movePath }.
                if (meta.TryGetProperty("files", out var mFiles) && mFiles.ValueKind == JsonValueKind.Array)
                    item.PatchJson = JsonSerializer.Serialize(mFiles, AppJsonContext.Default.JsonElement);
                // The task tool records the spawned subagent session here (see task.ts metadata).
                if (meta.TryGetProperty("sessionId", out var mSession)) item.ToolSessionId = mSession.GetString() ?? "";
                if (meta.TryGetProperty("parentSessionId", out var mParent)) item.ToolParentSessionId = mParent.GetString() ?? "";
            }
        }
    }

    private static void ApplyMessageStats(MessageItem item, JsonElement info)
    {
        item.ModelId = info.GetStringProperty("modelID");
        item.ProviderId = info.GetStringProperty("providerID");
        if (info.TryGetProperty("cost", out var cost) && cost.ValueKind == JsonValueKind.Number)
            item.Cost = cost.GetDouble();
        if (info.TryGetProperty("tokens", out var tokens) && tokens.ValueKind == JsonValueKind.Object)
        {
            item.TokensInput = tokens.GetInt64Property("input");
            item.TokensOutput = tokens.GetInt64Property("output");
            item.TokensReasoning = tokens.GetInt64Property("reasoning");
            if (tokens.TryGetProperty("cache", out var cache) && cache.ValueKind == JsonValueKind.Object)
            {
                item.TokensCacheRead = cache.GetInt64Property("read");
                item.TokensCacheWrite = cache.GetInt64Property("write");
            }
        }
    }

    private void ResetUsageStats()
    {
        UsageCostLabel = "$0.00";
        UsageTokensLabel = "0";
        ContextLabel = "0%";
        ContextUsage = 0;
        UsageTokensInput = 0;
        UsageTokensOutput = 0;
        UsageTokensReasoning = 0;
        UsageTokensCacheRead = 0;
        UsageTokensCacheWrite = 0;
        ContextLimit = 0;
    }

    private void UpdateSessionStats()
    {
        var last = Messages.LastOrDefault(m => m.Role == "assistant" && m.TokensOutput > 0);
        if (last is null)
        {
            ResetUsageStats();
            return;
        }

        var sessionCost = Messages.Where(m => m.Role == "assistant").Sum(m => m.Cost);
        UsageCostLabel = FormatCost(sessionCost);

        UsageTokensInput = last.TokensInput;
        UsageTokensOutput = last.TokensOutput;
        UsageTokensReasoning = last.TokensReasoning;
        UsageTokensCacheRead = last.TokensCacheRead;
        UsageTokensCacheWrite = last.TokensCacheWrite;

        var tokens = last.TokensInput + last.TokensOutput + last.TokensReasoning
            + last.TokensCacheRead + last.TokensCacheWrite;
        UsageTokensLabel = tokens.ToString("N0");

        var limit = ResolveContextLimit(last);
        ContextLimit = limit;
        if (limit > 0)
        {
            var percent = (int)Math.Round(tokens / (double)limit * 100);
            ContextLabel = $"{percent}%";
            ContextUsage = percent;
        }
        else
        {
            ContextLabel = "--";
            ContextUsage = 0;
        }
    }

    private long ResolveContextLimit(MessageItem message)
    {
        var model = Router.ModelOptions.FirstOrDefault(m => m.Id == message.ModelId
            && (message.ProviderId.Length == 0 || m.ProviderId == message.ProviderId));
        model ??= Router.ModelOptions.FirstOrDefault(m => m.Id == ModelId && m.ProviderId == ProviderId);
        return model?.LimitContext ?? 0;
    }

    private static string FormatCost(double cost)
    {
        if (cost <= 0) return "$0.00";
        if (cost < 0.01) return $"${cost:0.####}";
        return $"${cost:F2}";
    }

    private static ReasoningTime ParsePartTime(JsonElement time)
    {
        return new ReasoningTime
        {
            Start = time.GetInt64Property("start"),
            End = time.GetInt64Property("end"),
        };
    }

    private static void UpdatePart(PartItem item, JsonElement part)
    {
        if (item.Type is "text" or "reasoning" && part.TryGetProperty("text", out var text))
            item.Text = text.GetString() ?? "";

        if (item.Type == "reasoning" && part.TryGetProperty("time", out var time))
            item.Time = ParsePartTime(time);

        if (item.Type == "reasoning" || item.Type == "text")
            item.Synthetic = part.GetBoolProperty("synthetic", item.Synthetic);

        if (item.Type == "tool")
            ApplyToolState(item, part);

        if (item.Type == "file")
        {
            item.Mime = part.GetStringProperty("mime");
            item.Url = part.GetStringProperty("url");
            item.FileName = part.GetStringProperty("filename") != "" ? part.GetStringProperty("filename") : item.Url;
        }
    }

    /// <summary>Kicks off async decode of image file parts so message thumbnails render.</summary>
    private static void LoadPartImages(MessageItem item)
    {
        foreach (var part in item.Parts)
        {
            if (part.Type == "file") _ = part.LoadImageAsync();
        }
    }

    // ---------------------------------------------------------------------
    // Mode / model / variant (per-session agent settings).
    // ---------------------------------------------------------------------

    private void ApplySessionSettings(SessionInfo session)
    {
        if (session.Agent.Length > 0) Mode = session.Agent;
        if (session.ModelId.Length > 0)
        {
            ModelId = session.ModelId;
            ProviderId = session.ModelProviderId;
        }
        UpdateVariantOptions();
        Variant = session.ModelVariant is "" or "default" ? "Default" : session.ModelVariant;
        ReapplyComboSelections();
    }

    // Reference.Value only fires when the value changes; the SelectedItem bindings ran once
    // against empty options, so nudge the refs to make the bindings re-apply the selection.
    internal void ReapplyComboSelections()
    {
        var mode = Mode; Mode = ""; Mode = mode;
        var modelId = ModelId; ModelId = ""; ModelId = modelId;
        var variant = Variant; Variant = ""; Variant = variant;
    }

    internal void UpdateVariantOptions()
    {
        Router.VariantOptions.Clear();
        Router.VariantOptions.Add("Default");
        var model = Router.ModelOptions.FirstOrDefault(m => m.Id == ModelId && m.ProviderId == ProviderId);
        if (model is not null)
            foreach (var v in model.Variants) Router.VariantOptions.Add(v);
        HasVariants = model?.Variants.Length > 0;
        if (Variant != "Default" && !Router.VariantOptions.Contains(Variant)) Variant = "Default";
    }

    public void SetMode(string mode)
    {
        if (mode.Length > 0) Mode = mode;
    }

    public void SetModel(string modelId)
    {
        if (modelId.Length == 0 || modelId == ModelId) return;
        var model = Router.ModelOptions.FirstOrDefault(m => m.Id == modelId);
        if (model is null) return;
        ModelId = model.Id;
        ProviderId = model.ProviderId;
        Variant = "Default";
        UpdateVariantOptions();
        ReapplyComboSelections();
    }

    public void SetVariant(string variant)
    {
        Variant = variant.Length == 0 ? "Default" : variant;
    }
}
