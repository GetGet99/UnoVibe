using UnoVibe.Integration.Events;
namespace UnoVibe.States;

partial class ChatboxState
{

    // Auto-continue ("turn.autocontinue" setting) bookkeeping. When a turn stops with the chat
    // ending on a Thinking (reasoning) part, a "continue" prompt is sent automatically instead of
    // surfacing the end-of-chat Continue button, and the router suppresses the completion toast +
    // sidebar unread/outcome indicators for that stop (the turn is already restarting).
    private const int MaxAutoContinues = 50;

    /// <summary>Consecutive automatic continues fired without an intervening manual send or a
    /// stop that didn't qualify — bounds runaway loops against a provider that keeps stopping
    /// mid-thinking; past the cap the manual Continue button returns.</summary>
    private int autoContinueStreak;

    /// <returns>True if session is no longer idle. False if session is still idle</returns>
    public async Task<bool> TurnStopActionAsync(ChatOutcome outcome, string messageId)
    {
        if (SessionId is not {} sessId) return false;
        if (outcome is ChatOutcome.Interrupted)
        {
            // interrupt means intentional stop by user
            // so don't do anything else
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
                // remove from queue
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
            // Best-effort: return false
        }
        return false;
    }
}
