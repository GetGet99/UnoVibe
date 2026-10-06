using Microsoft.Extensions.Logging;
using UnoVibe.Models.Startup;
using UnoVibe.Pages.Test;

namespace UnoVibe;

partial class App : Application
{
    public App()
    {
        this.InitializeComponent();
#if WASDK
        _ = DataTemplateDelegator.IdProperty;
#endif
    }

    public static List<WindowController> Windows { get; } = new();

    protected Window? MainWindow { get; private set; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        ReactiveInitializer.InitReactiveScheduler();
        NotificationsHelper.Initialize();

        MainWindow = CreateWindow().Window;
    }

    public static WindowController CreateWindow()
    {
        var controller = new WindowController();
        Windows.Add(controller);

        var startup = CLIHelper.Parse(Environment.GetCommandLineArgs());
        if (TestPage.IsEnabled)
            controller.ShowTest();
        else if (startup.Kind == LaunchKind.None)
            controller.ShowConnect();
        else
            controller.ShowConnect(startup);

        controller.Disposed += () => Windows.Remove(controller);

        controller.Window.Activate();
        NotificationsHelper.RegisterWindow(controller.Window);
        return controller;
    }

    public static void InitializeLogging()
    {
#if DEBUG

        var factory = LoggerFactory.Create(builder =>
        {
#if __WASM__
            builder.AddProvider(new global::Uno.Extensions.Logging.WebAssembly.WebAssemblyConsoleLoggerProvider());
#elif __IOS__
            builder.AddProvider(new global::Uno.Extensions.Logging.OSLogLoggerProvider());

            builder.AddConsole();
#else
            builder.AddConsole();
#endif

            builder.SetMinimumLevel(LogLevel.Information);

            builder.AddFilter("Uno", LogLevel.Warning);
            builder.AddFilter("Windows", LogLevel.Warning);
            builder.AddFilter("Microsoft", LogLevel.Warning);

        });

        global::Uno.Extensions.LogExtensionPoint.AmbientLoggerFactory = factory;

#if HAS_UNO
        global::Uno.UI.Adapter.Microsoft.Extensions.Logging.LoggingAdapter.Initialize();
#endif
#endif
    }
}
