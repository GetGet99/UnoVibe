namespace UnoVibe.Helpers;

static class DispatcherExtension
{
    extension(DispatcherQueue dispatcher)
    {
        public bool RunOrEnqueue(DispatcherQueueHandler handler)
        {
            if (dispatcher.HasThreadAccess)
            {
                handler();
                return true;
            } else
            {
                return dispatcher.TryEnqueue(handler);
            }
        }
    }
}