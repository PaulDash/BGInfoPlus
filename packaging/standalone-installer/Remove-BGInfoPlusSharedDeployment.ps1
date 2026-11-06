#Requires -Version 5.1
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Unregister-ScheduledTask -TaskName 'BGInfoPlus - Apply wallpaper at logon' -Confirm:$false -ErrorAction SilentlyContinue
Remove-Item -LiteralPath (Join-Path $env:ProgramData 'BGInfoPlus\config.json') -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath (Join-Path $env:ProgramData 'BGInfoPlus\config.source.sha256') -Force -ErrorAction SilentlyContinue

Get-ChildItem -Path Registry::HKEY_USERS -ErrorAction SilentlyContinue |
    Where-Object { $_.PSChildName -match '^S-1-5-21-.+' } |
    ForEach-Object {
        $runKey = Join-Path $_.PSPath 'Software\Microsoft\Windows\CurrentVersion\Run'
        Remove-ItemProperty -LiteralPath $runKey -Name BGInfoPlus -ErrorAction SilentlyContinue
    }