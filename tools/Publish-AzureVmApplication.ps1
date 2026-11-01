#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$ResourceGroupName,
    [Parameter(Mandatory)] [string]$GalleryName,
    [Parameter(Mandatory)] [string]$Location,
    [string[]]$TargetRegions = @($Location),
    [string]$ApplicationName = 'BGInfoPlus',
    [string]$Version = '1.0.2',
    [string]$PackagePath,
    [string]$PackageUri,
    [string]$StorageAccountName,
    [string]$StorageResourceGroup = $ResourceGroupName,
    [string]$ContainerName = 'vm-applications'
)

$ErrorActionPreference = 'Stop'
function Invoke-AzCli {
    param([Parameter(ValueFromRemainingArguments)] [string[]]$Arguments)
    & az @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Azure CLI failed with exit code $LASTEXITCODE." }
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($PackagePath)) {
    $PackagePath = Join-Path $repositoryRoot "artifacts\release\v$Version\BGInfoPlus-$Version-azure-vmapp.zip"
}
$PackagePath = [IO.Path]::GetFullPath($PackagePath)
if (-not (Test-Path -LiteralPath $PackagePath -PathType Leaf)) { throw "Package not found: $PackagePath" }

$manifestPath = Join-Path ([IO.Path]::GetDirectoryName($PackagePath)) 'azure-vm-application.json'
if (-not (Test-Path -LiteralPath $manifestPath)) { throw "Manifest not found: $manifestPath" }
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.version -ne $Version) { throw "Manifest version $($manifest.version) does not match $Version." }

Invoke-AzCli account show --only-show-errors --output none
Invoke-AzCli group show --name $ResourceGroupName --only-show-errors --output none

if ([string]::IsNullOrWhiteSpace($PackageUri)) {
    if ([string]::IsNullOrWhiteSpace($StorageAccountName)) {
        throw 'Specify either -PackageUri (recommended with a gallery managed identity) or -StorageAccountName.'
    }
    Invoke-AzCli storage account show --name $StorageAccountName --resource-group $StorageResourceGroup --only-show-errors --output none
    Invoke-AzCli storage container create --name $ContainerName --account-name $StorageAccountName --auth-mode login --only-show-errors --output none
    $blobName = [IO.Path]::GetFileName($PackagePath)
    Invoke-AzCli storage blob upload --account-name $StorageAccountName --container-name $ContainerName --name $blobName --file $PackagePath --type page --overwrite true --auth-mode login --only-show-errors --output none
    $expiry = [DateTime]::UtcNow.AddDays(7).ToString('yyyy-MM-ddTHH:mmZ')
    $sas = (& az storage blob generate-sas --account-name $StorageAccountName --container-name $ContainerName --name $blobName --permissions r --expiry $expiry --https-only --auth-mode login --as-user --only-show-errors --output tsv)
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($sas)) { throw 'Could not generate a user-delegation SAS for the package.' }
    $PackageUri = "https://$StorageAccountName.blob.core.windows.net/$ContainerName/$blobName`?$sas"
    Write-Warning 'A seven-day read-only SAS is being used for initial gallery ingestion. A gallery user-assigned managed identity and plain blob URL are preferred for long-term operation.'
}

& az sig show --resource-group $ResourceGroupName --gallery-name $GalleryName --only-show-errors --output none 2>$null
if ($LASTEXITCODE -ne 0) {
    Invoke-AzCli sig create --resource-group $ResourceGroupName --gallery-name $GalleryName --location $Location --only-show-errors --output none
}

& az sig gallery-application show --resource-group $ResourceGroupName --gallery-name $GalleryName --application-name $ApplicationName --only-show-errors --output none 2>$null
if ($LASTEXITCODE -ne 0) {
    Invoke-AzCli sig gallery-application create --resource-group $ResourceGroupName --gallery-name $GalleryName --application-name $ApplicationName --location $Location --os-type Windows --description 'Azure-focused system information wallpaper utility.' --only-show-errors --output none
}

& az sig gallery-application version show --resource-group $ResourceGroupName --gallery-name $GalleryName --application-name $ApplicationName --version-name $Version --only-show-errors --output none 2>$null
if ($LASTEXITCODE -eq 0) { throw "VM Application version $ApplicationName/$Version already exists." }

$targetRegionJson = @($TargetRegions | ForEach-Object {
    [ordered]@{ name = $_; regionalReplicaCount = 1; storageAccountType = 'Standard_LRS' }
}) | ConvertTo-Json -Compress

Invoke-AzCli sig gallery-application version create `
    --resource-group $ResourceGroupName `
    --gallery-name $GalleryName `
    --application-name $ApplicationName `
    --version-name $Version `
    --location $Location `
    --package-file-link $PackageUri `
    --package-file-name $manifest.packageFileName `
    --install-command $manifest.installCommand `
    --update-command $manifest.updateCommand `
    --remove-command $manifest.removeCommand `
    --target-regions $targetRegionJson `
    --only-show-errors --output none

$subscriptionId = (& az account show --query id --output tsv)
$versionId = "/subscriptions/$subscriptionId/resourceGroups/$ResourceGroupName/providers/Microsoft.Compute/galleries/$GalleryName/applications/$ApplicationName/versions/$Version"
Write-Host "Published VM Application version: $versionId"
Write-Host 'Deploy with --treat-deployment-as-failure true.'
