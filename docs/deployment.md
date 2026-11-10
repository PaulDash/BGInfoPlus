# Installation and Deployment

## Install

BGInfoPlus is distributed as a machine-wide MSI, a portable ZIP, and a standalone EXE. The MSI installs to `%ProgramFiles%\BGInfoPlus`, adds a Start Menu shortcut, and supports Windows Installer upgrade and removal. It requires elevation. The portable ZIP needs no installation or separate .NET runtime and includes the all-users deployment helper. The standalone EXE does not include that helper.

The editor stores the current user's design at `%LOCALAPPDATA%\BGInfoPlus\config.json`. **Always apply at login** is opt-in; selecting it and applying creates a shortcut in the user's Windows Startup folder. At sign-in, BGInfoPlus refreshes the wallpaper without opening the editor. Clear the option and apply again to remove the shortcut.

The MSI installs `%ProgramFiles%\BGInfoPlus\DefaultConfig.json` and registers a delayed Task Scheduler task. This provides an immediate fallback for users without a personal file while still allowing personal customization. Replace it by exporting a configuration and copying it to that path with administrative permission. **Deploy for all users…** writes enforced `%ProgramData%\BGInfoPlus\config.json`; it takes precedence over personal and application-local files. Each user still gets a separately rendered wallpaper under `%LOCALAPPDATA%`. See [Configuration model](configuration.md).

For reference image preparation, follow [Preparing a reference image](reference-image.md).

## Uninstall

MSI removal deletes the machine task and machine templates while preserving per-user JSON, wallpapers, and logs.

## Build and Release

Publish a self-contained executable directly with the .NET 10 SDK:

```powershell
dotnet publish src/BGInfoPlus -c Release -r win-x64 -o artifacts/win-x64
```

Use `win-arm64` for ARM64 Windows. WPF prevents Native AOT; native runtime components may extract to the framework's per-user temporary location at launch.

Create a versioned release with:

```powershell
./tools/Build-Release.ps1 -Version 1.2.2
```

The version must match `Version` in `BGInfoPlus.csproj`. The script calls `tools/Test-BGInfoPlus.ps1`, publishes the executable, generates the default configuration, builds the MSI, and creates the MSI, standalone EXE, portable ZIP, Azure VM Application ZIP, default configuration JSON, command manifest, and SHA-256 checksums under `artifacts/release/v<version>`. It recreates that version directory, so use a new version rather than storing other files there.

After validation, the script asks whether to create the matching GitHub release. Answering yes requires a clean worktree, a checked-out branch, an authenticated GitHub CLI (`gh auth login`), and an `origin` remote on GitHub. It pushes the current branch, refuses to move an existing tag, creates `v<version>`, and uploads every validated artifact. Use `-SkipGitHubRelease` for non-interactive automation. The tagged GitHub Actions workflow uses that switch and safely refreshes release assets if the local workflow created the release first.

To remove compiled release directories older than the newest release and its two predecessors, review the targets and then run the cleanup:

```powershell
./tools/Clean-ReleaseArtifacts.ps1 -WhatIf
./tools/Clean-ReleaseArtifacts.ps1 -Confirm:$false
```

Only directories matching `artifacts/release/v<major>.<minor>.<patch>` are considered. Non-versioned directories and filesystem links are left untouched. Use `-KeepPrevious <count>` to retain a different number of predecessors.

## Azure VM Application

Each published version has a default configuration link. On a new VM install, BGInfoPlus validates that configuration, installs it as enforced AllUsers scope, secures it for ordinary-user read access, and registers the machine logon task. A VM's `configurationReference` overrides the version default. The downloaded configuration is named `BGInfoPlus-config.json` and is passed to the installer.

On update, an unchanged app-managed template is refreshed from the supplied configuration. If an administrator has modified or redeployed the enforced template, the update preserves it. To change a custom design, edit it and select **Deploy for all users…** again. Removal deletes the machine task and machine templates but preserves each user's private configuration and wallpaper.

Publish a release package with the interactive wizard:

```powershell
./tools/Publish-AzureVmApplication.ps1
```

The wizard requires `DefaultConfig.json` beside the VM Application ZIP. The storage-upload path uploads both files and creates seven-day, read-only user-delegation SAS URLs. The signed-in identity needs `Storage Blob Data Contributor` and `Storage Blob Delegator` on the storage account, plus access to the selected resource group. The managed-identity path asks for plain HTTPS blob URLs for both files; the gallery's user-assigned identity must be able to read them. The wizard reviews the plan before making Azure changes and does not save resource details or SAS values to the repository.

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

- Per-user configuration: `%LOCALAPPDATA%\BGInfoPlus\config.json`.
- Default fallback: `DefaultConfig.json` beside the running `BGInfoPlus.exe`.
- Enforced all-users configuration: `%ProgramData%\BGInfoPlus\config.json`.
- Generated wallpaper: `%LOCALAPPDATA%\BGInfoPlus\wallpaper.bmp`.
- Automatic-apply log: `%LOCALAPPDATA%\BGInfoPlus\BGInfoPlus.log`.

Automatic-apply failures are logged and return a nonzero exit code.
