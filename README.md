# BGInfoPlus
A lean Windows wallpaper information utility for Azure VMs, built with C# and WPF.

## First version

- Select fields, preview the wallpaper, and apply it to the current Windows user's desktop.
- Hostname, username, local versus domain/external account context.
- Active network interfaces: IPv4/IPv6 addresses with subnet prefix lengths, and DNS server addresses.
- Azure VM name, size, and region through the in-guest Instance Metadata Service (IMDS), when available. Requests bypass proxies, carry the required Metadata header, and time out after three seconds. No Azure credentials are needed.
- CustomData from `C:\AzureData\CustomData.bin`, or a file chosen in the editor. Accepts Base64-encoded UTF-8 JSON and already-decoded JSON. Nested objects and arrays become selectable JSON Pointer paths. Malformed data and duplicate paths produce an error without blocking system fields.
- Solid dark wallpaper, upper-right text, selectable font size. Long content is clipped to the wallpaper bounds; inspect the preview before applying.
- Field selection, source path, and font size persist when applying; values are refreshed when the editor opens or Refresh is clicked. Missing fields remain selected in saved preferences for when they become available again.

CustomData fields are initially unchecked. Selected values are written to the wallpaper image and visible on the desktop; choose them carefully. The app reads CustomData only and never executes it. Windows permissions still control access to its source file; use a readable copy if needed. It does not retrieve CustomData through IMDS.

UserData is deferred. Multiple desktop support, VBScript, countdowns, timers, databases, popups, taskbar integration, and RTF are outside scope. VM agent status is not part of the agreed first-version field list.

## Build and run

Use the .NET 10 SDK. The application requires Windows 10/11 or a compatible Windows Server desktop environment for execution. Development builds can cross-compile on Linux, but UI and wallpaper validation require Windows.

```powershell
dotnet run --project tests/BGInfoPlus.Tests -c Release
dotnet run --project src/BGInfoPlus
```

The test executable runs 12 checks of actual CustomData parsing, including nested values, Base64, BOMs, arrays, malformed input, and ambiguous paths. It exits nonzero on failure.

## Native EXE deployment

```powershell
dotnet publish src/BGInfoPlus -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/win-x64
```

Distribute `artifacts/win-x64/BGInfoPlus.exe`. This is a native Windows executable containing the managed application and .NET runtime; targets do not need .NET installed. Native runtime components extract locally on launch. Use `win-arm64` instead of `win-x64` for ARM64 Windows. No trimming or Native AOT is used (WPF compatibility).

Settings and the generated `wallpaper.bmp` are stored in `%LOCALAPPDATA%\BGInfoPlus`. Applying replaces the current wallpaper; restore your previous background through Windows Settings. Existing Windows wallpaper fit settings apply. There is no background process or scheduled refresh. Desktop policy may prevent wallpaper changes. The CI workflow runs parsing checks and publishes the x64 artifact on Windows.

## Windows smoke test

1. Run the published EXE on a desktop machine without a separately installed .NET runtime.
2. Confirm hostname, current account context, IP/prefix and DNS values against Windows settings. On an Azure VM, check VM name/size/region; off Azure, expect metadata to be unavailable without blocking the editor.
3. Choose a file containing Base64-encoded JSON with nested objects and arrays. Confirm fields start unchecked, select several, change text size, and inspect the preview.
4. Apply and verify the wallpaper, then reopen to verify saved selection and refreshed values.
5. Try a missing file and malformed JSON: errors should appear while local fields remain usable. Restore the previous wallpaper through Windows Settings.

