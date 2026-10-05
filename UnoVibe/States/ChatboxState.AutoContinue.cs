using UnoVibe.Integration.Events;
namespace UnoVibe.States;

partial class ChatboxState
{

    private const int MaxAutoContinues = 50;

    private int autoContinueStreak;

    public async Task<bool> TurnStopActionAsync(ChatOutcome outcome, string messageId)
    {
        if (SessionId is not {} sessId) return false;
        if (outcome is ChatOutcome.Interrupted)
        {
            return false;
        }
        Lazy<Task<bool>> endedWithReasoning = new(() => HasMessageEndedWithReasoningAsync(messageId));
        var canContinue = outcome is ChatOutcome.Error || await endedWithReasoning.Value;
        var canAutoContinue =
            SettingsStore.AutoContinueOnThinking
            && autoContinueStreak < MaxAutoContinues
            && await endedWithReasoning.Value;

        var shouldDrain = !canContinue && !canAutoContinue;

        if (shouldDrain)
        {
            ShowContinue = false;
            if (!_pendingPrompts.TryPeek(out var text))
                return false;
            try
            {
                await SendPromptNowAsync(text);
                _pendingPrompts.Dequeue();
                PendingPromptsCount = _pendingPrompts.Count;
                return true;
            }
            catch (Exception ex)
            {
                Toasts.ShowError($"An error occured while trying to send a message\n{ex.Message}", "Prompt Queue");
                return false;
            }
        } else if (canAutoContinue)
        {
            ShowContinue = false;
            autoContinueStreak++;
            try
            {
                await SendPromptNowAsync(new() {
                    Text = """
                        continue
                        <automatic_message_metadata>
                        If you have already finished your task, end the turn with a non-reasoning message instead.
                        </automatic_message_metadata>
                        """
                });
                return true;
            } catch (Exception ex)
            {
                Toasts.ShowError($"An error occured while trying to send a continue message\n{ex.Message}", "Auto Continue");
                return  false;
            }
        } else
        {
            ShowContinue = canContinue;
            return false;
        }
    }

    async Task<bool> HasMessageEndedWithReasoningAsync(string messageId)
    {
        if (SessionId is not {} sessId) return false;
        try
        {
            var message = (await Opencode.GetMessageAsync(sessId, messageId)).GetOrThrow();
            if (message.Parts is { Count: > 0 } parts)
            {
                var lastPart = parts[^1];
                return lastPart is ReasoningPart;
            }
        }
        catch
        {
        }
        return false;
    }
}
