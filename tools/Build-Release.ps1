#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectPath = Join-Path $repositoryRoot 'src\BGInfoPlus\BGInfoPlus.csproj'
[xml]$project = Get-Content -LiteralPath $projectPath
$projectVersion = [string]$project.Project.PropertyGroup.Version
if ($projectVersion -ne $Version) { throw "Project version is $projectVersion, but release version is $Version." }

$releaseRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot "artifacts\release\v$Version"))
$expectedParent = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts\release'))
if (-not $releaseRoot.StartsWith($expectedParent + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Release output resolved outside the expected artifacts directory.'
}
if (Test-Path -LiteralPath $releaseRoot) { Remove-Item -LiteralPath $releaseRoot -Recurse -Force }
[IO.Directory]::CreateDirectory($releaseRoot) | Out-Null

& dotnet run --project (Join-Path $repositoryRoot 'tests\BGInfoPlus.Tests\BGInfoPlus.Tests.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Release tests failed.' }

$publishRoot = Join-Path $releaseRoot 'publish'
& dotnet publish $projectPath -c Release -r win-x64 --self-contained true -o $publishRoot
if ($LASTEXITCODE -ne 0) { throw 'Release publish failed.' }

$publishedExe = Join-Path $publishRoot 'BGInfoPlus.exe'
$publishedVersion = (Get-Item -LiteralPath $publishedExe).VersionInfo.FileVersion
if ($publishedVersion -ne "$Version.0") { throw "Published file version is $publishedVersion, expected $Version.0." }
$defaultConfiguration = Join-Path $releaseRoot 'BGInfoPlus-DefaultConfig.json'
& $publishedExe --export-default-config $defaultConfiguration
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $defaultConfiguration -PathType Leaf)) {
    throw 'Could not generate the default BGInfoPlus configuration.'
}

$installerProject = Join-Path $repositoryRoot 'packaging\standalone-installer\BGInfoPlus.Setup.wixproj'
$standaloneMsi = Join-Path $releaseRoot "BGInfoPlus-$Version-win-x64.msi"
& dotnet build $installerProject -c Release -p:ProductVersion=$Version -p:PublishDirectory=$publishRoot -p:RepositoryRoot=$repositoryRoot -p:OutputName="BGInfoPlus-$Version-win-x64" -o $releaseRoot
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $standaloneMsi -PathType Leaf)) { throw 'MSI build failed.' }

$standaloneExe = Join-Path $releaseRoot "BGInfoPlus-$Version-win-x64.exe"
Copy-Item -LiteralPath $publishedExe -Destination $standaloneExe

$portableStage = Join-Path $releaseRoot 'portable'
[IO.Directory]::CreateDirectory($portableStage) | Out-Null
Copy-Item -LiteralPath $publishedExe -Destination (Join-Path $portableStage 'BGInfoPlus.exe')
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'packaging\azure-vm-application\Deploy-BGInfoPlusForAllUsers.ps1') -Destination $portableStage
Set-Content -LiteralPath (Join-Path $portableStage 'README.txt') -Encoding UTF8 -Value @"
BGInfoPlus $Version

Run BGInfoPlus.exe to configure, preview, export, and apply the wallpaper.
No separate .NET runtime is required. Configuration is stored for the current user.
Use "Deploy for all users" in the editor to create a protected shared template and logon task.
Documentation: https://github.com/PaulDash/BGInfoPlus
"@
$portableZip = Join-Path $releaseRoot "BGInfoPlus-$Version-win-x64.zip"
Compress-Archive -Path (Join-Path $portableStage '*') -DestinationPath $portableZip -CompressionLevel Optimal

$vmStage = Join-Path $releaseRoot 'azure-vmapp'
[IO.Directory]::CreateDirectory($vmStage) | Out-Null
Copy-Item -LiteralPath $publishedExe -Destination (Join-Path $vmStage 'BGInfoPlus.exe')
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'packaging\azure-vm-application\Install-BGInfoPlus.ps1') -Destination $vmStage
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'packaging\azure-vm-application\Remove-BGInfoPlus.ps1') -Destination $vmStage
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'packaging\azure-vm-application\Deploy-BGInfoPlusForAllUsers.ps1') -Destination $vmStage
$vmPackageName = "BGInfoPlus-$Version-azure-vmapp.zip"
$vmPackage = Join-Path $releaseRoot $vmPackageName
Compress-Archive -Path (Join-Path $vmStage '*') -DestinationPath $vmPackage -CompressionLevel Optimal

$expand = "if (Test-Path '.\BGInfoPlus-payload') { Remove-Item -LiteralPath '.\BGInfoPlus-payload' -Recurse -Force }; Expand-Archive -LiteralPath '.\$vmPackageName' -DestinationPath '.\BGInfoPlus-payload' -Force"
$configurationFileName = 'BGInfoPlus-config.json'
$installAction = "& '.\BGInfoPlus-payload\Install-BGInfoPlus.ps1' -ConfigurationPath '.\$configurationFileName'"
$removeAction = "& '.\BGInfoPlus-payload\Remove-BGInfoPlus.ps1'"
$installCommand = "powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command `"try { $expand; $installAction } catch { Write-Error `$_; exit 1 }`""
$removeCommand = "powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command `"try { $expand; $removeAction } catch { Write-Error `$_; exit 1 }`""
$manifest = [ordered]@{
    applicationName = 'BGInfoPlus'
    version = $Version
    supportedOsType = 'Windows'
    packageFileName = $vmPackageName
    configurationFileName = $configurationFileName
    installCommand = $installCommand
    updateCommand = $installCommand
    removeCommand = $removeCommand
}
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $releaseRoot 'azure-vm-application.json') -Encoding UTF8

$releaseFiles = @($standaloneExe, $standaloneMsi, $defaultConfiguration, $portableZip, $vmPackage, (Join-Path $releaseRoot 'azure-vm-application.json'))
$checksums = foreach ($file in $releaseFiles) {
    $hash = Get-FileHash -LiteralPath $file -Algorithm SHA256
    "$($hash.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($file))"
}
$checksums | Set-Content -LiteralPath (Join-Path $releaseRoot 'SHA256SUMS.txt') -Encoding ascii

Remove-Item -LiteralPath $publishRoot, $portableStage, $vmStage -Recurse -Force
Write-Host "Release artifacts created in $releaseRoot"
