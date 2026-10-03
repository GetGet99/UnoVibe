namespace UnoVibe.States;

public enum ChatboxSentStatus
{
    /// <summary>
    /// Request was not processed as it is empty
    /// </summary>
    Empty,
    /// <summary>
    /// Request was sent
    /// </summary>
    Sent,
    /// <summary>
    /// Request was queued
    /// </summary>
    Queued,
    /// <summary>
    /// Some error have occurred. A toast has already been shown with the details.
    /// </summary>
    Error
}
