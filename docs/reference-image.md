# Preparing a BGInfoPlus reference image

This workflow prepares a Windows reference VM so that BGInfoPlus automatically generates and applies the same layout for every user beginning with the first logon after Sysprep. The layout is shared, but placeholders are resolved again in each user's session and the generated wallpaper remains per-user.

## 1. Install BGInfoPlus

Install the Azure VM Application package, or extract the portable ZIP into a permanent, administrator-controlled directory. The Azure package uses:

```text
C:\Program Files\BGInfoPlus\BGInfoPlus.exe
```

Do not prepare the image from the standalone EXE because it does not include the all-users deployment helper. Do not place the portable package in a profile or temporary directory; the scheduled task must still be able to find it after Sysprep.

## 2. Design and test the wallpaper

1. Sign in to the reference VM using the image-preparation administrator account.
2. Open BGInfoPlus from the Start menu.
3. Build the layout and add the required placeholders.
4. Select **Preview** and verify the result.
5. Select **Apply wallpaper** once. This validates and saves the design for the preparation account.

Avoid UserData paths or other file references inside the preparation account's profile. Use stable machine-wide paths, such as a protected location below `%ProgramData%`, if the shared layout depends on external files.

## 3. Deploy the design for all users

Select **Deploy for all users…** and approve the elevation prompt. BGInfoPlus then:

- exports the validated design to `%ProgramData%\BGInfoPlus\config.json`;
- grants ordinary users read-only access to that directory;
- removes the preparation account's duplicate `HKCU` automatic-start entry; and
- registers **BGInfoPlus - Apply wallpaper at logon** in Task Scheduler.

The task uses the well-known INTERACTIVE group with limited privileges. It starts ten seconds after any interactive user logs on, so the wallpaper is changed in the correct desktop session rather than in the SYSTEM session.

### RDP and Azure Bastion students

The `INTERACTIVE` task principal covers both console and Remote Desktop users. Windows adds the `Remote Interactive Logon` SID (`S-1-5-14`) to an RDP user's access token, and Microsoft documents that such tokens also contain the `Interactive` SID (`S-1-5-4`). Azure Bastion connects to Windows VMs using RDP, whether the student uses the browser experience or a supported native client, so those sessions activate this task as well. This is independent of whether Windows authenticates the student with a local account, a domain account, or a supported Microsoft Entra ID RDP flow.

The trigger fires when Windows creates a logon session. Reconnecting to an existing disconnected RDP session is not a new logon and does not run the task again; the wallpaper generated at the original logon remains in that profile and session. Signing out and signing in creates a new session and runs BGInfoPlus again.

To verify the token from the same connection method students will use, sign in to a test VM through RDP or Bastion and run:

```cmd
whoami /groups | findstr /i "S-1-5-4 S-1-5-14"
```

The output should include `NT AUTHORITY\INTERACTIVE` and, for RDP, `NT AUTHORITY\REMOTE INTERACTIVE LOGON`.

Verify the deployment from an elevated PowerShell prompt:

```powershell
Get-ScheduledTask -TaskName 'BGInfoPlus - Apply wallpaper at logon' | Get-ScheduledTaskInfo
Get-Item "$env:ProgramData\BGInfoPlus\config.json"
```

For a practical validation, create or use a disposable non-administrator account, sign in, wait approximately ten seconds, and confirm that `%LOCALAPPDATA%\BGInfoPlus\wallpaper.bmp` appears and the wallpaper changes. Review `%LOCALAPPDATA%\BGInfoPlus\BGInfoPlus.log` if it does not.

## 4. Generalize and shut down

Close BGInfoPlus and run Sysprep interactively from an elevated administrator command prompt:

```cmd
%WINDIR%\System32\Sysprep\Sysprep.exe /generalize /oobe /shutdown
```

Add `/unattend:C:\path\to\unattend.xml` when your image process uses an answer file. Do not restart and sign into the generalized reference VM before capturing it. Capture the stopped VM or disk using your normal image workflow.

Do not run Sysprep as SYSTEM through Task Scheduler or PsExec. Microsoft documents that scenario as unsupported.

## 5. First boot from the image

Windows specializes the deployed image and completes OOBE according to your provisioning method. No BGInfoPlus action is required during the `specialize` pass. At the first interactive logon:

1. Windows creates or loads the user's profile.
2. Task Scheduler waits ten seconds.
3. BGInfoPlus reads the protected machine-wide template.
4. Placeholders are resolved for the deployed VM and signed-in user.
5. The image is written to `%LOCALAPPDATA%\BGInfoPlus\wallpaper.bmp` and applied.

The same sequence occurs for every later user. Updating `%ProgramData%\BGInfoPlus\config.json` through **Deploy for all users…** changes the template used at subsequent logons.

## Removal or reconfiguration

To replace the shared design, edit it under an administrator account and select **Deploy for all users…** again. The existing task and template are replaced safely.

To disable only the all-users behavior while keeping the application installed, run from an elevated PowerShell prompt:

```powershell
& "$env:ProgramFiles\BGInfoPlus\Deploy-BGInfoPlusForAllUsers.ps1" -Remove
```

Uninstalling the Azure VM Application also removes the task and shared template. Existing per-user wallpapers, logs, and private configurations are preserved.
