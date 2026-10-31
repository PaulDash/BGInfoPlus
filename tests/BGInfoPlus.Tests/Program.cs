using System.Text;
using System.Text.Json;
using BGInfoPlus.Core;

var count = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); count++; }
var json = "{\"team\":\"Ops\",\"nested\":{\"enabled\":true},\"list\":[42,null],\"a/b\":\"escaped\",\"a~b\":\"tilde\"}";
var raw = CustomData.Parse(json);
var encoded = CustomData.Parse(Convert.ToBase64String(Encoding.UTF8.GetBytes(json)));
Check(raw.SequenceEqual(encoded), "Raw and Base64 JSON must match");
Check(raw.Single(f => f.Key == "custom:/nested/enabled").Value == "true", "Nested boolean");
Check(raw.Single(f => f.Key == "custom:/list/0").Value == "42", "Array number");
Check(raw.Single(f => f.Key == "custom:/list/1").Value == "null", "Array null");
Check(raw.Single(f => f.Key == "custom:/a~1b").Value == "escaped", "JSON pointer slash escaping");
Check(raw.Single(f => f.Key == "custom:/a~0b").Value == "tilde", "JSON pointer tilde escaping");
Check(CustomData.Parse("\uFEFF" + json).SequenceEqual(raw), "UTF8 BOM");
Check(CustomData.Parse("{}").Count == 0, "Empty object");
foreach (var invalid in new[] { "not base64!", Convert.ToBase64String(Encoding.UTF8.GetBytes("not json")), "{broken}", "{\"x\":1,\"x\":2}" })
{
    var rejected = false;
    try { CustomData.Parse(invalid); } catch (Exception e) when (e is FormatException or JsonException) { rejected = true; }
    Check(rejected, "Reject malformed/ambiguous data: " + invalid);
}
Console.WriteLine($"Passed {count} CustomData checks.");
