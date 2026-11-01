#Requires -Version 5.1
[CmdletBinding()]
param([switch]$Remove)

$ErrorActionPreference = 'Stop'
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Deploying BGInfoPlus for all users requires an elevated process.'
}

$taskName = 'BGInfoPlus - Apply wallpaper at logon'
$installDirectory = [IO.Path]::GetFullPath($PSScriptRoot)
$executable = Join-Path $installDirectory 'BGInfoPlus.exe'
$dataDirectory = Join-Path $env:ProgramData 'BGInfoPlus'
$configurationPath = Join-Path $dataDirectory 'config.json'

if ($Remove) {
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $configurationPath -Force -ErrorAction SilentlyContinue
    Write-Host 'The BGInfoPlus all-users logon deployment was removed.'
    return
}

if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw "BGInfoPlus was not found at $executable." }
[IO.Directory]::CreateDirectory($dataDirectory) | Out-Null

& $executable --export-config $configurationPath
if ($LASTEXITCODE -ne 0) {
    throw "BGInfoPlus could not export the current user's saved configuration. Apply the wallpaper once in the editor, then try again. See %LOCALAPPDATA%\BGInfoPlus\BGInfoPlus.log."
}

# Administrators and SYSTEM may modify the shared template; ordinary users can only read it.
& icacls.exe $dataDirectory /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)(F)' '*S-1-5-32-544:(OI)(CI)(F)' '*S-1-5-32-545:(OI)(CI)(RX)' /T /C | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not secure the shared BGInfoPlus configuration directory.' }

$action = New-ScheduledTaskAction -Execute $executable -Argument "--apply --config `"$configurationPath`"" -WorkingDirectory $installDirectory
$trigger = New-ScheduledTaskTrigger -AtLogOn
$trigger.Delay = 'PT10S'
# S-1-5-4 is the well-known INTERACTIVE group. Group activation uses the token of each signed-in user.
$taskPrincipal = New-ScheduledTaskPrincipal -GroupId 'S-1-5-4' -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable `
    -ExecutionTimeLimit (New-TimeSpan -Minutes 2) -MultipleInstances IgnoreNew
$task = New-ScheduledTask -Action $action -Trigger $trigger -Principal $taskPrincipal -Settings $settings `
    -Description 'Generates and applies the shared BGInfoPlus wallpaper in each interactive user session.'
Register-ScheduledTask -TaskName $taskName -InputObject $task -Force | Out-Null

Write-Host "Shared configuration: $configurationPath"
Write-Host "Scheduled task: $taskName"
Write-Host 'BGInfoPlus is ready for Sysprep. It will run 10 seconds after each user logs on.'
