#Requires -Version 5.1
[CmdletBinding()]
param([switch]$Remove, [string]$TemplatePath, [switch]$PreserveConfiguration, [switch]$PauseOnError)

$ErrorActionPreference = 'Stop'
trap {
    if ($PauseOnError) {
        Write-Host "`nBGInfoPlus all-users deployment failed: $_" -ForegroundColor Red
        Read-Host 'Press Enter to close this window'
        exit 1
    }
    throw
}
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

if ($TemplatePath) {
    if (-not (Test-Path -LiteralPath $TemplatePath -PathType Leaf)) { throw "The BGInfoPlus configuration was not found at $TemplatePath." }
    & $executable --install-config ([IO.Path]::GetFullPath($TemplatePath)) $configurationPath
    if ($LASTEXITCODE -ne 0) { throw 'BGInfoPlus rejected the supplied shared configuration. See %LOCALAPPDATA%\BGInfoPlus\BGInfoPlus.log.' }
} elseif (-not $PreserveConfiguration) {
    & $executable --export-config $configurationPath
    if ($LASTEXITCODE -ne 0) {
        throw "BGInfoPlus could not export the current user's saved configuration. Apply the wallpaper once in the editor, then try again. See %LOCALAPPDATA%\BGInfoPlus\BGInfoPlus.log."
    }
} elseif (-not (Test-Path -LiteralPath $configurationPath -PathType Leaf)) {
    throw 'The existing shared BGInfoPlus configuration could not be preserved because it was not found.'
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
Remove-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name BGInfoPlus -ErrorAction SilentlyContinue

Write-Host "Shared configuration: $configurationPath"
Write-Host "Scheduled task: $taskName"
Write-Host 'BGInfoPlus is ready for Sysprep. It will run 10 seconds after each user logs on.'
