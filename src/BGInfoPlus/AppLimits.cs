using System.Text.RegularExpressions;
using System.IO;

namespace BGInfoPlus;

internal static class AppLimits
{
    public const int MinImageWidth = 640;
    public const int MinImageHeight = 480;
    public const int MaxImageWidth = 3840;
    public const int MaxImageHeight = 2160;
    public const long MaxImagePixels = 3840L * 2160L;
    public const int MaxDocumentBytes = 768 * 1024;
    public const int MaxDocumentCharacters = 1024 * 1024;
    public const int MaxPlaceholders = 128;
    public const int MaxPlaceholderJsonCharacters = 512 * 1024;
    public const int MaxConfigurationFileBytes = 4 * 1024 * 1024;
    public const int MaxResolvedValueCharacters = 4096;
    public const int MaxArgumentCharacters = 4096;
    public const int MaxLabelCharacters = 256;
    private static readonly Regex ValidName = new(@"^[A-Za-z][A-Za-z0-9_.-]{0,63}$", RegexOptions.CultureInvariant);

    public static bool IsValidResolution(int width, int height) =>
        width is >= MinImageWidth and <= MaxImageWidth &&
        height is >= MinImageHeight and <= MaxImageHeight &&
        (long)width * height <= MaxImagePixels;

    public static DisplayResolution FitResolution(int width, int height, string? description = null)
    {
        width = Math.Max(MinImageWidth, width);
        height = Math.Max(MinImageHeight, height);
        var scale = Math.Min(1d, Math.Min(MaxImageWidth / (double)width, MaxImageHeight / (double)height));
        scale = Math.Min(scale, Math.Sqrt(MaxImagePixels / ((double)width * height)));
        return new DisplayResolution(
            Math.Clamp((int)Math.Floor(width * scale), MinImageWidth, MaxImageWidth),
            Math.Clamp((int)Math.Floor(height * scale), MinImageHeight, MaxImageHeight), description);
    }

    public static void Validate(AppConfiguration configuration)
    {
        if (configuration is null) throw new InvalidDataException("The configuration is missing.");
        if (!IsValidResolution(configuration.ImageWidth, configuration.ImageHeight))
            throw new InvalidDataException($"Image dimensions must be between {MinImageWidth}×{MinImageHeight} and {MaxImageWidth}×{MaxImageHeight}.");
        if (configuration.Document is null || configuration.Document.Length > MaxDocumentCharacters)
            throw new InvalidDataException("The rich-text document is too large.");
        if (configuration.Placeholders is null || configuration.Placeholders.Length > MaxPlaceholders)
            throw new InvalidDataException($"No more than {MaxPlaceholders} placeholders are allowed.");
        if (configuration.BackgroundColor is null || configuration.BackgroundColor.Length > 32)
            throw new InvalidDataException("The wallpaper color is invalid.");
        foreach (var placeholder in configuration.Placeholders)
        {
            if (placeholder is null) throw new InvalidDataException("A placeholder definition is missing.");
            if (!ValidName.IsMatch(placeholder.Name)) throw new InvalidDataException("A placeholder name is invalid.");
            if (placeholder.Label.Length > MaxLabelCharacters) throw new InvalidDataException("A placeholder label is too long.");
            if (placeholder.Kind.Length > 32) throw new InvalidDataException("A placeholder source name is too long.");
            if (placeholder.Argument.Length > MaxArgumentCharacters) throw new InvalidDataException("A placeholder argument is too long.");
        }
    }
}
