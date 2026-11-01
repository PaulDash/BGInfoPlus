# BGInfoPlus

A lean Windows wallpaper information utility for Azure VMs, built with C# and WPF.

## Usage

BGInfoPlus is a portable Windows application. Run `BGInfoPlus.exe` directly; installation and a separate .NET runtime are not required.

1. Select the output resolution to the left of **Preview**. The current primary-screen resolution is selected initially.
2. Enter fixed text in the editor and use the toolbar to set the font, size, color, emphasis, alignment, and wallpaper color.
3. Use a helper action to choose system data. BGInfoPlus creates a named placeholder, such as `{Hostname}`, and inserts it at the cursor. Placeholders can be moved and formatted like normal text.
4. Select **Preview** to resolve the current values and inspect the generated wallpaper without changing the desktop.
5. Select **Export image** to save a static PNG, BMP, or JPEG, or select **Apply wallpaper** to save the configuration and update the current user's wallpaper.

Enable **Always apply at login** before applying the wallpaper if it should be regenerated whenever the current user signs in. BGInfoPlus registers the current executable under the user's Windows `Run` key. At login it starts with `--apply`, reads the saved configuration, refreshes the wallpaper without opening the editor, and exits. Clear the checkbox and select **Apply wallpaper** again to remove that login entry.

### Data helpers

The helper actions provide:

- **Environment variables** — values available to the BGInfoPlus process.
- **Registry** — browse supported registry hives and select a value.
- **Networking** — select a network interface and values such as IP addresses, prefix, subnet mask, gateway, DNS, DHCP, MAC address, speed, and MTU.
- **UserData** — select scalar values from a UTF-8 JSON file or Base64-encoded UTF-8 JSON file. The file is read again whenever the wallpaper is generated.
- **Azure metadata** — VM name, size, and region from the Azure Instance Metadata Service. These values are available only when the application is running on a supported Azure VM with access to IMDS.
- **CIM system information** — common operating-system and hardware values without a WMI dependency.

**CustomData** is reserved for a future release and is currently disabled. If a source or selected value is unavailable, its placeholder is rendered as empty; the rest of the wallpaper is still generated.

### Saved files and troubleshooting

- Configuration is stored for the current user under `HKCU\Software\BGInfoPlus`.
- The generated wallpaper is `%LOCALAPPDATA%\BGInfoPlus\wallpaper.bmp`.
- Automatic-apply diagnostics are written to `%LOCALAPPDATA%\BGInfoPlus\BGInfoPlus.log`.
- Existing Windows wallpaper-fit settings and administrator policy still control how Windows displays or permits the generated image.
- Rendering is limited to 3840 × 2160. Decoded UserData is limited to 256 KiB, 256 scalar values, and 4,096 characters per value.

## Features and behavior

BGInfoPlus now has separate design and runtime paths:

- Opening `BGInfoPlus.exe` displays a rich layout editor. Text can be positioned with paragraph alignment and formatted with size, bold, italic, and underline controls.
- Toolbar color controls use the standard Windows color dialog to format selected text and set the solid wallpaper background.
- **Preview** resolves the current values and opens a uniformly scaled wallpaper preview without changing the desktop. A resolution selector defaults to the current primary display and controls the exact output bitmap dimensions.
- **Export image** writes the resolved layout as PNG, BMP, or JPEG at the selected resolution without applying it as the wallpaper.
- **Apply wallpaper** saves the layout and placeholder mappings under `HKCU\Software\BGInfoPlus`, renders the wallpaper, and assigns it to the current user's desktop.
- When **Always apply at login** is selected, the editor adds `BGInfoPlus` to the current user's `Run` registry key with the `--apply` argument. That mode has no UI: it reads the registry configuration, refreshes the wallpaper, and exits.

Placeholders are inserted through helper windows for:

- Environment variables
- Registry values through a hive/key browser with value name, type, and data preview
- Per-interface networking values, including IP addressing, prefix and subnet mask, gateways, DNS, DHCP, MAC, link state, speed, and MTU
- Azure IMDS values
- Selected scalar values from UserData JSON
- Common system facts exposed by the CIM helper without a WMI dependency

All data sources are optional. An unreachable Azure Instance Metadata Service, a missing UserData file, an inaccessible registry value, or a value that is temporarily absent resolves to an empty placeholder and does not produce a user-facing error. UserData accepts UTF-8 JSON or Base64-encoded UTF-8 JSON and is read but never executed. Decoded UserData is limited to 256 KiB, 256 scalar values, and 4,096 characters per value.

Rendering is limited to 3840 × 2160, placeholder values and configuration fields are bounded, and wallpaper writes use same-directory atomic replacement. Rendering is serialized in-process; wallpaper application is also serialized across processes. Automatic-apply failures are written to a bounded `%LOCALAPPDATA%\BGInfoPlus\BGInfoPlus.log` and return a nonzero exit code.

## Build and test

Use the .NET 10 SDK on Windows:

```powershell
dotnet build src/BGInfoPlus -c Release
dotnet run --project tests/BGInfoPlus.Tests -c Release
```

The test executable covers bounded UserData parsing, placeholder behavior across formatted runs, non-cascading substitutions, document and image limits, actual WPF rendering dimensions, and successful and failed atomic writes. Tests requiring Azure IMDS, machine-specific registry data, network adapters, or desktop policy remain integration tests and are intentionally not part of the build suite.

## Single-file deployment

The project is configured for self-contained, single-file publishing with native libraries included:

```powershell
dotnet publish src/BGInfoPlus -c Release -r win-x64 -o artifacts/win-x64
```

Distribute `artifacts/win-x64/BGInfoPlus.exe`. The target does not need a separate .NET installation or third-party runtime. Use `win-arm64` for ARM64 Windows. WPF prevents Native AOT and native runtime components may extract to the framework's per-user temporary location on launch.

The generated wallpaper is stored at `%LOCALAPPDATA%\BGInfoPlus\wallpaper.bmp`. Existing Windows wallpaper-fit settings still apply, and desktop policy may prevent wallpaper changes.

## Creating a release

Versioned release artifacts are produced by one script. The version must match the version in `BGInfoPlus.csproj`:

```powershell
./tools/Build-Release.ps1 -Version 1.0.3
```

The script runs the tests, publishes the self-contained `win-x64` executable, verifies its Windows file version, and creates these files under the matching version directory in `artifacts/release`:

- Standalone executable and portable ZIP
- Azure VM Application ZIP and its command manifest
- SHA-256 checksums

Pushing a version tag such as `v1.0.3` runs `.github/workflows/release.yml`, repeats the verified build on GitHub Actions, and creates the GitHub release with those artifacts.

## Azure Compute Gallery VM Application

The Azure package installs BGInfoPlus under `%ProgramFiles%\BGInfoPlus`, creates an all-users Start Menu shortcut, and registers an uninstall entry. Users run the editor once to configure their own wallpaper and can then enable **Always apply at login**. Removal preserves each user's saved layout and generated wallpaper.

Run the interactive publisher after building the release package:

```powershell
./tools/Publish-AzureVmApplication.ps1
```

The wizard discovers subscriptions, resource groups, galleries, storage accounts, and regions at runtime. It guides you through selecting or creating resources, choosing SAS or managed-identity ingestion, reviewing the plan, and confirming before any Azure mutation. It does not write subscription IDs, resource names, URLs, SAS values, or prompt answers to the repository or a settings file.

The signed-in identity needs access to the chosen resource group. The assisted upload path also requires `Storage Blob Data Contributor` on the storage account and uses a seven-day, read-only user-delegation SAS for gallery ingestion. For stronger security, preconfigure a user-assigned managed identity on the gallery and choose the plain blob URL option. See Microsoft's documentation for [creating and deploying VM Applications](https://learn.microsoft.com/azure/virtual-machines/vm-applications-how-to) and [publishing with managed identity](https://learn.microsoft.com/azure/virtual-machines/vm-applications-publish-with-managed-identity).

Deploy the replicated application version to a VM with failure propagation enabled:

```powershell
$versionId = '/subscriptions/<subscription-id>/resourceGroups/<gallery-rg>/providers/Microsoft.Compute/galleries/<gallery>/applications/BGInfoPlus/versions/<version>'
az vm application set `
  --resource-group '<vm-resource-group>' `
  --name '<vm-name>' `
  --app-version-ids $versionId `
  --treat-deployment-as-failure true
```

Only one version of a given VM Application can be assigned to a VM at a time. The target VM must have the Azure VM Agent and the application version must be replicated to the VM's region.

## Copyright

No infringement intended on BGInfo (available from Microsoft Sysinternals). Hi, Mark!
