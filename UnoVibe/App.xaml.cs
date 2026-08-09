using System;
using System.IO;
using Microsoft.Extensions.Logging;
using Uno.Resizetizer;
using UnoVibe.Pages;
using UnoVibe.Services;

namespace UnoVibe;

public partial class App : Application
{
    /// <summary>
    /// Initializes the singleton application object. This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        this.InitializeComponent();
    }

    /// <summary>All open windows. Each window scopes to its own <see cref="ChatStore"/>.</summary>
    public static List<WindowController> Windows { get; } = new();

    protected Window? MainWindow { get; private set; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        ReactiveInitializer.InitReactiveScheduler();

        MainWindow = CreateWindow().Window;
    }

    /// <summary>
    /// Creates a new window wired to its own chat store. The connect page or the main
    /// chat page is chosen from the command-line launch target: a folder runs a local
    /// `opencode serve` there, an http(s) URL connects to an existing server, and no
    /// argument shows the interactive ConnectPage.
    /// </summary>
    public static WindowController CreateWindow()
    {
        var controller = new WindowController();
        Windows.Add(controller);

        var startup = ValidateFolderTarget(StartupArgs.Parse());
        if (startup.Kind == LaunchKind.None)
            controller.ShowConnect();
        else
            controller.ShowConnect(startup);

#if DEBUG
        controller.Window.UseStudio();
#endif
        controller.Window.SetWindowIcon();

        controller.Window.Closed += (_, _) =>
        {
            Windows.Remove(controller);
            TryDispose(controller.Store);
        };

        controller.Window.Activate();
        return controller;
    }

    /// <summary>
    /// Validates a folder launch target before the window is built: a path that resolves
    /// to a file fails the launch (a folder is required), and a missing folder is created
    /// so `opencode serve` has somewhere to run (VSCode-style open). Server/None targets
    /// pass through unchanged.
    /// </summary>
    private static StartupArgs ValidateFolderTarget(StartupArgs startup)
    {
        if (startup.Kind != LaunchKind.Folder) return startup;

        var full = Path.GetFullPath(startup.Value);
        if (File.Exists(full))
            FailLaunch($"'{startup.Value}' is a file, not a folder.");
        if (!Directory.Exists(full)) Directory.CreateDirectory(full);
        return startup with { Value = full };
    }

    /// <summary>Terminates the app with a console error, mirroring a CLI launch failure.</summary>
    private static void FailLaunch(string message)
    {
        Console.Error.WriteLine($"UnoVibe: {message}");
        Console.Error.WriteLine("Usage: UnoVibe [folder-or-http-url] [--password [password]]");
        Environment.Exit(1);
    }

    private static void TryDispose(ChatStore store)
    {
        try { store.Dispose(); } catch { /* best effort on shutdown */ }
    }

    /// <summary>
    /// Configures global Uno Platform logging
    /// </summary>
    public static void InitializeLogging()
    {
#if DEBUG
        // Logging is disabled by default for release builds, as it incurs a significant
        // initialization cost from Microsoft.Extensions.Logging setup. If startup performance
        // is a concern for your application, keep this disabled. If you're running on the web or
        // desktop targets, you can use URL or command line parameters to enable it.
        //
        // For more performance documentation: https://platform.uno/docs/articles/Uno-UI-Performance.html

        var factory = LoggerFactory.Create(builder =>
        {
#if __WASM__
            builder.AddProvider(new global::Uno.Extensions.Logging.WebAssembly.WebAssemblyConsoleLoggerProvider());
#elif __IOS__
            builder.AddProvider(new global::Uno.Extensions.Logging.OSLogLoggerProvider());

            // Log to the Visual Studio Debug console
            builder.AddConsole();
#else
            builder.AddConsole();
#endif

            // Exclude logs below this level
            builder.SetMinimumLevel(LogLevel.Information);

            // Default filters for Uno Platform namespaces
            builder.AddFilter("Uno", LogLevel.Warning);
            builder.AddFilter("Windows", LogLevel.Warning);
            builder.AddFilter("Microsoft", LogLevel.Warning);

            // Generic Xaml events
            // builder.AddFilter("Microsoft.UI.Xaml", LogLevel.Debug );
            // builder.AddFilter("Microsoft.UI.Xaml.VisualStateGroup", LogLevel.Debug );
            // builder.AddFilter("Microsoft.UI.Xaml.StateTriggerBase", LogLevel.Debug );
            // builder.AddFilter("Microsoft.UI.Xaml.UIElement", LogLevel.Debug );
            // builder.AddFilter("Microsoft.UI.Xaml.FrameworkElement", LogLevel.Trace );

            // Layouter specific messages
            // builder.AddFilter("Microsoft.UI.Xaml.Controls", LogLevel.Debug );
            // builder.AddFilter("Microsoft.UI.Xaml.Controls.Layouter", LogLevel.Debug );
            // builder.AddFilter("Microsoft.UI.Xaml.Controls.Panel", LogLevel.Debug );

            // builder.AddFilter("Windows.Storage", LogLevel.Debug );

            // Binding related messages
            // builder.AddFilter("Microsoft.UI.Xaml.Data", LogLevel.Debug );
            // builder.AddFilter("Microsoft.UI.Xaml.Data", LogLevel.Debug );

            // Binder memory references tracking
            // builder.AddFilter("Uno.UI.DataBinding.BinderReferenceHolder", LogLevel.Debug );

            // DevServer and HotReload related
            // builder.AddFilter("Uno.UI.RemoteControl", LogLevel.Information);

            // Debug JS interop
            // builder.AddFilter("Uno.Foundation.WebAssemblyRuntime", LogLevel.Debug );
        });

        global::Uno.Extensions.LogExtensionPoint.AmbientLoggerFactory = factory;

#if HAS_UNO
        global::Uno.UI.Adapter.Microsoft.Extensions.Logging.LoggingAdapter.Initialize();
#endif
#endif
    }
}
