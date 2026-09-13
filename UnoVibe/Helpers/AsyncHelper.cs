namespace UnoVibe.Helpers;

class AsyncHelper
{
    public static async void RunAndReport(Func<Task> func, ToastsProvider toasts, string error, string title)
        => RunAndReport(func(), toasts, error, title);
    public static async void RunAndReport(Task task, ToastsProvider toasts, string error, string title)
    {
        try
        {
            await task;
        } catch (Exception ex)
        {
            toasts.ShowError(string.IsNullOrWhiteSpace(error) ? ex.Message : $"{error}\n{ex.Message}", title);
        }
    }
}