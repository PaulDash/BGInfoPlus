#Requires -Version 5.1
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'BGInfoPlus must be removed from an elevated process.'
}

$installDirectory = [IO.Path]::GetFullPath((Join-Path $env:ProgramFiles 'BGInfoPlus'))
$destinationExe = Join-Path $installDirectory 'BGInfoPlus.exe'

Unregister-ScheduledTask -TaskName 'BGInfoPlus - Apply wallpaper at logon' -Confirm:$false -ErrorAction SilentlyContinue
Remove-Item -LiteralPath (Join-Path $env:ProgramData 'BGInfoPlus\config.json') -Force -ErrorAction SilentlyContinue

Get-Process -Name BGInfoPlus -ErrorAction SilentlyContinue | ForEach-Object {
    try {
        if ([IO.Path]::GetFullPath($_.MainModule.FileName) -eq $destinationExe) { Stop-Process -Id $_.Id -Force }
    }
    catch { }
}

Remove-Item -LiteralPath (Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\BGInfoPlus.lnk') -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\BGInfoPlus' -Recurse -Force -ErrorAction SilentlyContinue

# Remove login entries from currently loaded user hives. Per-user layouts and generated wallpapers are preserved.
Get-ChildItem -Path Registry::HKEY_USERS -ErrorAction SilentlyContinue |
    Where-Object { $_.PSChildName -match '^S-1-5-21-.+' } |
    ForEach-Object {
        $runKey = Join-Path $_.PSPath 'Software\Microsoft\Windows\CurrentVersion\Run'
        Remove-ItemProperty -LiteralPath $runKey -Name BGInfoPlus -ErrorAction SilentlyContinue
    }

foreach ($fileName in @('BGInfoPlus.exe', 'BGInfoPlus.exe.new', 'BGInfoPlus.exe.previous', 'Remove-BGInfoPlus.ps1', 'Deploy-BGInfoPlusForAllUsers.ps1')) {
    Remove-Item -LiteralPath (Join-Path $installDirectory $fileName) -Force -ErrorAction SilentlyContinue
}
Remove-Item -LiteralPath $installDirectory -Force -ErrorAction SilentlyContinue

Write-Host 'BGInfoPlus removed. Per-user configuration and wallpaper files were preserved.'
