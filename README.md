# BGInfoPlus

A Windows wallpaper information utility for Azure VMs, built with C# and WPF. Create a formatted layout, insert live system values, and preview, export, or apply the result.

![BGInfoPlus screenshot](screenshot.png)

## Quick Start

Install the machine-wide MSI or use the portable ZIP. The portable ZIP needs no installation or separate .NET runtime; the standalone EXE is also available for direct use.

1. Open BGInfoPlus and create a layout. Use paragraph alignment, Tab characters, bold/italic/underline, or enter a font size from 8 to 96 points.
2. Select text and use the color dialog; recent custom colors remain available as swatches for the current editor session and can be applied to text or the wallpaper.
3. Add values through the data helpers. A helper inserts a named placeholder such as `{Hostname}` at the cursor.
4. Choose an output resolution, then select **Preview**, **Export image**, or **Apply wallpaper**.

**Always apply at login** regenerates the current user's wallpaper at sign-in. For shared systems, **Deploy for all users…** saves a protected machine-wide template and registers a delayed interactive logon task. Azure VM Application installation provisions its default template and task automatically.

## Data Sources

The editor provides helpers for environment variables, registry values, per-interface networking, Azure Instance Metadata Service values, scalar UserData JSON values, and common CIM system information. Missing or unavailable values resolve to empty text. CustomData is reserved and currently disabled.

## Build and Test

Use the .NET 10 SDK on Windows:

```powershell
dotnet build src/BGInfoPlus -c Release
dotnet run --project tests/BGInfoPlus.Tests -c Release
```

The test executable covers bounded UserData parsing, formatted placeholder behavior, configuration validation, Tab round-tripping, font-size bounds, recent colors, rendering, and atomic writes. Machine-specific Azure, registry, networking, and desktop behavior remains integration-tested.

## More

- [Installation, releases, and Azure VM Applications](docs/deployment.md)
- [Preparing a reference image](docs/reference-image.md)

## Copyright

No infringement intended on BGInfo (available from Microsoft Sysinternals). Hi, Mark!
