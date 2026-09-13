namespace UnoVibe.Models.Startup;

/// <summary>What the app was asked to open on the command line.</summary>
public enum LaunchKind
{
    /// <summary>No target argument: show the interactive ConnectPage.</summary>
    None,

    /// <summary>A local folder to run <c>opencode serve</c> in.</summary>
    Folder,

    /// <summary>An existing opencode server URL to connect to.</summary>
    Server,
}
