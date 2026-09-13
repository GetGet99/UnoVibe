using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using UnoVibe.Integration;
namespace UnoVibe.States;

[QuickMarkup("""
    public int PendingPromptsCount;
    public bool ShowContinue;
    ChatboxMessage Message = `new()`;
    """)]
public partial class ChatboxState
{
    public SessionId? SessionId { get; private set; }
    SessionHead? Head => Sessions.Head(SessionId);
    ToastsProvider Toasts { get; set; }
    SessionsStateProvider Sessions { get; set; }
    OpencodeClient Opencode { get; set; }
    DispatcherQueue Dispatcher { get; set; }
    [QuickMarkupConstructor]
    [MemberNotNull(nameof(Toasts), nameof(Opencode), nameof(Sessions), nameof(Dispatcher))]
    void Ctor(OpencodeClient client, ToastsProvider toasts, SessionsStateProvider sessions, DispatcherQueue dispatcher, SessionId? sessionId)
    {
        Dispatcher = dispatcher;
        Opencode = client;
        Toasts = toasts;
        Sessions = sessions;
        SessionId = sessionId;
    }
    private readonly Queue<ChatboxMessage> _pendingPrompts = new();


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
    public async Task<ChatboxSentStatus> SendAsync(SendPromptMode? mode = null)
    {
        var status = await SendCoreAsync(Message, mode, fromUser: true);
        switch (status)
        {
            case ChatboxSentStatus.Sent:
            case ChatboxSentStatus.Queued:
                // clear the chatbox
                Message = new();
                break;
            case ChatboxSentStatus.Error:
            case ChatboxSentStatus.Empty:
            default:
                // do nothing
                break;
        }
        return status;
    }
    public Task<ChatboxSentStatus> SendManualContinueAsync()
        => SendCoreAsync(new() { Text = "continue" }, SendPromptMode.OnNextToolCall, fromUser: true);

#pragma warning disable CS8774 // Member must have a non-null value when exiting.
    [MemberNotNull(nameof(SessionId), nameof(Head))]
    async Task EnsureSessionAsync()
    {
        SessionId ??= (await Sessions.CreateFromPreparedSessionAsync()).Id;
    }
#pragma warning restore CS8774 // Member must have a non-null value when exiting.

    /// <summary>Send implementation. <paramref name="fromUser"/> distinguishes real user sends
    /// (which reset the auto-continue streak) from the automatic "continue" (which must not).</summary>
    private async Task<ChatboxSentStatus> SendCoreAsync(ChatboxMessage message, SendPromptMode? mode, bool fromUser)
    {
        if (message.IsEmpty) return ChatboxSentStatus.Empty;
        if (fromUser)
        {
            autoContinueStreak = 0;
        }
        try
        {
            await EnsureSessionAsync();

            var effective = mode ?? SettingsStore.SendMode;
            if (effective == SendPromptMode.Queue && Head.IsBusy)
            {
                EnqueuePrompt(message);
                return ChatboxSentStatus.Queued;
            }
            if (effective == SendPromptMode.SendImmediately && Head.IsBusy)
            {
                try
                {
                    await Opencode.AbortAsync(Head.Id);
                }
                catch (Exception ex)
                {
                    Toasts.ShowError(ex.Message, "Stop failed");
                }
            }
            await SendPromptNowAsync(message);
            return ChatboxSentStatus.Sent;
        }
        catch (Exception ex)
        {
            Toasts.ShowError(ex.Message, "Message failed to send");
            
        return ChatboxSentStatus.Error;
        }
    }

    // Client-side prompt queue for the "Queue" send mode (SettingsStore.SendMode):
    // SendAsync enqueues while a turn is busy, and DrainPendingPromptsAsync flushes the queue
    // one prompt at a time when the session goes idle. The "OnNextToolCall" mode skips the
    // queue entirely and sends immediately (the server serializes prompts itself).
    private void EnqueuePrompt(ChatboxMessage message)
    {
        _pendingPrompts.Enqueue(message);
        PendingPromptsCount = _pendingPrompts.Count;
    }

    private void ClearPendingPrompts()
    {
        _pendingPrompts.Clear();
        PendingPromptsCount = 0;
    }

    private async Task SendPromptNowAsync(ChatboxMessage message)
    {
        Debug.Assert(!message.IsEmpty);
        await EnsureSessionAsync();
        // Slash-command send (opencode Commands): when the input starts with "/name" and the
        // server knows that command for the active directory, route it through
        // POST /session/{id}/command so the server expands the template ($ARGUMENTS/$1..,
        // !`shell`, @file) and runs it with the command's own options — instead of sending the
        // verbatim text (which the session loop does NOT expand). Unknown slash text still sends
        // as a normal prompt, matching the TUI/web clients.
        if (ParseSlashCommand(message.Text) is { } cmd && await IsKnownCommandAsync(cmd.Name))
        {
            SendCommandNow(cmd.Name, cmd.Arguments, message);
            return;
        }

        // Mark busy optimistically so interleaved SendAsync calls queue instead of
        // racing the HTTP call; the server's session.status busy event confirms it.
        Head.IsBusy = true;
        ShowContinue = false;
        var chatParams = Head.ChatParams;
        var model = chatParams.Model;
        await Opencode.SendPromptAsync(Head.Id, new()
        {
            Parts = [ToPromptPart(message.Text), ..message.Images.Select(ToPromptPart)],
            Agent = chatParams.Agent,
            Model = model is null ? null : new()
            {
                ProviderID = model.ProviderId,
                ModelID = model.Id
            },
            Variant = chatParams.Variant
        });
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
        if (newline >= 0) arguments += string.Concat("\n", text.AsSpan(newline + 1));
        return (name, arguments);
    }

    /// <summary>
    /// Fires POST /session/{id}/command as a detached request. The endpoint runs the whole command
    /// server-side and blocks until its turn ends, but all progress arrives over the SSE stream,
    /// so the request is fire-and-forget (TUI parity) — the composer clears immediately and the
    /// same busy/status plumbing drives the UI as for a normal prompt. Errors (e.g. the command
    /// vanished server-side) are surfaced as an error toast.
    /// </summary>
    private void SendCommandNow(string name, string arguments, ChatboxMessage message)
    {
        if (SessionId is null) throw new NullReferenceException(nameof(SessionId));
        Head!.IsBusy = true;
        ShowContinue = false;
        var images = message.Images.ToList();

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
                    Parts = images.Count is 0 ? null : [.. images.Select(ToPromptPart)],
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

    /// <summary>
    /// Runs a shell command inside the session (the composer's <c>!</c> shell mode, TUI parity).
    /// Fires POST /session/{id}/shell detached like <see cref="SendCommandNow"/>: the endpoint
    /// blocks until the command exits while all progress — the synthetic user message, the
    /// assistant message with a running <c>bash</c> tool part, and its streaming output —
    /// arrives over the SSE stream and renders through the normal message plumbing. The session
    /// goes busy for the duration (Stop aborts the command); the server 409s a concurrent run,
    /// so a busy session surfaces an error instead of sending.
    /// </summary>
    public async Task<ChatboxSentStatus> SendShellAsync(string command)
    {
        await EnsureSessionAsync();
        if (Head.IsBusy)
        {
            Toasts.ShowWarning("Wait for the current turn to finish before running a shell command.", "Session busy");
            return ChatboxSentStatus.Error;
        }
        try
        {
            // Mark busy optimistically so a second shell submit can't race the HTTP call;
            // the server's session.status busy event confirms it.
            Head.IsBusy = true;
            ShowContinue = false;

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
            return ChatboxSentStatus.Sent;
        }
        catch (Exception ex)
        {
            Toasts.ShowError(ex.Message, "Shell command failed");
            return ChatboxSentStatus.Error;
        }
    }
}
