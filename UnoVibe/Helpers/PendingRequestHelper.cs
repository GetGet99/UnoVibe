namespace UnoVibe.Helpers;

static class PendingRequestHelper
{
    public static async Task<bool> ReplyPermissionAsync(OpencodeClient opencode, ToastsProvider toasts, string? directory, string requestId, string reply, string? message)
    {
        try
        {
            await opencode.ReplyPermissionAsync(requestId, new() { Reply = reply, Message = message }, directory);
            return true;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            toasts.ShowWarning("The permission request was already handled.", "Approval dismissed");
            return true;
        }
        catch (Exception ex)
        {
            toasts.ShowError(ex.Message, "Approval reply failed");
            return false;
        }
    }

    public static async Task ReplyQuestionAsync(OpencodeClient opencode, ToastsProvider toasts, string? directory, string requestId, IReadOnlyList<IReadOnlyList<string>> answers)
    {
        try
        {
            await opencode.ReplyQuestionAsync(requestId, new() { Answers = answers }, directory);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            toasts.ShowWarning("The question form was dismissed — the request is no longer pending.", "Question already handled");
        }
        catch (Exception ex)
        {
            toasts.ShowError(ex.Message, "Question reply failed");
        }
    }

    public static async Task RejectQuestionAsync(OpencodeClient opencode, ToastsProvider toasts, string? directory, string requestId)
    {
        try
        {
            await opencode.RejectQuestionAsync(requestId, directory);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            toasts.ShowWarning("The question form was dismissed — the request is no longer pending.", "Question already handled");
        }
        catch (Exception ex)
        {
            toasts.ShowError(ex.Message, "Question dismiss failed");
        }
    }
}
