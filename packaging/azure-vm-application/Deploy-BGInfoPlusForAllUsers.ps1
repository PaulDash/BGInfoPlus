#Requires -Version 5.1
<#
.SYNOPSIS
Installs or removes a machine-scoped BGInfoPlus JSON configuration and logon task.

.DESCRIPTION
Default scope supplies a fallback only when a user has no private configuration.
AllUsers scope is enforced and takes precedence over every private configuration.
Both modes use the same interactive logon task and validated JSON format.
#>
[CmdletBinding()]
param(
    [ValidateSet('Default', 'AllUsers')]
    [string]$Scope = 'AllUsers',
    [switch]$Remove,
    [string]$TemplatePath,
    [switch]$PreserveConfiguration,
    [switch]$Force,
    [switch]$ValidateOnly,
    [switch]$PauseOnError
)

$ErrorActionPreference = 'Stop'
trap {
    if ($PauseOnError) {
        Write-Host "`nBGInfoPlus machine deployment failed: $_" -ForegroundColor Red
        Read-Host 'Press Enter to close this window'
        exit 1
    }
    throw
}

function New-BGInfoPlusLogonTask([string]$Executable, [string]$WorkingDirectory) {
    $action = New-ScheduledTaskAction -Execute $Executable -Argument '--apply' -WorkingDirectory $WorkingDirectory
    $trigger = New-ScheduledTaskTrigger -AtLogOn
    $trigger.Delay = 'PT10S'
    # S-1-5-4 is the well-known INTERACTIVE group. RDP tokens also contain this SID.
    $taskPrincipal = New-ScheduledTaskPrincipal -GroupId 'S-1-5-4' -RunLevel Limited
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable `
        -ExecutionTimeLimit (New-TimeSpan -Minutes 2) -MultipleInstances IgnoreNew
    New-ScheduledTask -Action $action -Trigger $trigger -Principal $taskPrincipal -Settings $settings `
        -Description 'Applies the effective BGInfoPlus JSON configuration in each interactive user session.'
}

if ($ValidateOnly) {
    $validationTask = New-BGInfoPlusLogonTask (Join-Path $PSScriptRoot 'BGInfoPlus.exe') ([IO.Path]::GetFullPath($PSScriptRoot))
    if ($validationTask.Actions.Arguments -ne '--apply' -or $validationTask.Triggers.Delay -ne 'PT10S' -or
        $validationTask.Principal.LogonType -ne 'Group' -or $validationTask.Principal.RunLevel -ne 'Limited') {
        throw 'The generated BGInfoPlus logon task does not match its required security and timing settings.'
    }
    Write-Host 'BGInfoPlus interactive logon task definition is valid.'
    return
}

$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Deploying a machine BGInfoPlus configuration requires an elevated process.'
}

$taskName = 'BGInfoPlus - Apply wallpaper at logon'
$installDirectory = [IO.Path]::GetFullPath($PSScriptRoot)
$executable = Join-Path $installDirectory 'BGInfoPlus.exe'
$dataDirectory = Join-Path $env:ProgramData 'BGInfoPlus'
$allUsersPath = Join-Path $dataDirectory 'config.json'
$defaultPath = Join-Path $dataDirectory 'default-config.json'
$configurationPath = if ($Scope -eq 'Default') { $defaultPath } else { $allUsersPath }
$managedHashPath = Join-Path $dataDirectory (if ($Scope -eq 'Default') { 'default-config.source.sha256' } else { 'config.source.sha256' })

if ($Remove) {
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
    foreach ($path in @($allUsersPath, $defaultPath, (Join-Path $dataDirectory 'config.source.sha256'),
            (Join-Path $dataDirectory 'default-config.source.sha256'))) {
        Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
    }
    Write-Host 'The BGInfoPlus machine logon deployment was removed.'
    return
}

if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw "BGInfoPlus was not found at $executable." }
[IO.Directory]::CreateDirectory($dataDirectory) | Out-Null

if ($TemplatePath) {
    $source = [IO.Path]::GetFullPath($TemplatePath)
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "The BGInfoPlus configuration was not found at $source." }
    $installTemplate = $Force -or -not (Test-Path -LiteralPath $configurationPath -PathType Leaf)
    if (-not $installTemplate -and (Test-Path -LiteralPath $managedHashPath -PathType Leaf)) {
        $managedHash = (Get-Content -LiteralPath $managedHashPath -Raw).Trim()
        $currentHash = (Get-FileHash -LiteralPath $configurationPath -Algorithm SHA256).Hash
        $installTemplate = $managedHash -match '^[A-Fa-f0-9]{64}$' -and $currentHash -eq $managedHash
    }
    if ($installTemplate) {
        & $executable --install-config $source $configurationPath
        if ($LASTEXITCODE -ne 0) { throw 'BGInfoPlus rejected the supplied configuration. See the invoking user''s BGInfoPlus.log.' }
        (Get-FileHash -LiteralPath $configurationPath -Algorithm SHA256).Hash |
            Set-Content -LiteralPath $managedHashPath -Encoding ascii
    } else {
        Write-Host "Preserved administrator-modified configuration at $configurationPath"
    }
} elseif (-not $PreserveConfiguration) {
    & $executable --export-config $configurationPath
    if ($LASTEXITCODE -ne 0) {
        throw 'BGInfoPlus could not export the effective configuration. See the invoking user''s BGInfoPlus.log.'
    }
    (Get-FileHash -LiteralPath $configurationPath -Algorithm SHA256).Hash |
        Set-Content -LiteralPath $managedHashPath -Encoding ascii
} elseif (-not (Test-Path -LiteralPath $allUsersPath) -and -not (Test-Path -LiteralPath $defaultPath)) {
    throw 'No existing machine BGInfoPlus configuration was found to preserve.'
}

# Administrators and SYSTEM may modify templates; ordinary users can only read them.
& icacls.exe $dataDirectory /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)(F)' '*S-1-5-32-544:(OI)(CI)(F)' '*S-1-5-32-545:(OI)(CI)(RX)' /T /C | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not secure the BGInfoPlus machine configuration directory.' }

# The executable resolves AllUsers > User > Default on every run. Keeping the task
# action path-free allows administrators to switch scope without recreating the task.
$task = New-BGInfoPlusLogonTask $executable $installDirectory
Register-ScheduledTask -TaskName $taskName -InputObject $task -Force | Out-Null

# Remove both legacy and current per-user launch points for the deploying account;
# the machine task now owns logon execution for it.
Remove-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name BGInfoPlus -ErrorAction SilentlyContinue
Remove-Item -LiteralPath (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Startup\BGInfoPlus.lnk') `
    -Force -ErrorAction SilentlyContinue

Write-Host "$Scope configuration: $configurationPath"
Write-Host "Scheduled task: $taskName"
if ($Scope -eq 'Default' -and (Test-Path -LiteralPath $allUsersPath)) {
    Write-Warning 'An AllUsers configuration also exists and takes precedence over this default.'
}
Write-Host 'BGInfoPlus will apply the effective configuration 10 seconds after each interactive logon.'
