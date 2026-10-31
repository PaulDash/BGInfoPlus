using System.Text;
using System.Text.Json;

namespace BGInfoPlus.Core;

public sealed record InfoField(string Key, string Label, string Value);

public static class CustomData
{
    public static IReadOnlyList<InfoField> Parse(string content)
    {
        content = content.Trim().TrimStart('\uFEFF');
        // Azure provisioning can leave either the original JSON or its Base64 representation.
        if (!content.StartsWith('{') && !content.StartsWith('['))
            content = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(content)).Trim().TrimStart('\uFEFF');
        using var document = JsonDocument.Parse(content, new JsonDocumentOptions { MaxDepth = 64 });
        var result = new List<InfoField>();
        Visit(document.RootElement, "", result);
        if (result.Select(f => f.Key).Distinct(StringComparer.Ordinal).Count() != result.Count)
            throw new FormatException("CustomData contains duplicate JSON paths.");
        return result;
    }

    private static void Visit(JsonElement element, string path, List<InfoField> fields)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject())
                Visit(property.Value, path + "/" + property.Name.Replace("~", "~0").Replace("/", "~1"), fields);
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var value in element.EnumerateArray()) Visit(value, path + "/" + index++, fields);
        }
        else
            fields.Add(new InfoField("custom:" + path, "CustomData " + (path.Length == 0 ? "/" : path),
                element.ValueKind == JsonValueKind.String ? element.GetString()! : element.GetRawText()));
    }
}
