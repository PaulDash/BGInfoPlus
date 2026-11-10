using System.Runtime.ExceptionServices;
using System.Windows.Documents;

namespace BGInfoPlus.Tests;

internal static class TestSupport
{
    public static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    public static FlowDocument Document(params string[] runs)
    {
        var document = new FlowDocument();
        var paragraph = new Paragraph();
        foreach (var text in runs) paragraph.Inlines.Add(new Run(text));
        document.Blocks.Add(paragraph);
        return document;
    }

    public static string Text(FlowDocument document) =>
        new TextRange(document.ContentStart, document.ContentEnd).Text.Trim();

    public static string CreateTemporaryDirectory(string category)
    {
        var path = Path.Combine(Path.GetTempPath(), category, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
