# Configuration model

BGInfoPlus uses one JSON configuration format at every scope. Scope is determined only by the file's well-known location.

## Locations and precedence

| Scope | Location | Meaning |
| --- | --- | --- |
| All users | `%ProgramData%\BGInfoPlus\config.json` | Enforced machine configuration. It wins over every personal file. |
| Current user | `%LOCALAPPDATA%\BGInfoPlus\config.json` | The signed-in user's editable configuration. |
| Default | `DefaultConfig.json` beside `BGInfoPlus.exe` | Portable fallback for a user who has no personal configuration. |
| Built in | Embedded in `BGInfoPlus.exe` | Starter design used only when none of the files above exists. |

Automatic application resolves files in this order:

```text
AllUsers → User → Default → no wallpaper change
```

The editor uses the same effective order, except that it creates the built-in starter design when no file exists. An invalid higher-precedence file is reported and logged; BGInfoPlus does not silently fall through to a lower scope because that could bypass an administrator's all-users policy.

The Default scope is application behavior, not a modification of `C:\Users\Default`. It moves with the executable and is suitable for an MSI installation, a portable folder, or an image. BGInfoPlus deliberately avoids editing the Default profile hive.

## Choosing a scope

### One user

Open the editor and select **Apply now**. BGInfoPlus atomically saves `%LOCALAPPDATA%\BGInfoPlus\config.json` and applies it. If **Always apply at login** is selected, BGInfoPlus creates `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\BGInfoPlus.lnk`. Clearing the option and applying again removes that shortcut.

When the editor opens, it remembers the signed-in user's current Personalization background mode in memory. The snapshot covers Picture (including per-monitor paths and positioning), Solid color, Slideshow (source items, interval, shuffle, and positioning), and Windows spotlight (the mode selector and current image). Hold **Alt** while selecting **Apply now** to restore that launch-time wallpaper configuration. This is a session-only undo: the snapshot is not written to disk, and it is unavailable if Windows prevented BGInfoPlus from reading the settings at launch.

The executable path is stored in the shortcut, so a portable installation must remain at a stable path.

### Users without a personal configuration

Select **Export config…**, then copy the exported file beside `BGInfoPlus.exe` and name it exactly `DefaultConfig.json`. A personal file overrides this fallback. **Import config…** can load the file into the editor later without saving over the user's configuration until **Apply now** is selected.

The MSI already installs a release `DefaultConfig.json` and registers the interactive machine logon task. For a portable folder, place the file first and then run the deployment helper once from an elevated prompt with `-PreserveConfiguration` to register the task without creating an enforced ProgramData configuration. This is appropriate when every new lab user should start with the same design but may customize it later.

### All users

Select **Deploy for all users…** and approve elevation. BGInfoPlus writes ProgramData `config.json` and registers the same machine logon task. This file is enforced at logon and overrides personal and default files.

This is the appropriate mode for a reference image or lab where every student must receive the same centrally selected layout. Placeholders are still resolved separately inside each student's session.

## Logon behavior

Personal automatic application uses the user's Startup folder and requires no elevation. MSI setup, Azure VM Application setup, **Deploy for all users…**, or the portable helper's `-PreserveConfiguration` option can register **BGInfoPlus - Apply wallpaper at logon**, a Task Scheduler task with:

- an `AtLogOn` trigger delayed by ten seconds;
- the well-known `INTERACTIVE` group (`S-1-5-4`) as a limited principal;
- `BGInfoPlus.exe --apply` as its action; and
- a two-minute execution limit with duplicate instances ignored.

At runtime `--apply` resolves the effective file again. The task therefore does not need to be replaced when an administrator adds or removes enforced AllUsers policy. RDP and Azure Bastion sessions include the Interactive SID and are covered.

## Export and automation

**Import config…** validates a selected JSON file and loads its document, placeholders, resolution, fit mode, and colors into the editor and its live design surface. It does not overwrite the personal file until **Apply now** is selected.

**Export config…** writes the current design and placeholder mappings to a JSON file without per-user startup policy. The resulting file can be copied as application-local `DefaultConfig.json`, archived with a lab definition, used as an Azure VM Application `configurationReference`, or installed as enforced policy with the deployment helper.

Examples from an elevated PowerShell prompt in the installation directory:

```powershell
# Register the logon task while preserving adjacent DefaultConfig.json.
./Deploy-BGInfoPlusForAllUsers.ps1 -PreserveConfiguration

# Enforce the same template for every user.
./Deploy-BGInfoPlusForAllUsers.ps1 -TemplatePath C:\Lab\BGInfoPlus.json -Force

# Remove enforced policy and the machine logon task. The adjacent fallback remains.
./Deploy-BGInfoPlusForAllUsers.ps1 -Remove
```

For application automation:

```text
BGInfoPlus.exe --apply
BGInfoPlus.exe --apply --config <explicit-file>
BGInfoPlus.exe --export-config <destination>
BGInfoPlus.exe --install-config <source> <destination>
```

All file input goes through the same size and structure validation. Writes use a same-directory temporary file followed by atomic replacement.

## JSON fields and limits

The JSON object contains:

- `Document`: the serialized rich-text layout;
- `Placeholders`: named data-source mappings;
- `RunAtSignIn`: a personal-scope preference, normalized to `false` when exported or installed as a machine template;
- `ImageWidth` and `ImageHeight`;
- `BackgroundColor`; and
- `WallpaperFit`: `Fill`, `Fit`, or `Stretch`.

The editor initially selects **Current** output resolution even when a saved configuration used another size. The adjacent label shows the familiar image proportion, such as `16:9`. Importing a configuration explicitly selects its saved resolution. `WallpaperFit` is optional and defaults to `Fill` when omitted. Unknown JSON properties, numeric fit values, null placeholder members, and colors outside `#RRGGBB` format are rejected.

Configuration files are limited to 4 MiB, documents to 1,048,576 characters, placeholders to 128, and rendered images to 3840 × 2160. See `AppLimits.cs` for the authoritative bounds.

## Installation and removal

The MSI and Azure VM Application install their packaged `DefaultConfig.json` beside the executable and register the machine task. Users may create personal files that override this fallback. An Azure VM Application deployment installs enforced AllUsers scope only when it explicitly supplies a `configurationReference`.

Installer-managed hashes allow Azure VM Application upgrades with `configurationReference` to refresh unchanged enforced policy while preserving a file an administrator modified after installation. An update without a deployment configuration preserves existing policy. Uninstall removes installed application files, machine policy, the machine task, and BGInfoPlus Startup shortcuts. Personal JSON files, generated wallpapers, and logs are preserved.
