# BGInfoPlus

A Windows wallpaper information utility for Azure VMs, built with C# and WPF. Create a formatted layout, insert live system values, and preview, export, or apply the result.

![BGInfoPlus screenshot](screenshot.png)

## Data Sources

The editor provides helpers for environment variables, registry values, per-interface networking, Azure Instance Metadata Service values, scalar UserData JSON values, and common CIM system information. Missing or unavailable values resolve to empty text.

Azure CustomData is reserved and currently disabled.

## Quick Start

Install the machine-wide MSI or use the portable ZIP. The portable ZIP needs no installation or separate .NET runtime; the standalone EXE is also available for direct use.

1. Start by inserting data from the available data helpers. A helper inserts a named placeholder such as `{Hostname}` at the cursor.
2. Lay out the placeholders, change colors and formatting.
3. Choose an output resolution and wallpaper fit mode, then select **Preview**, **Export image**, or **Apply now**. Hold **Alt** while selecting **Apply now** to restore the wallpaper configuration captured when the editor opened.

Use **Import config…** and **Export config…** to exchange reusable JSON layouts. To provide a fallback, copy an exported file beside `BGInfoPlus.exe` as `DefaultConfig.json`. **Deploy for all users…** enforces the current design for every interactive user.

**Always apply at login** regenerates the current user's wallpaper at sign-in.

All scopes use the same validated JSON format. Personal configuration is stored under `%LOCALAPPDATA%`, the optional fallback travels with the application, and only enforced all-users policy is stored under `%ProgramData%`. MSI installation provisions `DefaultConfig.json` and the logon task automatically. Azure VM Application installation provisions its supplied configuration as enforced all-users policy.

## Build and Test

Use the .NET 10 SDK on Windows:

```powershell
dotnet build src/BGInfoPlus -c Release
dotnet run --project tests/BGInfoPlus.Tests -c Release
```

`tools/Test-BGInfoPlus.ps1` is the repeatable pre-release check used by the release builder. It parses maintained PowerShell scripts, validates installer XML, and runs the compiled tests. The test executable covers bounded UserData parsing, configuration precedence and validation, formatted placeholder behavior, Tab round-tripping, font-size bounds, shared native custom colors, rendering, and atomic writes. Machine-specific Azure, networking, Task Scheduler, and desktop behavior remains integration-tested.

## More documentation

- [Installation, releases, and Azure VM Applications](docs/deployment.md)
- [Configuration scopes, precedence, export, and migration](docs/configuration.md)
- [Preparing a reference image](docs/reference-image.md)

## Limitations

Rendering is limited to 3840 × 2160. Azure UserData accepts UTF-8 JSON or Base64-encoded UTF-8 JSON, is read but never executed, and is limited to 256 KiB, 256 scalar values, and 4,096 characters per value.

Administrator policy can prevent the generated image from being applied. BGInfoPlus supports the Windows Fill, Fit, and Stretch modes; span, tile, slideshow, and per-monitor wallpaper layouts are not editor options.

## Copyright

No infringement intended on BGInfo (available from Microsoft Sysinternals). Hi, Mark!
