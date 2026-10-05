using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using UnoVibe.Integration;
namespace UnoVibe.States;

[QuickRefs("""
    public int PendingPromptsCount;
    public bool ShowContinue;
    ChatboxMessage Message = `new()`;
    """)]
partial class ChatboxState
{
    public SessionId? SessionId { get; private set; }
    SessionHead? Head => Sessions.Head(SessionId);
    ToastsProvider Toasts { get; set; }
    SessionsStateProvider Sessions { get; set; }
    OpencodeClient Opencode { get; set; }
    DispatcherQueue Dispatcher { get; set; }
    public ChatboxState(OpencodeClient client, ToastsProvider toasts, SessionsStateProvider sessions, DispatcherQueue dispatcher, SessionId? sessionId)
    {
        Dispatcher = dispatcher;
        Opencode = client;
        Toasts = toasts;
        Sessions = sessions;
        SessionId = sessionId;
    }
    private readonly Queue<ChatboxMessage> _pendingPrompts = new();

    public async Task<ChatboxSentStatus> SendAsync(SendPromptMode? mode = null)
    {
        var status = await SendCoreAsync(Message, mode, fromUser: true);
        switch (status)
        {
            case ChatboxSentStatus.Sent:
            case ChatboxSentStatus.Queued:
                Message = new();
                break;
            case ChatboxSentStatus.Error:
            case ChatboxSentStatus.Empty:
            default:
                break;
        }
        return status;
    }
    public Task<ChatboxSentStatus> SendManualContinueAsync()
        => SendCoreAsync(new() { Text = "continue" }, SendPromptMode.OnNextToolCall, fromUser: true);

#pragma warning disable CS8774
    [MemberNotNull(nameof(SessionId), nameof(Head))]
    void EnsureSession()
    {
        if (SessionId is null) throw new InvalidOperationException("Cannot send message for chatbox with null session");
    }
#pragma warning restore CS8774

    private async Task<ChatboxSentStatus> SendCoreAsync(ChatboxMessage message, SendPromptMode? mode, bool fromUser)
    {
        EnsureSession();
        if (message.IsEmpty) return ChatboxSentStatus.Empty;
        if (fromUser)
        {
            autoContinueStreak = 0;
        }
        try
        {
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

    private void EnqueuePrompt(ChatboxMessage message)
    {
        _pendingPrompts.Enqueue(message);
        PendingPromptsCount = _pendingPrompts.Count;
    }

    private async Task SendPromptNowAsync(ChatboxMessage message)
    {
        Debug.Assert(!message.IsEmpty);
        EnsureSession();
        if (ParseSlashCommand(message.Text) is { } cmd && await IsKnownCommandAsync(cmd.Name))
        {
            SendCommandNow(cmd.Name, cmd.Arguments, message);
            return;
        }

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

    private void SendCommandNow(string name, string arguments, ChatboxMessage message)
    {
        if (SessionId is null) throw new InvalidOperationException("Cannot send message when it is not registered to a session id");
        Head!.IsBusy = true;
        ShowContinue = false;
        var images = message.Images.ToList();

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
                Dispatcher.RunOrEnqueue(() => Toasts.ShowError(ex.Message, "Command failed"));
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

    public async Task<ChatboxSentStatus> SendShellAsync(string command)
    {
        EnsureSession();
        if (Head.IsBusy)
        {
            Toasts.ShowWarning("Wait for the current turn to finish before running a shell command.", "Session busy");
            return ChatboxSentStatus.Error;
        }
        try
        {
            Head.IsBusy = true;
            ShowContinue = false;

            ChatParameters chatParams = Head.ChatParams;
            var agent = Sessions.ResolveAgent(chatParams.Agent);
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
                    Dispatcher.RunOrEnqueue(() =>
                    {
                        Toasts.ShowError(ex.Message, "Shell command failed");
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
