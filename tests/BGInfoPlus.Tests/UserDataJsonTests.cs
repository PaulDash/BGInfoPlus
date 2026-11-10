using System.Text;
using BGInfoPlus.Core;
using Xunit;

namespace BGInfoPlus.Tests;

public sealed class UserDataJsonTests
{
    private const string SampleJson = "{\"team\":\"Ops\",\"nested\":{\"enabled\":true},\"list\":[42,null],\"a/b\":\"escaped\",\"a~b\":\"tilde\"}";

    [Fact]
    public void RawAndBase64JsonProduceTheSameFlattenedValues()
    {
        var raw = UserDataJson.Parse(SampleJson);
        var encoded = UserDataJson.Parse(Convert.ToBase64String(Encoding.UTF8.GetBytes(SampleJson)));
        Assert.Equal(raw, encoded);
        Assert.Equal("true", raw.Single(field => field.Key == "userdata:/nested/enabled").Value);
        Assert.Equal("42", raw.Single(field => field.Key == "userdata:/list/0").Value);
        Assert.Equal("null", raw.Single(field => field.Key == "userdata:/list/1").Value);
        Assert.Equal("escaped", raw.Single(field => field.Key == "userdata:/a~1b").Value);
        Assert.Equal("tilde", raw.Single(field => field.Key == "userdata:/a~0b").Value);
    }

    [Fact]
    public void Utf8BomIsAccepted() =>
        Assert.Equal(UserDataJson.Parse(SampleJson), UserDataJson.Parse("\uFEFF" + SampleJson));

    [Fact]
    public void EmptyObjectProducesNoFields() => Assert.Empty(UserDataJson.Parse("{}"));

    [Theory]
    [InlineData("not base64!")]
    [InlineData("bm90IGpzb24=")]
    [InlineData("{broken}")]
    [InlineData("{\"x\":1,\"x\":2}")]
    public void MalformedOrAmbiguousDataIsRejected(string content) => AssertRejected(content);

    [Fact]
    public void OversizedScalarIsRejected()
    {
        var content = "{\"value\":\"" + new string('x', InputLimits.MaxValueCharacters + 1) + "\"}";
        AssertRejected(content);
    }

    [Fact]
    public void TooManyValuesAreRejected()
    {
        var content = "{" + string.Join(',', Enumerable.Range(0, InputLimits.MaxUserDataFields + 1).Select(i => $"\"p{i}\":{i}")) + "}";
        AssertRejected(content);
    }

    [Fact]
    public void OversizedUserDataIsRejected()
    {
        var content = "{\"padding\":\"" + new string('x', InputLimits.MaxUserDataBytes) + "\"}";
        AssertRejected(content);
    }

    private static void AssertRejected(string content)
    {
        var exception = Record.Exception(() => UserDataJson.Parse(content));
        Assert.True(exception is FormatException or System.Text.Json.JsonException,
            $"Expected FormatException or JsonException, but got {exception?.GetType().Name ?? "no exception"}.");
    }
}
