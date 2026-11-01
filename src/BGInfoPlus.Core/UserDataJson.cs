using System.Text;
using System.Text.Json;

namespace BGInfoPlus.Core;

public sealed record InfoField(string Key, string Label, string Value);

public static class UserDataJson
{
    public static IReadOnlyList<InfoField> Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Length > InputLimits.MaxSourceCharacters)
            throw new FormatException($"UserData exceeds the {InputLimits.MaxUserDataBytes / 1024} KiB limit.");
        content = content.Trim().TrimStart('\uFEFF');
        if (!content.StartsWith('{') && !content.StartsWith('['))
        {
            var decoded = Convert.FromBase64String(content);
            if (decoded.Length > InputLimits.MaxUserDataBytes)
                throw new FormatException($"UserData exceeds the {InputLimits.MaxUserDataBytes / 1024} KiB limit.");
            content = new UTF8Encoding(false, true).GetString(decoded).Trim().TrimStart('\uFEFF');
        }
        else if (Encoding.UTF8.GetByteCount(content) > InputLimits.MaxUserDataBytes)
            throw new FormatException($"UserData exceeds the {InputLimits.MaxUserDataBytes / 1024} KiB limit.");
        using var document = JsonDocument.Parse(content, new JsonDocumentOptions { MaxDepth = 64 });
        var result = new List<InfoField>();
        Visit(document.RootElement, "", result);
        if (result.Select(f => f.Key).Distinct(StringComparer.Ordinal).Count() != result.Count)
            throw new FormatException("UserData contains duplicate JSON paths.");
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
        {
            if (fields.Count >= InputLimits.MaxUserDataFields)
                throw new FormatException($"UserData contains more than {InputLimits.MaxUserDataFields} values.");
            var value = element.ValueKind == JsonValueKind.String ? element.GetString()! : element.GetRawText();
            if (value.Length > InputLimits.MaxValueCharacters)
                throw new FormatException($"A UserData value exceeds {InputLimits.MaxValueCharacters} characters.");
            fields.Add(new InfoField("userdata:" + path, "UserData " + (path.Length == 0 ? "/" : path), value));
        }
    }
}
