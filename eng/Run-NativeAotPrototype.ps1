[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot 'src\OmniTray\OmniTray.csproj'
$framework = 'net10.0-windows10.0.19041.0'
$runtimeIdentifier = 'win-x64'
$releaseRoot = Join-Path $repositoryRoot "src\OmniTray\bin\x64\Release\$framework\$runtimeIdentifier"
$publishDirectory = Join-Path $repositoryRoot "src\OmniTray\bin\Release\$framework\$runtimeIdentifier\publish"
$manifestPath = Join-Path $releaseRoot 'AppxManifest.xml'
$looseLayoutPath = Join-Path $releaseRoot 'AppX-Aot'

Push-Location $repositoryRoot
try
{
    & dotnet build $projectPath -c Release -p:Platform=x64 -p:RuntimeIdentifier=$runtimeIdentifier
    if ($LASTEXITCODE -ne 0)
    {
        throw "Release build failed with exit code $LASTEXITCODE."
    }

    & dotnet publish $projectPath -c Release -p:Platform=x64 -r $runtimeIdentifier --no-restore
    if ($LASTEXITCODE -ne 0)
    {
        throw "Native AOT publish failed with exit code $LASTEXITCODE."
    }

    $publishedExecutable = Join-Path $publishDirectory 'OmniTray.exe'
    if (-not (Test-Path -LiteralPath $publishedExecutable) -or -not (Test-Path -LiteralPath $manifestPath))
    {
        throw 'The native executable or generated package manifest was not produced.'
    }

    [xml]$project = Get-Content -LiteralPath $projectPath -Raw
    $winAppReference = $project.SelectNodes('/Project/ItemGroup/PackageReference') |
        Where-Object Include -EQ 'Microsoft.Windows.SDK.BuildTools.WinApp' |
        Select-Object -First 1
    if ($null -eq $winAppReference)
    {
        throw 'Microsoft.Windows.SDK.BuildTools.WinApp is not referenced by the app project.'
    }

    $globalPackagesLine = (& dotnet nuget locals global-packages --list | Select-Object -First 1).Trim()
    $globalPackagesPath = $globalPackagesLine -replace '^[^:]+:\s*', ''
    $winAppCli = Join-Path $globalPackagesPath "microsoft.windows.sdk.buildtools.winapp\$($winAppReference.Version)\tools\win-x64\winapp.exe"
    if (-not (Test-Path -LiteralPath $winAppCli))
    {
        throw "Windows App Development CLI was not found at '$winAppCli'."
    }

    $env:WINAPP_CLI_TELEMETRY_OPTOUT = '1'
    & $winAppCli run $publishDirectory `
        --manifest $manifestPath `
        --output-appx-directory $looseLayoutPath `
        --exe OmniTray.exe `
        --detach
    if ($LASTEXITCODE -ne 0)
    {
        throw "Native AOT launch failed with exit code $LASTEXITCODE."
    }
}
finally
{
    Pop-Location
}
