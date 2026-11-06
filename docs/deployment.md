# Installation and Deployment

## Install

BGInfoPlus is distributed as a machine-wide MSI, a portable ZIP, and a standalone EXE. The MSI installs to `%ProgramFiles%\BGInfoPlus`, adds a Start Menu shortcut, and supports Windows Installer upgrade and removal. It requires elevation. The portable ZIP needs no installation or separate .NET runtime; it includes the helper required by **Deploy for all users…**. The standalone EXE does not include that helper.

The editor stores the current user's design under `HKCU\Software\BGInfoPlus`. **Always apply at login** is opt-in; selecting it and applying the design registers the current executable in the user's Windows `Run` key. At sign-in, BGInfoPlus refreshes the wallpaper without opening the editor. Clear the option and apply again to remove the entry.

For shared systems, **Deploy for all users…** writes a protected template to `%ProgramData%\BGInfoPlus\config.json` and registers a delayed Task Scheduler task for interactive logons. Each user gets a separately rendered wallpaper under `%LOCALAPPDATA%`. MSI removal deletes the shared task and template while preserving per-user configurations and wallpapers. For reference image preparation, follow [Preparing a reference image](reference-image.md).

## Build and Release

Publish a self-contained executable directly with the .NET 10 SDK:

```powershell
dotnet publish src/BGInfoPlus -c Release -r win-x64 -o artifacts/win-x64
```

Use `win-arm64` for ARM64 Windows. WPF prevents Native AOT; native runtime components may extract to the framework's per-user temporary location at launch.

Create a versioned release with:

```powershell
./tools/Build-Release.ps1 -Version 1.0.3
```

The version must match `Version` in `BGInfoPlus.csproj`. The script runs tests, publishes the executable, generates the default configuration, builds the MSI, and creates the MSI, standalone EXE, portable ZIP, Azure VM Application ZIP, default configuration JSON, command manifest, and SHA-256 checksums under `artifacts/release/v<version>`. It recreates that version directory, so use a new version rather than storing other files there. Pushing a matching version tag builds and publishes the same artifacts through `.github/workflows/release.yml`.

## Azure VM Application

Each published version has a default configuration link. On a new VM install, BGInfoPlus validates that configuration, stores it under ProgramData, secures it for ordinary-user read access, and registers the all-users logon task. A VM's `configurationReference` overrides the version default. The downloaded configuration is named `BGInfoPlus-config.json` and is passed to the installer.

On update, an unchanged app-managed template is refreshed from the supplied configuration. If an administrator has modified or redeployed the shared template, the update preserves it. To change a custom shared design, edit it and select **Deploy for all users…** again. Removal deletes the shared task and template but preserves each user's private configuration and wallpaper.

Publish a release package with the interactive wizard:

```powershell
./tools/Publish-AzureVmApplication.ps1
```

The wizard requires `BGInfoPlus-DefaultConfig.json` beside the VM Application ZIP. The storage-upload path uploads both files and creates seven-day, read-only user-delegation SAS URLs. The signed-in identity needs `Storage Blob Data Contributor` and `Storage Blob Delegator` on the storage account, plus access to the selected resource group. The managed-identity path asks for plain HTTPS blob URLs for both files; the gallery's user-assigned identity must be able to read them. The wizard reviews the plan before making Azure changes and does not save resource details or SAS values to the repository.

Deploy the replicated version to a VM with failure propagation enabled:

```powershell
$versionId = '/subscriptions/<subscription-id>/resourceGroups/<gallery-rg>/providers/Microsoft.Compute/galleries/<gallery>/applications/BGInfoPlus/versions/<version>'
az vm application set `
  --resource-group '<vm-resource-group>' `
  --name '<vm-name>' `
  --app-version-ids $versionId `
  --treat-deployment-as-failure true
```

Only one version of a given VM Application can be assigned to a VM at a time. The VM needs the Azure VM Agent, and the application version must be replicated to its region. For a VM-specific design, set `configurationReference` on that VM's gallery application profile; it takes precedence over the version default. See Microsoft's guidance for [creating and deploying VM Applications](https://learn.microsoft.com/azure/virtual-machines/vm-applications-how-to) and [publishing with managed identity](https://learn.microsoft.com/azure/virtual-machines/vm-applications-publish-with-managed-identity).

## Files and Troubleshooting

- Per-user configuration: `HKCU\Software\BGInfoPlus`.
- Shared all-users template: `%ProgramData%\BGInfoPlus\config.json`.
- Generated wallpaper: `%LOCALAPPDATA%\BGInfoPlus\wallpaper.bmp`.
- Automatic-apply log: `%LOCALAPPDATA%\BGInfoPlus\BGInfoPlus.log`.

Automatic-apply failures are logged and return a nonzero exit code. An interactive all-users deployment leaves the PowerShell window open after an error; Azure VM Application operations remain noninteractive and report command output through the VM Application logs.

Rendering is limited to 3840 × 2160. UserData accepts UTF-8 JSON or Base64-encoded UTF-8 JSON, is read but never executed, and is limited to 256 KiB, 256 scalar values, and 4,096 characters per value. Windows wallpaper-fit settings and administrator policy still control how the generated image is displayed or whether it can be applied.