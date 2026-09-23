<#
    Builds Release and produces dist\TroopTooltipSorter-v<version>.zip.

    The zip holds the module folder at its root:

        TroopTooltipSorter\SubModule.xml
        TroopTooltipSorter\bin\Win64_Shipping_Client\TroopTooltipSorter.dll

    so it can be dropped straight into the game's Modules folder, or handed to a
    mod manager. The version comes from module\SubModule.xml, which is the copy
    the game actually reads.
#>

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

[xml]$manifest = Get-Content (Join-Path $root 'module\SubModule.xml')
$moduleName = $manifest.Module.Id.value
$version = $manifest.Module.Version.value

# SkipDeploy so packing never disturbs the installed copy in the game folder.
& dotnet build (Join-Path $root 'src\TroopTooltipSorter\TroopTooltipSorter.csproj') -c Release --nologo -p:SkipDeploy=true
if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE" }

$dist = Join-Path $root 'dist'
$staging = Join-Path $dist "staging\$moduleName"
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
$binDir = Join-Path $staging 'bin\Win64_Shipping_Client'
New-Item -ItemType Directory -Force -Path $binDir | Out-Null

Copy-Item (Join-Path $root 'module\SubModule.xml') $staging
Copy-Item (Join-Path $root 'src\TroopTooltipSorter\bin\Release\TroopTooltipSorter.dll') $binDir

$zip = Join-Path $dist "$moduleName-$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $staging -DestinationPath $zip
Remove-Item (Join-Path $dist 'staging') -Recurse -Force

Write-Host "Packed $zip"
