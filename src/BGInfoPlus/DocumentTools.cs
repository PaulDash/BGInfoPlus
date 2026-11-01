using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Text;
using System.Text.RegularExpressions;

namespace BGInfoPlus;

internal static class DocumentTools
{
    private static readonly Regex PlaceholderPattern = new(@"\{[A-Za-z][A-Za-z0-9_.-]{0,63}\}", RegexOptions.CultureInvariant);

    public static string Save(FlowDocument document)
    {
        using var stream = new MemoryStream();
        new TextRange(document.ContentStart, document.ContentEnd).Save(stream, DataFormats.Xaml);
        if (stream.Length > AppLimits.MaxDocumentBytes)
            throw new InvalidDataException($"The formatted document exceeds the {AppLimits.MaxDocumentBytes / 1024} KiB limit.");
        return Convert.ToBase64String(stream.GetBuffer(), 0, checked((int)stream.Length));
    }

    public static FlowDocument Load(string data)
    {
        if (data.Length > AppLimits.MaxDocumentCharacters)
            throw new InvalidDataException("The formatted document is too large.");
        var document = NewDocument();
        using var stream = new MemoryStream(Convert.FromBase64String(data));
        new TextRange(document.ContentStart, document.ContentEnd).Load(stream, DataFormats.Xaml);
        return document;
    }

    public static FlowDocument NewDocument()
    {
        var document = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"), FontSize = 24, Foreground = Brushes.White,
            PagePadding = new Thickness(48), TextAlignment = TextAlignment.Right
        };
        var paragraph = new Paragraph();
        paragraph.Inlines.Add(new Run("Computer: "));
        paragraph.Inlines.Add(new Run("{Hostname}") { FontWeight = FontWeights.SemiBold });
        paragraph.Inlines.Add(new LineBreak());
        paragraph.Inlines.Add(new Run("IPv4: "));
        paragraph.Inlines.Add(new Run("{IPv4}") { FontWeight = FontWeights.SemiBold });
        document.Blocks.Add(paragraph);
        return document;
    }

    public static FlowDocument Resolve(string data, IReadOnlyDictionary<string, string> values)
    {
        var document = Load(data);
        var lookup = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
        foreach (var paragraph in Paragraphs(document.Blocks).ToArray()) ResolveParagraph(paragraph, lookup);
        return document;
    }

    private static void ResolveParagraph(Paragraph paragraph, IReadOnlyDictionary<string, string> values)
    {
        var text = new StringBuilder();
        var segments = new List<(Run Run, int Start, string Text)>();
        foreach (var run in RunSequence(paragraph.Inlines))
        {
            if (run is null) { text.Append('\n'); continue; }
            var value = run.Text ?? string.Empty;
            segments.Add((run, text.Length, value));
            text.Append(value);
        }

        var segmentArray = segments.ToArray();
        var edits = new List<(int Start, int Length, string Value)>[segmentArray.Length];
        foreach (Match match in PlaceholderPattern.Matches(text.ToString()))
        {
            var firstIndex = Array.FindIndex(segmentArray, s => match.Index >= s.Start && match.Index < s.Start + s.Text.Length);
            var lastCharacter = match.Index + match.Length - 1;
            var lastIndex = Array.FindLastIndex(segmentArray, s => lastCharacter >= s.Start && lastCharacter < s.Start + s.Text.Length);
            if (firstIndex < 0 || lastIndex < firstIndex) continue;

            var name = match.Value[1..^1];
            var replacement = values.TryGetValue(name, out var resolved) ? resolved : string.Empty;
            for (var index = firstIndex; index <= lastIndex; index++)
            {
                edits[index] ??= [];
                var segment = segmentArray[index];
                var start = index == firstIndex ? match.Index - segment.Start : 0;
                var end = index == lastIndex ? match.Index + match.Length - segment.Start : segment.Text.Length;
                edits[index].Add((start, end - start, index == firstIndex ? replacement : string.Empty));
            }
        }

        for (var index = 0; index < segmentArray.Length; index++)
        {
            if (edits[index] is null) continue;
            var updated = new StringBuilder(segmentArray[index].Text);
            foreach (var edit in edits[index].OrderByDescending(e => e.Start))
            {
                updated.Remove(edit.Start, edit.Length);
                updated.Insert(edit.Start, edit.Value);
            }
            segmentArray[index].Run.Text = updated.ToString();
        }
    }

    private static IEnumerable<Run?> RunSequence(InlineCollection inlines)
    {
        foreach (var inline in inlines)
        {
            if (inline is Run run) yield return run;
            else if (inline is Span span)
                foreach (var child in RunSequence(span.Inlines)) yield return child;
            else yield return null;
        }
    }

    private static IEnumerable<Paragraph> Paragraphs(BlockCollection blocks)
    {
        foreach (var block in blocks)
        {
            if (block is Paragraph paragraph) yield return paragraph;
            else if (block is Section section)
                foreach (var child in Paragraphs(section.Blocks)) yield return child;
            else if (block is List list)
                foreach (var item in list.ListItems)
                    foreach (var child in Paragraphs(item.Blocks)) yield return child;
        }
    }

}
