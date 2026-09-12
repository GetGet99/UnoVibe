using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using QuickMarkup.Infra.Collections;
using UnoVibe.Integration;
using UnoVibe.Models;
using UnoVibe.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Streams;

namespace UnoVibe.Providers;

[QuickMarkup("""
    public int PendingPrompts;
    public int PendingImageCount;
    """)]
partial class ChatboxModel
{
    public SessionId? SessionId { get; private set; }
    SessionHead? Head => Sessions.Head(SessionId);
    ToastService Toasts { get; set; }
    SessionsSource Sessions { get; set; }
    OpencodeClient Opencode { get; set; }
    DispatcherQueue Dispatcher { get; set; }
    [QuickMarkupConstructor]
    [MemberNotNull(nameof(Toasts), nameof(Opencode), nameof(Sessions))]
    void Ctor(OpencodeClient client, ToastService toasts, SessionsSource sessions, DispatcherQueue dispatcher, SessionId? sessionId)
    {
        Dispatcher = dispatcher;
        Opencode = client;
        Toasts = toasts;
        Sessions = sessions;
        SessionId = sessionId;
    }
    /// <summary>Image attachments staged for the next prompt (shown as thumbnails above the input).</summary>
    public ObservableCollection<ImageAttachment> PendingImages { get; } = new();
    private readonly Queue<string> _pendingPrompts = new();

    private bool _draining;

    /// <summary>Image file extensions accepted by the picker and the clipboard storage-items paste path.</summary>
    private static string[] ImageExtensions => field ??= [.. ImageClipboardFormats.Select(x => x.Ext).Distinct()];

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

#pragma warning disable CS8774 // Member must have a non-null value when exiting.
    [MemberNotNull(nameof(SessionId), nameof(Head))]
    async Task EnsureSessionAsync()
    {
        SessionId ??= (await Sessions.CreateFromPreparedSessionAsync()).Id;
    }
#pragma warning restore CS8774 // Member must have a non-null value when exiting.

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
            await EnsureSessionAsync();

            var effective = mode ?? SettingsStore.SendMode;
            if (effective == SendPromptMode.Queue && Head.IsBusy)
            {
                EnqueuePrompt(text);
                return;
            }
            if (effective == SendPromptMode.SendImmediately && Head.IsBusy)
                await InterruptAsync();
            await SendPromptNowAsync(text);
        }
        catch (Exception ex)
        {
            Toasts.ShowError(ex.Message, "Message failed to send");
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
            Toasts.ShowError(ex.Message, "Could not attach image");
        }
    }

    private async Task SendPromptNowAsync(string text)
    {
        await EnsureSessionAsync();
        // Slash-command send (opencode Commands): when the input starts with "/name" and the
        // server knows that command for the active directory, route it through
        // POST /session/{id}/command so the server expands the template ($ARGUMENTS/$1..,
        // !`shell`, @file) and runs it with the command's own options — instead of sending the
        // verbatim text (which the session loop does NOT expand). Unknown slash text still sends
        // as a normal prompt, matching the TUI/web clients.
        if (ParseSlashCommand(text) is { } cmd && await IsKnownCommandAsync(cmd.Name))
        {
            SendCommandNow(cmd.Name, cmd.Arguments);
            return;
        }

        // Mark busy optimistically so interleaved SendAsync calls queue instead of
        // racing the HTTP call; the server's session.status busy event confirms it.
        Head.IsBusy = true;
        ResetTurnFlags();
        var images = PendingImages.ToArray();
        var chatParams = Head.ChatParams;
        var model = chatParams.Model;
        await Opencode.SendPromptAsync(Head.Id, new()
        {
            Parts = [ToPromptPart(text), ..images.Select(ToPromptPart)],
            Agent = chatParams.Agent,
            Model = model is null ? null : new()
            {
                ProviderID = model.ProviderId,
                ModelID = model.Id
            },
            Variant = chatParams.Variant
        });
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
        if (SessionId is null) throw new NullReferenceException(nameof(SessionId));
        Head!.IsBusy = true;
        ResetTurnFlags();
        var images = PendingImages.ToArray();
        PendingImages.Clear();
        PendingImageCount = 0;

        // Capture the reactive values on the UI thread (Reference<T> must only be read/written
        // there), then run the long-lived request off-thread.
        var sessionId = SessionId;
        ChatParameters chatParams = Head.ChatParams;
        var agent = chatParams.Agent;
        Model? model = chatParams.Model;
        var variant = chatParams.Variant;
        _ = Task.Run(async () =>
        {
            try
            {
                await Opencode.SendCommandAsync(SessionId!, new()
                {

                    Command = name,
                    Arguments = arguments,
                    Parts = images.Length is 0 ? null : [.. images.Select(ToPromptPart)],
                    Agent = agent,
                    Model = model?.Formatted,
                    Variant = variant
                });
            }
            catch (Exception ex)
            {
                Dispatcher.TryEnqueue(() => Toasts.ShowError(ex.Message, "Command failed"));
            }
        });
    }

    /// <summary>Drains queued prompts one at a time. Called when the session goes idle.</summary>
    public async Task DrainPendingAsync()
    {
        await EnsureSessionAsync();
        if (_draining || Head.IsBusy || _pendingPrompts.Count == 0) return;
        _draining = true;
        try
        {
            if (_pendingPrompts.Count > 0)
            {
                var text = _pendingPrompts.Dequeue();
                PendingPrompts = _pendingPrompts.Count;
                try
                {
                    await SendPromptNowAsync(text);
                }
                catch (Exception ex)
                {
                    Toasts.ShowError(ex.Message, "Queued prompt failed");
                    return;
                }
            }
        }
        finally
        {
            _draining = false;
        }
    }

    public bool TurnStopAction(bool meetsContinueCriteria, bool meetsAutoContinueCriteria)
    {

        // TODO: AUTO CONTINUE

        // return true if turn continues
        // return false otherwise
        DrainPendingAsync();
    }


    /// <summary>
    /// Restores the user message's prompt into the composer: concatenated non-synthetic
    /// text parts (TUI skips synthetic) plus its data-URL image file parts re-staged as pending
    /// attachments. Matches the TUI/web undo behavior.
    /// </summary>
    public void ReplaceFromMessage(MessageItem message)
    {
        ForkPromptText = PromptTextFromMessage(message);
        StageImagesFromMessage(message);
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

    private static PromptPart ToPromptPart(string text)
    {
        return new PromptPart
        {
            Type = "text",
            Text = text
        };
    }

    private static PromptPart ToPromptPart(ImageAttachment image)
    {
        return new PromptPart
        {
            Type = "file",
            Mime = image.Mime,
            Filename = image.FileName,
            Url = image.DataUrl,
        };
    }


    /// <summary>Concatenates a message's non-synthetic text parts into a composer prompt (TUI parity).</summary>
    static string PromptTextFromMessage(MessageItem message)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var part in message.Parts)
        {
            if (part.Type == "text" && !part.Synthetic) sb.Append(part.Text);
        }
        return sb.ToString();
    }

    /// <summary>Rebuilds an <see cref="ImageAttachment"/> from a data-URL image file part; null when not decodable.</summary>
    static ImageAttachment? AttachmentFromPart(PartItem part)
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

    /// <summary>
    /// True when <paramref name="name"/> (the input token after the leading <c>/</c>) should be
    /// routed as a command for the active directory: it is a server command/MCP prompt (always),
    /// or a skill when the "Expand skills" setting is on. Fetches/cache-refreshes the command
    /// list on a directory change or staleness; returns false when the server is unreachable so
    /// slash text degrades to a plain prompt.
    /// </summary>
    public async Task<bool> IsKnownCommandAsync(string name)
    {
        if (name.Length == 0) return false;
        var directory = Sessions.ActiveSessionDirectory;
        var now = Environment.TickCount64;
        if (_commandNames is null || _skillNames is null || now - _commandNamesFetchedMs > CommandCacheTtlMs)
        {
            var commands = await Opencode.GetCommandsAsync(directory);
            _commandNames = new HashSet<string>(StringComparer.Ordinal);
            _skillNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var command in commands.GetDataOr(static () => []))
            {
                if (command.Source == "skill") _skillNames.Add(command.Name);
                else _commandNames.Add(command.Name);
            }
            _commandNamesFetchedMs = now;
        }
        if (_commandNames.Contains(name)) return true;
        return SettingsStore.ExpandSkills && _skillNames.Contains(name);
    }



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
    /// Set when the user requests an interrupt (Stop button or an interrupt-then-send) and
    /// cleared when the server confirms the next running turn (first non-idle status). Guards
    /// auto-continue against the stop-signal race: session.status idle can be processed before
    /// the final message.updated lands the MessageAbortedError marker on the assistant message,
    /// so <see cref="LastAssistantMessageInterrupted"/> alone can miss a mid-thinking Stop.
    /// </summary>
    private bool interruptRequested;

    /// <summary>
    /// True between an automatic continue firing and the server confirming the restarted turn
    /// with its first non-idle status event. Any further stop signal for that same stop (the
    /// server emits session.status idle and the final message.updated carrying finish in either
    /// order) is an echo and must neither re-fire nor clobber the fresh turn's busy state.
    /// </summary>
    private bool AwaitingAutoContinueRun => autoContinued && !sawRunningStatus;

    /// <summary>
    /// Interrupts the currently-running turn (aborts in-flight tool calls and the
    /// model loop). Any queued prompts are flushed when the session goes idle.
    /// </summary>
    public async Task InterruptAsync()
    {
        interruptRequested = true;
        try
        {
            await Opencode.AbortAsync(Head.Id);
        }
        catch (Exception ex)
        {
            Toasts.ShowError(ex.Message, "Stop failed");
        }
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
        await EnsureSessionAsync();
        if (Head.IsBusy)
        {
            Toasts.ShowWarning("Wait for the current turn to finish before running a shell command.", "Session busy");
            return;
        }
        try
        {
            // Mark busy optimistically so a second shell submit can't race the HTTP call;
            // the server's session.status busy event confirms it.
            Head.IsBusy = true;
            ResetTurnFlags();

            // Capture the reactive values on the UI thread (Reference<T> must only be read/written
            // there), then run the long-lived request off-thread.
            ChatParameters chatParams = Head.ChatParams;
            var agent = chatParams.Agent;
            Model? model = chatParams.Model;
            
            _ = Task.Run(async () =>
            {
                try
                {
                    await Opencode!.SendShellAsync(Head.Id, new()
                    {
                        Command = command,
                        Agent = agent,
                        Model = model is null ? null : new()
                        {
                            ProviderID = model.ProviderId,
                            ModelID = model.Id
                        }
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.TryEnqueue(() =>
                    {
                        Toasts.ShowError(ex.Message, "Shell command failed");
                        // A failed request means no run started (e.g. the server's 409
                        // concurrent-run rejection), so no session.status idle event will
                        // arrive to unstick the composer's busy state.
                        Head.IsBusy = false;
                    });
                }
            });
        }
        catch (Exception ex)
        {
            Toasts.ShowError(ex.Message, "Shell command failed");
        }
    }



    // ── Slash-command send detection ─────────────────────────────────────────────
    // The composer routes "/name args" through POST /session/{id}/command (server expands the
    // template) instead of sending the text verbatim — the same check the TUI/web clients make
    // against their synced command list. The list is directory-scoped, so the cache is keyed to
    // ActiveDirectory() and invalidated by directory change or a short TTL (commands can be
    // added/edited on disk while connected).
    //
    // The list mixes the three command sources the server folds in (file/config commands and
    // built-ins with source "command", MCP prompts with "mcp", skills with "skill"), so names
    // are cached split by source. When the "Expand skills" setting (SettingsStore.ExpandSkills)
    // is off, skill-only names fall through to a plain prompt; a name backed by a real command
    // always routes (the server itself drops a skill whose name collides with a command —
    // command/index.ts adds skills only for names not already taken).

    private const long CommandCacheTtlMs = 5 * 60 * 1000;
    private HashSet<string>? _commandNames;
    private HashSet<string>? _skillNames;
    private long _commandNamesFetchedMs;

    /// <summary>
    /// Clears the end-of-chat retry card and "Continue" state. Called when the active session
    /// is reset (connect / new / switch / deleted) and before sending a new prompt.
    /// </summary>
    private void ResetTurnFlags()
    {
        ShowContinue = false;
    }



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
        && !interruptRequested
        && !LastAssistantMessageInterrupted()
        && LastAssistantMessageEndsOnThinking();

    
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

        if (SettingsStore.AutoContinueOnThinking && !LastAssistantMessageInterrupted() && !interruptRequested
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
}