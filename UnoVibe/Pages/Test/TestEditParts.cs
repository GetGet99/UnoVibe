using System.Text.Json;

namespace UnoVibe.Pages.Test;

static class TestEditParts
{
    public const string FilePath = "/home/user/demo/Program.cs";

    public const string CompletedTitle = "demo/Program.cs";

    public const string CompletedOutput = "Edit applied successfully.";

    public static ToolCallPartItem CreatePending()
    {
        var item = new ToolCallPartItem
        {
            Id = "test-edit-part",
            MessageId = "test-edit-message",
            CallId = "test-edit-call",
            ToolName = "edit",
        };
        item.State = new ToolPendingState
        {
            Input = InputElement,
            Raw = "",
        };
        item.ToolStatus = "pending";
        item.ToolFilePath = FilePath;
        item.ToolInput = InputJson;
        return item;
    }

    public static void MarkRunning(ToolCallPartItem item)
    {
        item.State = new ToolRunningState
        {
            Input = InputElement,
        };
        item.ToolStatus = "running";
    }

    public static void MarkCompleted(ToolCallPartItem item)
    {
        item.State = new ToolCompletedState
        {
            Input = InputElement,
            Title = CompletedTitle,
            Output = CompletedOutput,
        };
        item.ToolStatus = "completed";
        item.ToolTitle = CompletedTitle;
        item.Diff = SampleDiff;
        item.ToolOutput = CompletedOutput;
    }

    private static JsonElement InputElement =>
        JsonDocument.Parse(InputJson).RootElement.Clone();

    private const string InputJson =
        "{\"filePath\":\"/home/user/demo/Program.cs\"," +
        "\"oldString\":\"Console.WriteLine(\\\"Hello, world!\\\");\"," +
        "\"newString\":\"Console.WriteLine(\\\"Hello, UnoVibe!\\\");\"}";

    private const string SampleDiff =
        "--- /home/user/demo/Program.cs\n" +
        "+++ /home/user/demo/Program.cs\n" +
        "@@ -1,6 +1,6 @@\n" +
        " using System;\n" +
        " \n" +
        " static class Program\n" +
        " {\n" +
        "-    static void Main() => Console.WriteLine(\"Hello, world!\");\n" +
        "+    static void Main() => Console.WriteLine(\"Hello, UnoVibe!\");\n" +
        " }";
}
